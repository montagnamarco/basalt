using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Core.Model;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Completion;

namespace Basalt.Tests;

/// <summary>
/// Typing Visual Basic at the pace of a person, in a real window.
///
/// Each journey opens a file in the IDE, gives the keyboard to the editor and
/// types one key at a time with about 60 ms of UI time between keys. The
/// earlier tests called the editor's handlers directly, or typed in one burst,
/// and so never met the list as it is when someone is writing: the list was
/// cut to the first 200 names alphabetically before the typed letters narrowed
/// it, so a name was committed from whatever fuzzy match happened to be left.
/// </summary>
public sealed class CompletionTypingTests : IAsyncLifetime
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-typing-").FullName;

    private const string Caret = "§";

    private string ProgramPath => Path.Combine(_root, "Program.vb");

    public async ValueTask InitializeAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Probe.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_root, true); } catch { }

        return ValueTask.CompletedTask;
    }

    /// <summary>An open file, its editor, and the window that hosts them.</summary>
    private sealed record Journey(TestWindow Host, CodeEditor Editor) : IDisposable
    {
        public Window Window => Host.Window;

        public void Dispose() => Host.Dispose();
    }

    /// <summary>Opens Program.vb with the caret where the § marker stands.</summary>
    private Task<Journey> OpenAsync(string source) => OpenFileAsync("Program.vb", source);

    /// <summary>
    /// Opens a file of the probe project with the caret where the § marker
    /// stands. A web project, with the ASP.NET Core tag helpers in scope, is
    /// needed for the markup names that come from them.
    /// </summary>
    private async Task<Journey> OpenFileAsync(string relativePath, string source, bool web = false)
    {
        var caret = source.IndexOf(Caret, StringComparison.Ordinal);
        var code = source.Replace(Caret, "");

        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, code);

        if (web)
        {
            await File.WriteAllTextAsync(Path.Combine(_root, "Probe.vbproj"), """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RootNamespace></RootNamespace>
                  </PropertyGroup>
                </Project>
                """);
        }

        var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));

        for (var i = 0; i < 600 && (vm.StartupProject is null || vm.IsBusy); i++) await host.SettleAsync(1);

        await vm.OpenFileAsync(path);
        await host.SettleAsync(5);

        var editor = host.Window.GetVisualDescendants().OfType<CodeEditor>().First();

        editor.CaretOffsetForTests = caret >= 0 ? caret : code.Length;
        editor.GetVisualDescendants().OfType<TextEditor>().Single().TextArea.Focus();

        // The first Roslyn answer for a freshly opened file takes a moment.
        await host.SettleAsync(10);

        return new Journey(host, editor);
    }

    private static void Tap(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None, PhysicalKey physical = PhysicalKey.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }

    /// <summary>Types text as a person does: one key, then a short pause. "\n" is Enter and "\t" is Tab.</summary>
    private static async Task TypeAsync(Journey journey, string text)
    {
        foreach (var character in text)
        {
            switch (character)
            {
                case '\n': Tap(journey.Window, Key.Enter, physical: PhysicalKey.Enter); break;
                case '\t': Tap(journey.Window, Key.Tab, physical: PhysicalKey.Tab); break;
                default: journey.Window.KeyTextInput(character.ToString()); break;
            }

            // One settle pass is about 60 ms: the pace of a fast typist.
            await journey.Host.SettleAsync(1);
        }
    }

    /// <summary>Lets the editor work until the list is showing, or the time runs out.</summary>
    private static async Task WaitForListAsync(Journey journey, double seconds = 8)
    {
        var clock = Stopwatch.StartNew();

        while (!journey.Editor.CompletionOpenForTests && clock.Elapsed.TotalSeconds < seconds)
            await journey.Host.SettleAsync(1);
    }

    /// <summary>The names in the list as the user sees them, in order.</summary>
    private static IReadOnlyList<string> ListedNames(Journey journey) =>
        journey.Editor.CompletionWindowForTests?.CompletionList.CompletionData
            .Select(data => data.Text)
            .ToList() ?? [];

    /// <summary>
    /// Asserts that a line of the file reads exactly as expected once its
    /// indentation is removed, and shows the whole file when none does. A
    /// substring would also pass for "Dim x As Strng " inside a longer line,
    /// which is the very damage these journeys exist to catch.
    /// </summary>
    private static void AssertWritten(string expected, string text)
    {
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r').TrimStart());

        Assert.True(lines.Contains(expected, StringComparer.Ordinal),
            $"expected the line \"{expected}\" in the file:\n{text}");
    }

    /// <summary>The line the caret was on in the statement tests: the blank line inside Sub Main.</summary>
    private static string StatementLine(Journey journey) =>
        journey.Editor.TextForTests.Split('\n')[3].Trim();

    private const string EmptyStatement =
        "Module Program\n    Sub Main()\n        §\n    End Sub\nEnd Module\n";

    [AvaloniaFact]
    public async Task TypedDeclarationsAndCallsKeepTheirNames()
    {
        using var journey = await OpenAsync("Imports System\n" + EmptyStatement);

        await TypeAsync(journey,
            "Dim n As Integer = 42\n" +
            "Dim name As String = \"Marco\"\n" +
            "Console.WriteLine(\"Hello \" & name & n)\n" +
            "Dim total = n + 5\n");

        await journey.Host.SettleAsync(8);

        var text = journey.Editor.TextForTests;

        AssertWritten("Dim n As Integer = 42", text);
        AssertWritten("Dim name As String = \"Marco\"", text);
        AssertWritten("Console.WriteLine(\"Hello \" & name & n)", text);
        AssertWritten("Dim total = n + 5", text);
    }

    [AvaloniaFact]
    public async Task ACallToTheFileOwnSubKeepsItsArguments()
    {
        using var journey = await OpenAsync(
            "Module Program\n" +
            "    Sub Foo(a As String, b As Integer)\n" +
            "    End Sub\n" +
            "    Sub Main()\n" +
            "        §\n" +
            "    End Sub\n" +
            "End Module\n");

        await TypeAsync(journey, "Foo(\"a\", 2)\nFoo(\"b\", 12)\n");

        await journey.Host.SettleAsync(8);

        var text = journey.Editor.TextForTests;

        AssertWritten("Foo(\"a\", 2)", text);
        AssertWritten("Foo(\"b\", 12)", text);
    }

    [AvaloniaFact]
    public async Task ControlFlowKeywordsAreTypedAsWritten()
    {
        using var journey = await OpenAsync(
            "Module Program\n    Sub Main()\n        Dim x = 3\n        §\n    End Sub\nEnd Module\n");

        await TypeAsync(journey, "If x > 1 Then\nFor i = 1 To 3\nSelect Case x\n");

        await journey.Host.SettleAsync(8);

        var text = journey.Editor.TextForTests;

        AssertWritten("If x > 1 Then", text);
        AssertWritten("For i = 1 To 3", text);
        AssertWritten("Select Case x", text);
    }

    [AvaloniaFact]
    public async Task TabAfterAPartialTypeCommitsTheBestMatch()
    {
        using var journey = await OpenAsync(
            "Module Program\n    Sub Main()\n        Dim x §\n    End Sub\nEnd Module\n");

        await TypeAsync(journey, "As Str");
        await WaitForListAsync(journey);

        Assert.True(journey.Editor.CompletionOpenForTests, "no list for 'As Str'");

        await TypeAsync(journey, "\t");
        await journey.Host.SettleAsync(5);

        AssertWritten("Dim x As String", journey.Editor.TextForTests);
    }

    [AvaloniaFact]
    public async Task ImportsWithAnEnterKeepTheWholeName()
    {
        using var journey = await OpenAsync(
            "§\nModule Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        await TypeAsync(journey, "Imports System.Text\n");

        await journey.Host.SettleAsync(5);

        var firstLine = journey.Editor.TextForTests.Split('\n')[0];

        Assert.Equal("Imports System.Text", firstLine);
    }

    [AvaloniaFact]
    public async Task ControlSpaceOnAnEmptyStatementOffersKeywordsAndLocals()
    {
        using var journey = await OpenAsync(
            "Imports System\nModule Program\n    Sub Main()\n        Dim total As Integer\n        §\n    End Sub\nEnd Module\n");

        Tap(journey.Window, Key.Space, RawInputModifiers.Control, PhysicalKey.Space);

        await WaitForListAsync(journey);

        Assert.True(journey.Editor.CompletionOpenForTests, "Ctrl+Space opened no list");

        var names = ListedNames(journey);

        Assert.Contains("If", names);
        Assert.Contains("Dim", names);
        Assert.Contains("Console", names);
        Assert.Contains("total", names);
    }

    [AvaloniaFact]
    public async Task AMemberListHoldsEachNameOnce()
    {
        using var journey = await OpenAsync(
            "Imports System.Linq\n" +
            "Imports System.Collections.Generic\n" +
            "Module Program\n" +
            "    Sub Main()\n" +
            "        Dim nums As New List(Of Integer)\n" +
            "        §\n" +
            "    End Sub\n" +
            "End Module\n");

        await TypeAsync(journey, "nums.");
        await WaitForListAsync(journey);

        Assert.True(journey.Editor.CompletionOpenForTests, "the dot opened no member list");

        var names = ListedNames(journey);

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Single(names, name => name == "Aggregate");
    }

    [AvaloniaFact]
    public async Task AfterAsTheCommonTypesComeFirst()
    {
        using var journey = await OpenAsync(
            "Module Program\n    Sub Main()\n        Dim x §\n    End Sub\nEnd Module\n");

        await TypeAsync(journey, "As ");
        await WaitForListAsync(journey);

        Assert.True(journey.Editor.CompletionOpenForTests, "no list after As");

        Assert.Equal(["Integer", "String"], ListedNames(journey).Take(2));
    }

    [AvaloniaFact]
    public async Task ATypedPrefixPreselectsItsBestMatch()
    {
        using var journey = await OpenAsync(
            "Module Program\n    Sub Main()\n        Dim x §\n    End Sub\nEnd Module\n");

        await TypeAsync(journey, "As Str");
        await WaitForListAsync(journey);
        await journey.Host.SettleAsync(3);

        Assert.Equal("String", journey.Editor.SelectedCompletionForTests);
    }

    [AvaloniaFact]
    public async Task ASpaceAfterAWordSeveralNamesShareLeavesItAsTyped()
    {
        // "st" is still being written: the list holds many names beginning with
        // it, Static and String among them, and none was chosen. The space must
        // not turn the word into the first of them.
        using var journey = await OpenAsync(
            "Imports System\nModule Program\n    Sub Main()\n        Dim x = §\n    End Sub\nEnd Module\n");

        await TypeAsync(journey, "st");
        await WaitForListAsync(journey);

        Assert.True(journey.Editor.CompletionOpenForTests, "no list for 'st'");
        Assert.True(ListedNames(journey).Count(name => name.StartsWith("st", StringComparison.OrdinalIgnoreCase)) > 1,
            "the list should hold several names beginning with 'st'");

        await TypeAsync(journey, " ");
        await journey.Host.SettleAsync(5);

        // The space may open a fresh list for the next word, as it does after
        // "As" and "="; what matters is that "st" was not turned into a name.
        AssertWritten("Dim x = st ", journey.Editor.TextForTests);
    }

    [AvaloniaFact]
    public async Task EnterWithNothingTypedDoesNotTakeTheEntryTheListPickedForItself()
    {
        // Typing S and deleting it leaves the list open with nothing typed. The
        // first entry was selected by the list, not by the user: Enter must end
        // the list and start a new line, not write that entry.
        using var journey = await OpenAsync(
            "Imports System\nModule Program\n    Sub Main()\n        §\n    End Sub\nEnd Module\n");

        await TypeAsync(journey, "S");
        await WaitForListAsync(journey);
        Assert.True(journey.Editor.CompletionOpenForTests, "no list for 'S'");

        Tap(journey.Window, Key.Back, physical: PhysicalKey.Backspace);
        await journey.Host.SettleAsync(5);
        Assert.True(journey.Editor.CompletionOpenForTests, "the list closed when the word was deleted");

        await TypeAsync(journey, "\n");
        await journey.Host.SettleAsync(5);

        Assert.Equal("", StatementLine(journey));
    }

    [AvaloniaFact]
    public async Task TabWithNothingTypedDoesNotTakeTheEntryTheListPickedForItself()
    {
        using var journey = await OpenAsync(
            "Imports System\nModule Program\n    Sub Main()\n        §\n    End Sub\nEnd Module\n");

        await TypeAsync(journey, "S");
        await WaitForListAsync(journey);
        Assert.True(journey.Editor.CompletionOpenForTests, "no list for 'S'");

        Tap(journey.Window, Key.Back, physical: PhysicalKey.Backspace);
        await journey.Host.SettleAsync(5);
        Assert.True(journey.Editor.CompletionOpenForTests, "the list closed when the word was deleted");

        await TypeAsync(journey, "\t");
        await journey.Host.SettleAsync(5);

        Assert.Equal("", StatementLine(journey));
    }

    [AvaloniaFact]
    public async Task EnterAfterMovingTheHighlightTakesTheEntryMovedTo()
    {
        using var journey = await OpenAsync(
            "Imports System\nModule Program\n    Sub Main()\n        §\n    End Sub\nEnd Module\n");

        Tap(journey.Window, Key.Space, RawInputModifiers.Control, PhysicalKey.Space);
        await WaitForListAsync(journey);
        Assert.True(journey.Editor.CompletionOpenForTests, "Ctrl+Space opened no list");

        var names = ListedNames(journey);

        Tap(journey.Window, Key.Down, physical: PhysicalKey.ArrowDown);
        await journey.Host.SettleAsync(2);

        var moved = journey.Editor.SelectedCompletionForTests;
        Assert.Equal(names[1], moved);

        await TypeAsync(journey, "\n");
        await journey.Host.SettleAsync(5);

        Assert.Equal(moved, StatementLine(journey));
    }

    [AvaloniaFact]
    public async Task ARefreshKeepsTheEntryTheUserMovedTo()
    {
        // Among the locals, "alps" is the best match for "al" and "alpha" is
        // second. Moving to "alpha" and typing "p" narrows the list to both: the
        // choice the user made must stay, not revert to "alps".
        using var journey = await OpenAsync(
            "Module Program\n" +
            "    Sub Main()\n" +
            "        Dim alpha As Integer\n" +
            "        Dim alps As Integer\n" +
            "        §\n" +
            "    End Sub\n" +
            "End Module\n");

        await TypeAsync(journey, "al");
        await WaitForListAsync(journey);
        Assert.True(journey.Editor.CompletionOpenForTests, "no list for 'al'");
        Assert.Equal("alps", journey.Editor.SelectedCompletionForTests);

        Tap(journey.Window, Key.Down, physical: PhysicalKey.ArrowDown);
        await journey.Host.SettleAsync(2);
        Assert.Equal("alpha", journey.Editor.SelectedCompletionForTests);

        await TypeAsync(journey, "p");
        await journey.Host.SettleAsync(3);

        Assert.Equal("alpha", journey.Editor.SelectedCompletionForTests);
    }

    [AvaloniaFact]
    public async Task AWhileShortcutExpandsToTheLoopOnItsSecondTab()
    {
        using var journey = await OpenAsync(
            "Module Program\n    Sub Main()\n        §\n    End Sub\nEnd Module\n");

        await TypeAsync(journey, "while\t\t");
        await journey.Host.SettleAsync(8);

        var lines = journey.Editor.TextForTests.Split('\n').Select(line => line.Trim()).ToArray();

        Assert.Contains(lines, line => line.StartsWith("While ", StringComparison.Ordinal));
        AssertWritten("End While", journey.Editor.TextForTests);
    }

    [AvaloniaFact]
    public async Task AnApostropheAboveADeclarationWritesItsDocumentationComment()
    {
        // Three apostrophes on the line above a Sub write its XML documentation
        // block. The declaration beneath it must come through untouched.
        using var journey = await OpenAsync(
            "Module Program\n    §\n    Sub Foo(a As String)\n    End Sub\nEnd Module\n");

        await TypeAsync(journey, "'''");
        await journey.Host.SettleAsync(8);

        var text = journey.Editor.TextForTests;

        AssertWritten("''' <summary>", text);
        AssertWritten("''' </summary>", text);
        AssertWritten("Sub Foo(a As String)", text);
        AssertWritten("End Sub", text);
    }

    [AvaloniaFact]
    public async Task ABracketAfterADotDoesNotTakeTheHighlightedMember()
    {
        // Nothing typed after the dot: the list only suggests, and "(" must not
        // write whichever member happens to be highlighted.
        using var journey = await OpenAsync(
            "Imports System\nModule Program\n    Sub Main()\n        Console§\n    End Sub\nEnd Module\n");

        await TypeAsync(journey, ".");
        await WaitForListAsync(journey);

        Assert.True(journey.Editor.CompletionOpenForTests, "the dot opened no member list");

        await TypeAsync(journey, "(");
        await journey.Host.SettleAsync(5);

        // The bracket pairs as it is typed: the caret sits between the pair, and
        // no member name was written into it.
        AssertWritten("Console.()", journey.Editor.TextForTests);
    }

    [AvaloniaFact]
    public async Task ACssPropertyWithDashesIsWrittenWhole()
    {
        // The word is "background-c", not "c": the entry replaces all of it.
        using var journey = await OpenFileAsync("Site.css", "body {\n    §\n}\n");

        await TypeAsync(journey, "background-c");
        await WaitForListAsync(journey);

        Assert.True(journey.Editor.CompletionOpenForTests, "no list for 'background-c'");

        await TypeAsync(journey, "\n");
        await journey.Host.SettleAsync(5);

        AssertWritten("background-color: ", journey.Editor.TextForTests);
    }

    [AvaloniaFact]
    public async Task AnAspAttributeInAViewIsWrittenWhole()
    {
        using var journey = await OpenFileAsync(
            Path.Combine("Views", "Home", "Index.vbhtml"),
            "@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers\n<a §>Home</a>\n",
            web: true);

        // The tag helpers come from the project's compilation, which a web project
        // takes longer to give than a console one.
        await TypeAsync(journey, "asp-act");
        await WaitForListAsync(journey, seconds: 30);

        Assert.True(journey.Editor.CompletionOpenForTests, "no list for 'asp-act'");

        await TypeAsync(journey, "\n");
        await journey.Host.SettleAsync(5);

        var text = journey.Editor.TextForTests;

        Assert.True(text.Contains("<a asp-action>Home</a>", StringComparison.Ordinal), $"the tag as written:\n{text}");
    }

    [AvaloniaFact(Skip = "VbHtmlCodeRegions.IsInCode reads '@oncl' inside a tag as code, so no markup list opens; the parser (Basalt.Razor.Vb, owner-only) has to treat it as a directive attribute first.")]
    public async Task ADirectiveAttributeWithItsAtIsWrittenWhole()
    {
        using var journey = await OpenFileAsync(
            "Counter.vbrazor",
            "<button §>+</button>\n",
            web: true);

        await TypeAsync(journey, "@oncl");
        await WaitForListAsync(journey, seconds: 30);

        Assert.True(journey.Editor.CompletionOpenForTests, "no list for '@oncl'");

        await TypeAsync(journey, "\n");
        await journey.Host.SettleAsync(5);

        var text = journey.Editor.TextForTests;

        Assert.True(text.Contains("<button @onclick>+</button>", StringComparison.Ordinal), $"the tag as written:\n{text}");
    }

    [AvaloniaFact]
    public async Task ASpaceAfterANameBeingDeclaredLeavesTheNameAsWritten()
    {
        // "it" is the loop variable being declared after For, and "items" is in
        // scope: Roslyn lists items in suggestion mode (measured: Each, i, items
        // for "For i"; "For Each item" offers nothing). The space must not take
        // "items"; the list only suggests the name being declared.
        using var journey = await OpenAsync(
            "Imports System.Collections.Generic\n" +
            "Module Program\n" +
            "    Sub Main()\n" +
            "        Dim items As New List(Of Integer)\n" +
            "        §\n" +
            "    End Sub\n" +
            "End Module\n");

        await TypeAsync(journey, "For it");
        await WaitForListAsync(journey);

        // The space must meet an open list, or the test proves nothing.
        Assert.True(journey.Editor.CompletionOpenForTests, "no list for 'it' after For");

        await TypeAsync(journey, " ");
        await journey.Host.SettleAsync(5);

        Assert.Contains("For it ", journey.Editor.TextForTests.Split('\n').Select(line => line.TrimStart()));
    }

    [AvaloniaFact]
    public async Task AParenthesisTakesTheHighlightedEntryThatBeginsWithWhatWasTyped()
    {
        using var journey = await OpenAsync(
            "Imports System\nModule Program\n    Sub Main()\n        §\n    End Sub\nEnd Module\n");

        await TypeAsync(journey, "Console.Wr");
        await WaitForListAsync(journey);

        Assert.True(journey.Editor.CompletionOpenForTests, "no list for 'Console.Wr'");

        await TypeAsync(journey, "(");
        await journey.Host.SettleAsync(5);

        AssertWritten("Console.Write()", journey.Editor.TextForTests);
    }

    [AvaloniaFact]
    public async Task AParenthesisAfterANewMethodsNameKeepsTheName()
    {
        // The name after "Sub" is being declared: "N(" must not become "New(", which would make a constructor.
        using var journey = await OpenAsync(
            "Class Shape\n    §\nEnd Class\n");

        await TypeAsync(journey, "Sub N");
        await journey.Host.SettleAsync(20);
        await TypeAsync(journey, "(");
        await journey.Host.SettleAsync(5);

        Assert.Contains("Sub N(", journey.Editor.TextForTests, StringComparison.Ordinal);
        Assert.DoesNotContain("Sub New", journey.Editor.TextForTests, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ACommaTakesTheHighlightedEntryThatBeginsWithWhatWasTyped()
    {
        using var journey = await OpenAsync(
            "Module Program\n" +
            "    Sub Foo(a As Integer, b As Integer)\n" +
            "    End Sub\n" +
            "    Sub Main()\n" +
            "        Dim myValue As Integer = 1\n" +
            "        §\n" +
            "    End Sub\n" +
            "End Module\n");

        await TypeAsync(journey, "Foo(myVa");
        await WaitForListAsync(journey);

        Assert.True(journey.Editor.CompletionOpenForTests, "no list for 'myVa'");

        await TypeAsync(journey, ",");
        await journey.Host.SettleAsync(5);

        var text = journey.Editor.TextForTests;

        Assert.True(text.Contains("Foo(myValue,", StringComparison.Ordinal), $"the call as written:\n{text}");
    }

    [AvaloniaFact]
    public async Task ADeletionNarrowsTheListBeforeTheNextKey()
    {
        // The list is narrowed by the deletion itself, not by a later turn of the
        // dispatcher: the names of "Str" are in the list at once after Backspace.
        using var journey = await OpenAsync(
            "Imports System\nModule Program\n    Sub Main()\n        §\n    End Sub\nEnd Module\n");

        await TypeAsync(journey, "Stri");
        await WaitForListAsync(journey);

        Assert.True(journey.Editor.CompletionOpenForTests, "no list for 'Stri'");

        var narrower = ListedNames(journey).Count;

        Tap(journey.Window, Key.Back, physical: PhysicalKey.Backspace);

        Assert.True(ListedNames(journey).Count > narrower,
            $"the list still held {narrower} names after Backspace");
    }
}

/// <summary>
/// The rules that decide which entries a list keeps, which name stands for
/// the one-row-per-name cut, and which key may take the highlighted entry.
/// The journeys above show the rules at work in a window; these pin each one.
/// </summary>
public sealed class CompletionRuleTests
{
    private static CompletionItem Item(string name, CompletionKind kind = CompletionKind.Method, bool preselected = false) =>
        new(name, name, kind) { IsPreselected = preselected };

    [Fact]
    public void AWordWithDashesIsFoundByItsParts()
    {
        // The markup and style providers share this matcher: "bc" finds
        // background-color because the letter after the dash starts a part.
        var names = CompletionMatcher
            .Filter([Item("background-color"), Item("border-top")], "bc")
            .Select(match => match.Item.DisplayText);

        Assert.Equal(["background-color"], names);
    }

    [Fact]
    public void AKeywordAndATypeOfTheSameNameKeepTheKeywordWhicheverComesFirst()
    {
        var keyword = Item("String", CompletionKind.Keyword);
        var type = Item("String", CompletionKind.Class);

        Assert.Equal(CompletionKind.Keyword, CompletionMatcher.Filter([type, keyword], "Str").Single().Item.Kind);
        Assert.Equal(CompletionKind.Keyword, CompletionMatcher.Filter([keyword, type], "Str").Single().Item.Kind);
        Assert.Equal(CompletionKind.Keyword, CompletionMatcher.Filter([type, keyword], "").Single().Item.Kind);
        Assert.Equal(CompletionKind.Keyword, CompletionMatcher.Filter([keyword, type], "").Single().Item.Kind);
    }

    [Fact]
    public void ASpaceDoesNotTakeTheFirstOfSeveralNamesThatShareWhatWasTyped()
    {
        Assert.False(CompletionMatcher.MayCommitOnPunctuation(Item("Static"), "st", entriesMatching: 3, chosen: false));
    }

    [Fact]
    public void ASpaceTakesTheOnlyNameWhatWasTypedCanMean()
    {
        Assert.True(CompletionMatcher.MayCommitOnPunctuation(Item("Static"), "st", entriesMatching: 1, chosen: false));
    }

    [Fact]
    public void ASpaceTakesTheNameWhenItIsWrittenInFull()
    {
        Assert.True(CompletionMatcher.MayCommitOnPunctuation(
            Item("String", CompletionKind.Keyword), "string", entriesMatching: 3, chosen: false));
    }

    [Fact]
    public void ASpaceTakesAnEntryTheUserChose()
    {
        Assert.True(CompletionMatcher.MayCommitOnPunctuation(Item("Static"), "st", entriesMatching: 3, chosen: true));
    }

    [Fact]
    public void ASpaceNeverTakesAnEntryThatDoesNotMatchWhatWasTyped()
    {
        Assert.False(CompletionMatcher.MayCommitOnPunctuation(Item("Static"), "zz", entriesMatching: 1, chosen: true));
    }

    [Fact]
    public void WithNothingTypedAPunctuationTakesOnlyAPreselectedOrChosenEntry()
    {
        Assert.False(CompletionMatcher.MayCommitOnPunctuation(Item("Static"), "", entriesMatching: 3, chosen: false));
        Assert.True(CompletionMatcher.MayCommitOnPunctuation(Item("Static", preselected: true), "", entriesMatching: 3, chosen: false));
        Assert.True(CompletionMatcher.MayCommitOnPunctuation(Item("Static"), "", entriesMatching: 3, chosen: true));
    }

    [Fact]
    public void WithNothingTypedEnterAndTabTakeOnlyAPreselectedOrChosenEntry()
    {
        Assert.False(CompletionMatcher.MayCommit(Item("AddHandler"), "", chosen: false));
        Assert.True(CompletionMatcher.MayCommit(Item("AddHandler", preselected: true), "", chosen: false));
        Assert.True(CompletionMatcher.MayCommit(Item("AddHandler"), "", chosen: true));
    }

    [Fact]
    public void EnterAndTabWithSomethingTypedTakeAnEntryThatMatchesIt()
    {
        Assert.True(CompletionMatcher.MayCommit(Item("String"), "Str", chosen: false));
        Assert.False(CompletionMatcher.MayCommit(Item("String"), "Strq", chosen: false));
    }

    [Fact]
    public void TheSameNameFromTwoNamespacesIsTwoRows()
    {
        // Timer of System.Threading and Timer of System.Timers are two entries the
        // user must be able to tell apart.
        var rows = CompletionMatcher.Filter(
            [Named("Timer", "System.Threading"), Named("Timer", "System.Timers")], "Tim");

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void OverloadsOfOneMethodAreOneRow()
    {
        var rows = CompletionMatcher.Filter(
            [Named("WriteLine", "System.Console"), Named("WriteLine", "System.Console")], "Write");

        Assert.Single(rows);
    }

    [Fact]
    public void AKeywordIsOneRowWithTheTypeOfItsNameFromAnyNamespace()
    {
        var rows = CompletionMatcher.Filter(
            [Item("String", CompletionKind.Keyword), Named("String", "System", CompletionKind.Class)], "Str");

        Assert.Single(rows);
        Assert.Equal(CompletionKind.Keyword, rows[0].Item.Kind);
    }

    [Fact]
    public void AParenthesisOrDotTakesAHighlightedPrefixMatchWhereOthersMatchToo()
    {
        var entries = CompletionMatcher.Filter([Item("Write"), Item("WriteLine")], "Wr").Count;

        Assert.True(CompletionMatcher.MayCommitOnPunctuation(Item("Write"), "Wr", entries, chosen: false, character: '('));
        Assert.True(CompletionMatcher.MayCommitOnPunctuation(Item("Write"), "Wr", entries, chosen: false, character: '.'));
        Assert.False(CompletionMatcher.MayCommitOnPunctuation(Item("Write"), "Wr", entries, chosen: false, character: ' '));
    }

    [Fact]
    public void ACommaTakesAHighlightedPrefixMatchWhereOthersMatchToo()
    {
        Assert.True(CompletionMatcher.MayCommitOnPunctuation(
            Item("myValue", CompletionKind.Local), "myVa", entriesMatching: 3, chosen: false, character: ','));
    }

    [Fact]
    public void InSuggestionModeOnlyTheNameWrittenInFullIsTaken()
    {
        var suggested = new CompletionItem("items", "items", CompletionKind.Local) { SoftSelection = true };

        Assert.False(CompletionMatcher.MayCommitOnPunctuation(suggested, "item", entriesMatching: 1, chosen: false, character: ' '));
        Assert.False(CompletionMatcher.MayCommitOnPunctuation(suggested, "item", entriesMatching: 1, chosen: false, character: '('));
        Assert.True(CompletionMatcher.MayCommitOnPunctuation(suggested, "items", entriesMatching: 2, chosen: false, character: ' '));
    }

    [Fact]
    public void ADirectiveAttributeIsFoundWithOrWithoutItsAt()
    {
        var onclick = new CompletionItem("@onclick", "@onclick", CompletionKind.Keyword) { FilterText = "onclick" };

        Assert.Single(CompletionMatcher.Filter([onclick], "@oncl"));
        Assert.Single(CompletionMatcher.Filter([onclick], "oncl"));
        Assert.Empty(CompletionMatcher.Filter([onclick], "@bind"));
    }

    private static CompletionItem Named(string name, string container, CompletionKind kind = CompletionKind.Class) =>
        new(name, name, kind) { Container = container };
}
