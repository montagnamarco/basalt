using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.ObjectBrowser;

namespace Basalt.Tests;

/// <summary>
/// The other languages and the tool panels around them, driven the way a
/// person at the keyboard would: a real <see cref="MainWindow"/>, real key
/// presses, real projects on disk.
///
/// Each test is one journey. A journey that turned out to be broken either
/// has its fix next to the code it changed, with the regression test here
/// failing without it, or sits out of this file because the defect is in a
/// critical area (source mappings, #ExternalSource, the Razor parser, the
/// language server) and must be reported rather than patched here.
/// </summary>
public sealed class UserJourneyLanguagesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-journey-languages-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string L(string key) => Localizer.Get(key);

    private static void PressKey(MainWindow window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }

    private static void InvokeMenu(MainWindow window, params string[] path)
    {
        IReadOnlyList<MenuEntry> level = window.MenuEntriesForTests();
        MenuEntry? hit = null;

        foreach (var header in path)
        {
            hit = level.FirstOrDefault(e => e.Header == header)
                ?? throw new InvalidOperationException("menu entry not found: " + string.Join(" > ", path));
            level = hit.Children ?? [];
        }

        if (hit!.Action is { } action) action();
        else hit.Command!.Execute(null);
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

    private static TextEditor TextEditorFor(MainWindow window, string filePath)
    {
        var code = window.CodeEditorForTests(filePath);
        Assert.NotNull(code);
        return code!.GetVisualDescendants().OfType<TextEditor>().Single();
    }

    private static async Task<Window?> WaitForDialogAsync(TestWindow host, int maxPasses = 100)
    {
        Window? dialog = null;
        for (var i = 0; i < maxPasses && dialog is null; i++)
        {
            await host.SettleAsync(1);
            dialog = host.Window.OwnedWindows.FirstOrDefault();
        }
        return dialog;
    }

    private static void ClickDefaultButton(Window dialog)
    {
        var button = dialog.GetVisualDescendants().OfType<Button>().First(b => b.IsDefault);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static string WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private string WriteVbConsoleProject(string source, string programName = "Program.vb")
    {
        var project = Path.Combine(_root, "Journey.vbproj");
        WriteFile(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <MyType>Empty</MyType>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        WriteFile(Path.Combine(_root, programName), source);
        return project;
    }

    // ---- journey 4: Object Browser ---------------------------------------

    /// <summary>
    /// View > Object Browser, search for a type of the user's own project,
    /// select it, see its members, and double-click a member: the editor
    /// opens the file that declares it at the declaration's line.
    /// </summary>
    [AvaloniaFact]
    public async Task SearchingAProjectTypeListsItsMembersAndDoubleClickingOneOpensItsDeclaration()
    {
        const string helper =
            "Public Class Greeter\n    Public Sub Hello()\n    End Sub\nEnd Class\n";
        const string program =
            "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n";
        var project = WriteVbConsoleProject(program);
        var helperPath = WriteFile(Path.Combine(_root, "Greeter.vb"), helper);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolObjectBrowser));
        await host.SettleAsync(10);

        var browser = w.GetVisualDescendants().OfType<ObjectBrowserPanel>().Single();
        Assert.True(await WaitUntilAsync(host, () => browser.Libraries.Count > 0, maxPasses: 400),
            "the Object Browser never read the libraries of the solution.");

        browser.SearchBox.Text = "Greeter";
        Assert.True(await WaitUntilAsync(host, () => browser.TypeRows.OfType<BrowserTypeRow>()
            .Any(row => row.Symbol.Name == "Greeter"), maxPasses: 400),
            "searching for Greeter did not list it.");

        var typeRow = browser.TypeRows.OfType<BrowserTypeRow>().First(row => row.Symbol.Name == "Greeter");
        browser.TypeList.SelectedItem = typeRow;

        Assert.True(await WaitUntilAsync(host, () => browser.MemberRows.Any(m => m.Symbol.Name == "Hello"), maxPasses: 400),
            "selecting Greeter did not list its member Hello.");

        var memberRow = browser.MemberRows.First(m => m.Symbol.Name == "Hello");
        browser.MemberList.SelectedItem = memberRow;
        browser.MemberList.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));

        Assert.True(await WaitUntilAsync(host, () => vm.ActiveDocument?.FilePath == helperPath, maxPasses: 400),
            "double-clicking Hello did not open the file that declares it.");

        // The caret lands on the declaration (line 2 of Greeter.vb).
        // OpenAtAsync posted a single GoToLine, which ran before the editor existed and
        // left the caret on line 1; it now waits with GoToLineWhenReady, as F12 does.
        // With the single post restored this assertion fails.
        var editor = TextEditorFor(w, helperPath);
        Assert.NotNull(editor);
        Assert.True(await WaitUntilAsync(host,
                () => editor!.Document.GetLineByOffset(editor.CaretOffset).LineNumber == 2, maxPasses: 400),
            $"the caret is on line {editor!.Document.GetLineByOffset(editor.CaretOffset).LineNumber}, not on Hello's declaration.");
    }

    // ---- journey 5: Immediate window -------------------------------------

    private static ImmediatePanel ImmediateOf(MainWindow window) =>
        window.GetVisualDescendants().OfType<ImmediatePanel>().Single();

    private static Button ImmediateButtonTipped(ImmediatePanel panel, string tip) =>
        panel.GetVisualDescendants().OfType<Button>().Single(b => ToolTip.GetTip(b) as string == tip);

    /// <summary>
    /// The Immediate window answers "? 1 + 2" with 3 and a string function
    /// with its result, the way a VB6 developer types them.
    /// </summary>
    [AvaloniaFact]
    public async Task ImmediateWindowEvaluatesPrintedExpressions()
    {
        var project = WriteVbConsoleProject("Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;

        InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolImmediate));
        await host.SettleAsync(10);

        var panel = ImmediateOf(w);
        panel.Input.Text = "? 1 + 2";
        await panel.SubmitAsync();

        Assert.Contains("3", panel.TranscriptLines);

        panel.Input.Text = "? UCase(\"basalt\")";
        await panel.SubmitAsync();

        Assert.Contains("BASALT", panel.TranscriptLines);
    }

    /// <summary>Up walks back through the lines typed before, Clear empties the transcript and keeps them.</summary>
    [AvaloniaFact]
    public async Task ImmediateWindowRecallsTypedLinesWithUpAndClearEmptiesTheTranscript()
    {
        var project = WriteVbConsoleProject("Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;

        InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolImmediate));
        await host.SettleAsync(10);

        var panel = ImmediateOf(w);
        panel.Input.Text = "? 1 + 2";
        await panel.SubmitAsync();
        panel.Input.Text = "? 7 * 6";
        await panel.SubmitAsync();

        panel.Input.Focus();
        PressKey(w, Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp);
        Assert.Equal("? 7 * 6", panel.Input.Text);
        PressKey(w, Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp);
        Assert.Equal("? 1 + 2", panel.Input.Text);

        Assert.NotEmpty(panel.TranscriptLines);
        ImmediateButtonTipped(panel, L(StringKeys.ImmediateClear)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Empty(panel.TranscriptLines);
        panel.Input.Text = "? 7 * 6";
        await panel.SubmitAsync();
        Assert.Contains("42", panel.TranscriptLines);
    }

    // ---- journey 3: QuickBASIC -------------------------------------------

    /// <summary>
    /// A .bas file opens in an editor that colours it: the QuickBASIC
    /// language provider declares the Visual Basic definition, and the Shell
    /// is the one that has to apply it, because no TextMate grammar covers
    /// .bas.
    /// </summary>
    /// <remarks>
    /// Before the fix nothing consulted <c>ISyntaxHighlightProvider.BuiltInDefinitionName</c>,
    /// so the editor for a .bas file had no highlighting definition at all.
    /// </remarks>
    [AvaloniaFact]
    public async Task AQuickBasicFileOpensWithItsKeywordsColoured()
    {
        var project = WriteVbConsoleProject("Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");
        var basPath = WriteFile(Path.Combine(_root, "game.bas"),
            "DIM score AS INTEGER\nscore = 1\nPRINT score\nEND\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(basPath);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, basPath);
        Assert.Equal("VB", editor.SyntaxHighlighting?.Name);
    }

    /// <summary>
    /// Debug > Start on an open QuickBASIC file interprets the file itself:
    /// there is nothing to build, and what the program prints reaches the
    /// Output panel.
    /// </summary>
    [AvaloniaFact]
    public async Task StartingAnOpenQuickBasicFileRunsItsProgramThroughTheInterpreter()
    {
        var project = WriteVbConsoleProject("Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");
        var basPath = WriteFile(Path.Combine(_root, "hello.bas"), "PRINT \"basalt-quick\"\nEND\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(basPath);
        await host.SettleAsync(5);

        InvokeMenu(w, L(StringKeys.MenuDebug), L(StringKeys.MenuDebugStart));

        var printed = await WaitUntilAsync(host,
            () => vm.BuildOutput.Any(line => line.Contains("basalt-quick", StringComparison.Ordinal)),
            maxPasses: 600);

        Assert.True(printed, "Debug > Start printed nothing. Output:\n" + string.Join("\n", vm.BuildOutput));
    }
}
