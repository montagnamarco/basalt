using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The VB.NET coding experience end to end, through a real
/// <see cref="MainWindow"/>: typing, IntelliSense, parameter info, smart
/// tags, pretty listing, comment toggling and squiggles, the way a Visual
/// Basic developer at the keyboard would drive them.
///
/// Each test is one journey. Where the journey turned out to be broken, the
/// fix lives next to the code it changed and this file carries the
/// regression test that fails without it.
/// </summary>
public sealed class UserJourneyVbCodingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-journey-vbcoding-").FullName;

    private string ProgramPath => Path.Combine(_root, "Program.vb");

    private string WriteProject(string source)
    {
        var project = Path.Combine(_root, "Journey.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <MyType>Empty</MyType>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(ProgramPath, source);
        return project;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static TextEditor TextEditorFor(MainWindow window, string filePath)
    {
        var code = window.CodeEditorForTests(filePath);
        Assert.NotNull(code);
        return code!.GetVisualDescendants().OfType<TextEditor>().Single();
    }

    private static void PressKey(MainWindow window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }

    private static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, int maxPasses = 150)
    {
        for (var i = 0; i < maxPasses; i++)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }
        return condition();
    }

    // ---- journey: comment / uncomment (Ctrl+/) --------------------------

    /// <summary>
    /// Selecting two whole lines by Shift+Down from the start of the first
    /// one lands the selection at column zero of the third line: nothing on
    /// that third line was actually selected. Ctrl+/ must comment only the
    /// two lines the user selected.
    /// </summary>
    /// <remarks>
    /// Before the fix, <c>MainWindow.ToggleComment</c> took the line the
    /// selection's end offset falls on as the last line to comment.
    /// AvaloniaEdit's <c>GetLineByOffset</c> resolves an offset sitting
    /// exactly at a line's start to that line, not the one before it, so a
    /// selection ending at the start of line three also commented line three.
    /// </remarks>
    [AvaloniaFact]
    public async Task CommentTogglingTwoSelectedLinesDoesNotTouchTheLineTheSelectionEndsAt()
    {
        const string source =
            "Module Program\n    Sub Main()\n        Dim a = 1\n        Dim b = 2\n        Dim c = 3\n    End Sub\nEnd Module\n";
        var project = WriteProject(source);
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, ProgramPath);
        // Home, not the first letter of the statement: the bug is about a
        // selection that starts and ends at column zero, the way Shift+Down
        // from the very start of a line leaves it.
        editor.CaretOffset = editor.Document.GetLineByNumber(3).Offset;
        editor.TextArea.Focus();

        PressKey(w, Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown);
        PressKey(w, Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown);

        Assert.Equal(editor.Document.GetLineByNumber(5).Offset, editor.CaretOffset);

        PressKey(w, Key.Oem2, RawInputModifiers.Control, PhysicalKey.None);

        const string expected =
            "Module Program\n    Sub Main()\n        'Dim a = 1\n        'Dim b = 2\n        Dim c = 3\n    End Sub\nEnd Module\n";
        Assert.True(await WaitUntilAsync(host, () => editor.Text == expected),
            $"Ctrl+/ produced:\n{editor.Text}");
    }

    /// <summary>Ctrl+/ on an already-commented line brings the code back.</summary>
    [AvaloniaFact]
    public async Task CommentTogglingTheSameLineTwiceRestoresTheOriginalLine()
    {
        const string source = "Module Program\n    Sub Main()\n        Dim a = 1\n    End Sub\nEnd Module\n";
        var project = WriteProject(source);
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, ProgramPath);
        editor.CaretOffset = source.IndexOf("Dim a", StringComparison.Ordinal);
        editor.TextArea.Focus();

        PressKey(w, Key.Oem2, RawInputModifiers.Control, PhysicalKey.None);

        const string commented = "Module Program\n    Sub Main()\n        'Dim a = 1\n    End Sub\nEnd Module\n";
        Assert.True(await WaitUntilAsync(host, () => editor.Text == commented), editor.Text);

        PressKey(w, Key.Oem2, RawInputModifiers.Control, PhysicalKey.None);

        Assert.True(await WaitUntilAsync(host, () => editor.Text == source), editor.Text);
    }

    // ---- journey: pretty listing, end constructs ------------------------

    /// <summary>
    /// Pressing Enter after "Try" writes the Catch clause as well as End
    /// Try, the way Visual Studio's VB editor has always done: a bare Try
    /// with nothing to catch is not what pretty listing is supposed to leave
    /// behind.
    /// </summary>
    /// <remarks>
    /// Before the fix, <c>VisualBasicEnterInput</c> appended whatever
    /// <c>VisualBasicBlockCompleter</c> returned as a single closing line,
    /// which for a Try block is only "End Try".
    /// </remarks>
    [AvaloniaFact]
    public async Task EnterAfterTryWritesTheCatchClauseAndEndTry()
    {
        const string source = "Module Program\n    Sub Main()\n        Try\n    End Sub\nEnd Module\n";
        var project = WriteProject(source);
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var code = w.CodeEditorForTests(ProgramPath);
        Assert.NotNull(code);
        var editor = TextEditorFor(w, ProgramPath);
        editor.CaretOffset = source.IndexOf("Try", StringComparison.Ordinal) + "Try".Length;
        editor.TextArea.Focus();

        await code!.HandleEnterForTestsAsync();

        const string expected =
            "Module Program\n    Sub Main()\n        Try\n            \n        Catch ex As Exception\n            \n        End Try\n    End Sub\nEnd Module\n";
        Assert.True(await WaitUntilAsync(host, () => editor.Text == expected),
            $"Enter after Try produced:\n{editor.Text}");

        // The caret stays in the body right under Try, the same place a
        // plain block (If, While) leaves it: an empty line, indented one
        // level deeper than Try itself.
        var caretLine = editor.Document.GetLineByOffset(editor.CaretOffset);
        Assert.True(string.IsNullOrWhiteSpace(editor.Document.GetText(caretLine.Offset, caretLine.Length)),
            $"caret should sit on a blank line, found '{editor.Document.GetText(caretLine.Offset, caretLine.Length)}'");
        Assert.Equal(4, caretLine.LineNumber);
        Assert.Equal(caretLine.EndOffset, editor.CaretOffset);
    }

    /// <summary>
    /// Enter after "Select Case x" lines End Select up with Select Case and leaves the caret one
    /// level in, as Visual Studio does. The indentation probe is a declaration, which VB does not
    /// allow directly under Select Case before a Case, so End Select used to land at column 0.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("")]
    [InlineData(" ' which value")]
    public async Task EnterAfterSelectCaseLinesEndSelectUpWithIt(string comment)
    {
        var source = "Module Program\n    Sub Main()\n        Dim x = 1\n        Select Case x" + comment + "\n    End Sub\nEnd Module\n";
        var project = WriteProject(source);
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var code = w.CodeEditorForTests(ProgramPath);
        Assert.NotNull(code);
        var editor = TextEditorFor(w, ProgramPath);
        editor.CaretOffset = source.IndexOf("Select Case x", StringComparison.Ordinal) + ("Select Case x" + comment).Length;
        editor.TextArea.Focus();

        await code!.HandleEnterForTestsAsync();

        var expected =
            "Module Program\n    Sub Main()\n        Dim x = 1\n        Select Case x" + comment + "\n            \n        End Select\n    End Sub\nEnd Module\n";
        Assert.True(await WaitUntilAsync(host, () => editor.Text == expected),
            $"Enter after Select Case produced:\n{editor.Text}");
    }

    /// <summary>Enter on a Try line that already has its Catch below adds only End Try.</summary>
    [AvaloniaFact]
    public async Task EnterAfterATryWithACatchAlreadyBelowDoesNotWriteASecondOne()
    {
        const string source = "Module Program\n    Sub Main()\n        Try\n        Catch ex As Exception\n    End Sub\nEnd Module\n";
        var project = WriteProject(source);
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var code = w.CodeEditorForTests(ProgramPath);
        Assert.NotNull(code);
        var editor = TextEditorFor(w, ProgramPath);
        editor.CaretOffset = source.IndexOf("Try", StringComparison.Ordinal) + "Try".Length;
        editor.TextArea.Focus();

        await code!.HandleEnterForTestsAsync();
        Assert.True(await WaitUntilAsync(host, () => editor.Text.Contains("End Try", StringComparison.Ordinal)),
            $"Enter after Try produced:\n{editor.Text}");

        var catches = editor.Text.Split("Catch ex As Exception").Length - 1;
        Assert.True(catches == 1, $"expected one Catch, got {catches}:\n{editor.Text}");
    }

    // ---- journey: parameter info -----------------------------------------

    /// <summary>
    /// Signature help bolds the parameter being written, and moves to the
    /// next one as each comma is typed, the way Visual Studio's parameter
    /// info does for an overloaded call.
    /// </summary>
    [AvaloniaFact]
    public async Task SignatureHelpHighlightsTheParameterBeingTyped()
    {
        const string source = "Module Program\n    Sub Greet(name As String, times As Integer)\n    End Sub\n" +
            "    Sub Greet(name As String)\n    End Sub\n    Sub Main()\n        Greet(\n    End Sub\nEnd Module\n";
        var project = WriteProject(source);
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var code = w.CodeEditorForTests(ProgramPath);
        Assert.NotNull(code);
        var editor = TextEditorFor(w, ProgramPath);
        editor.CaretOffset = source.IndexOf("Greet(", source.IndexOf("Sub Main", StringComparison.Ordinal),
            StringComparison.Ordinal) + "Greet(".Length;
        editor.TextArea.Focus();

        await code!.ShowSignatureHelpForTestsAsync();

        Assert.True(await WaitUntilAsync(host, () => code.SignatureHelpCountForTests > 0),
            "typing the opening parenthesis did not show parameter info.");
        Assert.Equal(0, code.SignatureActiveParameterForTests);

        w.KeyTextInput("\"a\"");
        w.KeyTextInput(",");

        Assert.True(await WaitUntilAsync(host, () => code.SignatureActiveParameterForTests == 1),
            $"active parameter after the comma was {code.SignatureActiveParameterForTests}.");
    }

    // ---- journey: squiggles appear while typing and clear when fixed ----

    /// <summary>
    /// Typing a name that does not exist underlines it once the compiler has
    /// had a chance to look, the same as it would under Visual Studio's
    /// cursor; fixing the typo makes the underline go away.
    /// </summary>
    [AvaloniaFact]
    public async Task ASquiggleAppearsWhileTypingAnErrorAndClearsWhenItIsFixed()
    {
        const string source = "Module Program\n    Sub Main()\n        Dim n As Integer = 1\n    End Sub\nEnd Module\n";
        var project = WriteProject(source);
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, ProgramPath);
        editor.CaretOffset = source.IndexOf("1\n", StringComparison.Ordinal);
        editor.TextArea.Focus();

        // Breaks the statement: "1notAName" is not valid VB.
        w.KeyTextInput("notAName");

        Assert.True(await WaitUntilAsync(host,
                () => vm.Diagnostics.Any(d => d.FilePath == ProgramPath && d.Severity == Basalt.Core.Model.DiagnosticSeverity.Error)),
            "the typo was never reported.");

        for (var i = 0; i < "notAName".Length; i++)
            PressKey(w, Key.Back, RawInputModifiers.None, PhysicalKey.None);

        Assert.True(await WaitUntilAsync(host,
                () => vm.Diagnostics.All(d => d.FilePath != ProgramPath || d.Severity != Basalt.Core.Model.DiagnosticSeverity.Error)),
            "the diagnostic did not clear once the typo was removed.");
    }

    // ---- journey: member completion after a dot -------------------------

    /// <summary>
    /// The dot after a local offers that local's own members: a String's,
    /// a generic collection's, or an enum type's named values.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("s", "ToU", "ToUpper")]
    [InlineData("numbers", "Ad", "Add")]
    [InlineData("DayOfWeek", "Mon", "Monday")]
    public async Task TypingADotAfterALocalOffersItsOwnMembers(string expression, string filter, string expected)
    {
        const string declarations =
            "        Dim s As String = \"hi\"\n" +
            "        Dim numbers As New System.Collections.Generic.List(Of Integer)\n";
        var source = "Module Program\n    Sub Main()\n" + declarations +
            "        " + expression + "\n    End Sub\nEnd Module\n";
        var project = WriteProject(source);
        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(5);

        var code = w.CodeEditorForTests(ProgramPath);
        Assert.NotNull(code);
        var editor = TextEditorFor(w, ProgramPath);
        editor.CaretOffset = source.LastIndexOf(expression, StringComparison.Ordinal) + expression.Length;
        editor.TextArea.Focus();

        w.KeyTextInput(".");
        Assert.True(await WaitUntilAsync(host, () => code!.CompletionOpenForTests),
            $"typing \".\" after {expression} did not open the member list.");

        w.KeyTextInput(filter);
        Assert.True(await WaitUntilAsync(host, () => code!.SelectedCompletionForTests == expected),
            $"expected '{expected}' selected after typing '{filter}', got '{code!.SelectedCompletionForTests}'.");
    }
}
