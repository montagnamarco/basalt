using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Database;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The code editor's shortcuts pressed the way a person presses them: a real
/// <see cref="MainWindow"/>, a real project on disk, and key presses that land
/// on the focused code editor. Chords, Duplicate Line and Redo, under the
/// keyboard schemes that differ on them.
///
/// The scheme is written to the settings file before the window opens, which
/// is how the window reads it. BASALT_SETTINGS is process-wide, so the class
/// shares the settings collection with the other tests that set it.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class EditorShortcutTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-editor-shortcuts-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-editor-shortcuts-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings = Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public EditorShortcutTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ---- fixtures -----------------------------------------------------

    private const string ProgramSource = "Module Program\nSub Main()\nEnd Sub\nEnd Module\n";

    // Format Document indents the body of Main; the source above has none.
    private const string FormattedMainLine = "    Sub Main()";

    private string WriteProject(string programSource)
    {
        var project = Path.Combine(_root, "Shortcuts.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <MyType>Empty</MyType>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Program.vb"), programSource);
        return project;
    }

    /// <summary>Writes the keyboard scheme the next window will open with.</summary>
    private void ChooseScheme(string scheme) =>
        new SettingsStore(_settings).Save(new IdeSettings { Keyboard = new KeyboardSettings { Scheme = scheme } });

    /// <summary>Opens Program.vb in the window and gives the code editor the keyboard.</summary>
    private static async Task<TextEditor> OpenProgramAsync(TestWindow host, string programPath)
    {
        await host.SettleAsync(10);
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenFileAsync(programPath);
        await host.SettleAsync(5);

        var code = host.Window.CodeEditorForTests(programPath);
        Assert.NotNull(code);
        var editor = code!.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.TextArea.Focus();
        return editor;
    }

    private string ProgramPath => Path.Combine(_root, "Program.vb");

    private static void PressKey(MainWindow window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }

    private static void Ctrl(MainWindow window, Key key, PhysicalKey physical) =>
        PressKey(window, key, RawInputModifiers.Control, physical);

    private static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, int maxPasses = 100)
    {
        for (var i = 0; i < maxPasses; i++)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }
        return condition();
    }

    private static string Waiting(string firstHalf) => Localizer.Get(StringKeys.StatusChordWaiting, firstHalf);

    // ---- chords -------------------------------------------------------

    /// <summary>Ctrl+K, Ctrl+D is Format Document under the Basalt scheme.</summary>
    [AvaloniaFact]
    public async Task CtrlKThenCtrlDFormatsTheDocumentInTheBasaltScheme()
    {
        using var host = new TestWindow(WriteProject(ProgramSource));
        var editor = await OpenProgramAsync(host, ProgramPath);

        Ctrl(host.Window, Key.K, PhysicalKey.K);
        Ctrl(host.Window, Key.D, PhysicalKey.D);

        Assert.True(await WaitUntilAsync(host, () => editor.Text.Contains(FormattedMainLine, StringComparison.Ordinal)),
            "Ctrl+K, Ctrl+D did not format the document.");
    }

    /// <summary>The same chord is Format Document under Visual Studio too.</summary>
    [AvaloniaFact]
    public async Task CtrlKThenCtrlDFormatsTheDocumentInTheVisualStudioScheme()
    {
        ChooseScheme("VisualStudio");
        using var host = new TestWindow(WriteProject(ProgramSource));
        var editor = await OpenProgramAsync(host, ProgramPath);

        Ctrl(host.Window, Key.K, PhysicalKey.K);
        Ctrl(host.Window, Key.D, PhysicalKey.D);

        Assert.True(await WaitUntilAsync(host, () => editor.Text.Contains(FormattedMainLine, StringComparison.Ordinal)),
            "Ctrl+K, Ctrl+D did not format the document under the Visual Studio scheme.");
    }

    /// <summary>
    /// Settings > Keyboard decides what a chord does: Visual Studio Code gives
    /// Format Document Ctrl+Shift+I, so Ctrl+K, Ctrl+D does nothing there.
    /// </summary>
    [AvaloniaFact]
    public async Task TheVisualStudioCodeSchemeHasNoFormatChord()
    {
        ChooseScheme("VisualStudioCode");
        using var host = new TestWindow(WriteProject(ProgramSource));
        var editor = await OpenProgramAsync(host, ProgramPath);

        Ctrl(host.Window, Key.K, PhysicalKey.K);
        Ctrl(host.Window, Key.D, PhysicalKey.D);
        await host.SettleAsync(30);

        Assert.DoesNotContain(FormattedMainLine, editor.Text);
    }

    /// <summary>The first half is held, and the status bar says the chord is waiting.</summary>
    [AvaloniaFact]
    public async Task CtrlKSaysItIsWaitingForTheSecondKeyUntilEscapeCancelsIt()
    {
        using var host = new TestWindow(WriteProject(ProgramSource));
        await OpenProgramAsync(host, ProgramPath);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        // The solution is still reporting its load: wait until the status bar is quiet.
        await host.SettleAsync(30);
        var before = vm.StatusMessage;

        Ctrl(w, Key.K, PhysicalKey.K);
        Assert.Equal(Waiting("Ctrl+K"), vm.StatusMessage);

        PressKey(w, Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);
        Assert.Equal(before, vm.StatusMessage);
    }

    /// <summary>
    /// A key that completes no chord cancels the chord, so the Ctrl+D that
    /// follows it does not format the document.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape)]
    [InlineData(Key.L, RawInputModifiers.Control, PhysicalKey.L)]
    public async Task AKeyThatCompletesNoChordCancelsIt(Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        using var host = new TestWindow(WriteProject(ProgramSource));
        var editor = await OpenProgramAsync(host, ProgramPath);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        Ctrl(w, Key.K, PhysicalKey.K);
        PressKey(w, key, modifiers, physical);
        Assert.NotEqual(Waiting("Ctrl+K"), vm.StatusMessage);

        Ctrl(w, Key.D, PhysicalKey.D);
        await host.SettleAsync(30);

        Assert.DoesNotContain(FormattedMainLine, editor.Text);
    }

    /// <summary>
    /// Leaving the window (losing its activation) abandons a chord that waits:
    /// the status bar is put back, and the key pressed on return is an
    /// ordinary press, not the second half of the chord.
    /// </summary>
    [AvaloniaFact]
    public async Task DeactivatingTheWindowCancelsAWaitingChord()
    {
        using var host = new TestWindow(WriteProject(ProgramSource));
        var editor = await OpenProgramAsync(host, ProgramPath);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await host.SettleAsync(30);
        var before = vm.StatusMessage;

        Ctrl(w, Key.K, PhysicalKey.K);
        Assert.Equal(Waiting("Ctrl+K"), vm.StatusMessage);

        RaiseDeactivated(w);
        Assert.Equal(before, vm.StatusMessage);

        Ctrl(w, Key.D, PhysicalKey.D);
        await host.SettleAsync(30);
        Assert.DoesNotContain(FormattedMainLine, editor.Text);
    }

    /// <summary>
    /// Raises the window's Deactivated event the way the platform does when
    /// the window loses activation. Headless windows never lose it by
    /// themselves, so the event's handler list is invoked directly.
    /// </summary>
    private static void RaiseDeactivated(Window window)
    {
        for (var type = window.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField("Deactivated", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (field?.GetValue(window) is Delegate handlers)
            {
                handlers.DynamicInvoke(window, EventArgs.Empty);
                return;
            }
        }

        Assert.Fail("The window does not expose its Deactivated handlers to the test.");
    }

    /// <summary>A shortcut that is not a chord's first half runs at once, with nothing waiting.</summary>
    [AvaloniaFact]
    public async Task ANonChordShortcutRunsAtOnce()
    {
        using var host = new TestWindow(WriteProject(ProgramSource));
        var editor = await OpenProgramAsync(host, ProgramPath);
        var original = editor.Text;
        editor.CaretOffset = original.IndexOf("End Sub", StringComparison.Ordinal);

        host.Window.KeyTextInput("' typed");
        Assert.NotEqual(original, editor.Text);

        Ctrl(host.Window, Key.Z, PhysicalKey.Z);

        Assert.Equal(original, editor.Text);
    }

    // ---- Duplicate Line -----------------------------------------------

    // Indented as the formatter would have it: the line-leave formatter would
    // otherwise correct the body of Main while the test was looking at it.
    private const string CallSource =
        "Module Program\n    Sub Main()\n        Console.WriteLine(1)\n    End Sub\nEnd Module\n";

    private const string CallLine = "        Console.WriteLine(1)\n";

    /// <summary>With nothing selected, Ctrl+D copies the line the caret is on, and the caret goes to the copy.</summary>
    [AvaloniaFact]
    public async Task CtrlDDuplicatesTheCaretLineAndPutsTheCaretOnTheCopy()
    {
        using var host = new TestWindow(WriteProject(CallSource));
        var editor = await OpenProgramAsync(host, ProgramPath);

        var line = editor.Document.GetLineByNumber(3);
        var caretInLine = line.Offset + 6;
        var column = caretInLine - line.Offset;
        editor.CaretOffset = caretInLine;

        Ctrl(host.Window, Key.D, PhysicalKey.D);

        Assert.Equal(CallSource.Replace(CallLine, CallLine + CallLine),
            editor.Text);
        var copy = editor.Document.GetLineByNumber(4);
        Assert.Equal(copy.Offset + column, editor.CaretOffset);
    }

    /// <summary>With a selection, Ctrl+D inserts a copy straight after it and leaves the caret at its end.</summary>
    [AvaloniaFact]
    public async Task CtrlDDuplicatesTheSelectionAfterItself()
    {
        using var host = new TestWindow(WriteProject(CallSource));
        var editor = await OpenProgramAsync(host, ProgramPath);

        const string selected = "WriteLine(1)";
        var start = CallSource.IndexOf(selected, StringComparison.Ordinal);
        editor.Select(start, selected.Length);

        Ctrl(host.Window, Key.D, PhysicalKey.D);

        Assert.Equal(CallSource.Replace(selected, selected + selected), editor.Text);
        Assert.Equal(0, editor.SelectionLength);
        Assert.Equal(start + selected.Length * 2, editor.CaretOffset);
    }

    /// <summary>The duplicate is one edit: a single Ctrl+Z takes it back.</summary>
    [AvaloniaFact]
    public async Task CtrlDIsOneUndoStep()
    {
        using var host = new TestWindow(WriteProject(CallSource));
        var editor = await OpenProgramAsync(host, ProgramPath);
        editor.CaretOffset = CallSource.IndexOf("WriteLine", StringComparison.Ordinal);

        Ctrl(host.Window, Key.D, PhysicalKey.D);
        var duplicated = CallSource.Replace(CallLine, CallLine + CallLine);
        Assert.Equal(duplicated, editor.Text);

        Ctrl(host.Window, Key.Z, PhysicalKey.Z);

        Assert.Equal(CallSource, editor.Text);
    }

    /// <summary>Edit &gt; Duplicate Line shows the shortcut it answers to.</summary>
    [AvaloniaFact]
    public async Task TheEditMenuOffersDuplicateLineWithItsShortcut()
    {
        using var host = new TestWindow();
        await host.SettleAsync(10);

        var edit = host.Window.MenuEntriesForTests()
            .Single(entry => entry.Header == Localizer.Get(StringKeys.MenuEdit));
        var item = edit.Children!
            .Single(entry => entry.Header == Localizer.Get(StringKeys.MenuEditDuplicateLine));

        var gesture = Assert.IsType<KeyGesture>(item.Gesture);
        Assert.Equal(Key.D, gesture.Key);
        Assert.Equal(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control, gesture.KeyModifiers);
    }

    /// <summary>
    /// The second key of a chord is the chord's: the editor's own Ctrl+D (delete
    /// line) must not see it, so the document keeps its lines.
    /// </summary>
    [AvaloniaFact]
    public async Task CtrlKThenCtrlDFormatsWithoutDeletingALine()
    {
        using var host = new TestWindow(WriteProject(ProgramSource));
        var editor = await OpenProgramAsync(host, ProgramPath);
        var lines = editor.Document.LineCount;

        Ctrl(host.Window, Key.K, PhysicalKey.K);
        Ctrl(host.Window, Key.D, PhysicalKey.D);

        Assert.True(await WaitUntilAsync(host, () => editor.Text.Contains(FormattedMainLine, StringComparison.Ordinal)),
            "Ctrl+K, Ctrl+D did not format the document.");
        Assert.Equal(lines, editor.Document.LineCount);
    }

    // ---- Keys that belong to other controls ------------------------------

    /// <summary>
    /// Ctrl+D in a focused terminal is the shell's end of input, which ends an
    /// idle shell. It must reach the shell and not duplicate a line in the
    /// editor that is open behind the terminal.
    /// </summary>
    [AvaloniaFact]
    public async Task CtrlDInTheTerminalGoesToTheShellNotTheEditor()
    {
        using var host = new TestWindow(WriteProject(CallSource));
        var editor = await OpenProgramAsync(host, ProgramPath);
        var w = host.Window;
        editor.CaretOffset = CallSource.IndexOf("WriteLine", StringComparison.Ordinal);
        var original = editor.Text;

        var terminal = await OpenTerminalAsync(host);
        Assert.True(await WaitUntilAsync(host, () => terminal.ScreenText.Trim().Length > 0, maxPasses: 300),
            "The shell printed no prompt.");

        var exited = false;
        terminal.ProcessExited += (_, _) => exited = true;

        Ctrl(w, Key.D, PhysicalKey.D);

        Assert.True(await WaitUntilAsync(host, () => exited, maxPasses: 300),
            "Ctrl+D did not reach the shell, which should end on end of input.");
        Assert.Equal(original, editor.Text);
    }

    /// <summary>
    /// Ctrl+C in a focused terminal is the shell's interrupt, so it reaches the
    /// terminal rather than copying in the editor behind it, and the editor keeps
    /// its text. Whether a running program then stops is the PTY's business.
    /// </summary>
    [AvaloniaFact]
    public async Task CtrlCInTheTerminalInterruptsTheProgramAndLeavesTheEditorAlone()
    {
        using var host = new TestWindow(WriteProject(CallSource));
        var editor = await OpenProgramAsync(host, ProgramPath);
        var w = host.Window;
        var original = editor.Text;

        var terminal = await OpenTerminalAsync(host);
        Assert.True(await WaitUntilAsync(host, () => terminal.ScreenText.Trim().Length > 0, maxPasses: 300),
            "The shell printed no prompt.");

        w.KeyTextInput("sleep 30");
        PressKey(w, Key.Enter, RawInputModifiers.None, PhysicalKey.Enter);
        await host.SettleAsync(20);

        Ctrl(w, Key.C, PhysicalKey.C);

        // The terminal's line discipline echoes the interrupt as ^C once the
        // byte reaches the shell's terminal. Copy in the editor would echo nothing.
        Assert.True(await WaitUntilAsync(host, () => terminal.ScreenText.Contains("^C", StringComparison.Ordinal), maxPasses: 150),
            "Ctrl+C did not reach the terminal. Screen: " + terminal.ScreenText.Replace("\n", " | ", StringComparison.Ordinal));
        Assert.Equal(original, editor.Text);
    }

    /// <summary>Opens a terminal from View &gt; New Terminal and gives it the keyboard, as a click in it does.</summary>
    internal static async Task<TerminalView> OpenTerminalAsync(TestWindow host)
    {
        var w = host.Window;
        var view = w.MenuEntriesForTests().Single(entry => entry.Header == Localizer.Get(StringKeys.MenuView));
        var item = view.Children!.Single(entry => entry.Header == Localizer.Get(StringKeys.MenuViewNewTerminal));
        item.Action!.Invoke();
        await host.SettleAsync(10);

        var terminal = w.GetVisualDescendants().OfType<TerminalView>().Single();
        w.Activate();
        terminal.Focus();
        await host.SettleAsync(3);

        Assert.True(terminal.IsKeyboardFocusWithin, "The terminal did not take the keyboard.");
        return terminal;
    }

    /// <summary>With the Find box focused, Ctrl+D is the box's key: the editor's line is not copied.</summary>
    [AvaloniaFact]
    public async Task CtrlDInTheFindBoxDoesNotDuplicateTheEditorLine()
    {
        using var host = new TestWindow(WriteProject(CallSource));
        var editor = await OpenProgramAsync(host, ProgramPath);
        var w = host.Window;
        editor.CaretOffset = CallSource.IndexOf("WriteLine", StringComparison.Ordinal);
        var original = editor.Text;

        Ctrl(w, Key.F, PhysicalKey.F);
        await host.SettleAsync(5);
        Assert.False(editor.TextArea.IsKeyboardFocusWithin, "Ctrl+F did not move the keyboard to the Find box.");

        Ctrl(w, Key.D, PhysicalKey.D);
        await host.SettleAsync(5);

        Assert.Equal(original, editor.Text);
    }

    /// <summary>Esc closes the find bar, and it also abandons a chord waiting in the editor.</summary>
    [AvaloniaFact]
    public async Task EscapeClosesTheFindBarAndAbandonsAWaitingChord()
    {
        using var host = new TestWindow(WriteProject(ProgramSource));
        var editor = await OpenProgramAsync(host, ProgramPath);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        Ctrl(w, Key.F, PhysicalKey.F);
        await host.SettleAsync(5);
        editor.TextArea.Focus();

        // The solution is still reporting its load: wait until the status bar is quiet.
        await host.SettleAsync(30);
        var before = vm.StatusMessage;

        Ctrl(w, Key.K, PhysicalKey.K);
        Assert.Equal(Waiting("Ctrl+K"), vm.StatusMessage);

        PressKey(w, Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);

        Assert.Equal(before, vm.StatusMessage);
    }

    /// <summary>A press anywhere in the window abandons a chord that waits, and puts the status bar back.</summary>
    [AvaloniaFact]
    public async Task APointerPressAbandonsAWaitingChordAndRestoresTheStatus()
    {
        using var host = new TestWindow(WriteProject(ProgramSource));
        var editor = await OpenProgramAsync(host, ProgramPath);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await host.SettleAsync(30);
        var before = vm.StatusMessage;

        Ctrl(w, Key.K, PhysicalKey.K);
        Assert.Equal(Waiting("Ctrl+K"), vm.StatusMessage);

        var point = editor.TranslatePoint(new Point(20, 20), w)
            ?? throw new InvalidOperationException("The editor is not on screen to click.");
        w.MouseDown(point, MouseButton.Left);
        w.MouseUp(point, MouseButton.Left);
        await host.SettleAsync(3);

        Assert.Equal(before, vm.StatusMessage);
    }

    /// <summary>
    /// Ctrl+D in a query editor is that editor's key. The code editor behind
    /// it keeps its lines, although the query editor is not a document.
    /// </summary>
    [AvaloniaFact]
    public async Task CtrlDInAQueryEditorDoesNotDuplicateALineOfTheCodeEditor()
    {
        using var host = new TestWindow(WriteProject(CallSource));
        var editor = await OpenProgramAsync(host, ProgramPath);
        var w = host.Window;
        editor.CaretOffset = CallSource.IndexOf("WriteLine", StringComparison.Ordinal);
        var original = editor.Text;

        InvokeMenuItem(w, Localizer.Get(StringKeys.DbMenu), Localizer.Get(StringKeys.DbNewQuery));
        await host.SettleAsync(5);

        var query = Assert.Single(w.DatabaseDocumentsForTests.Select(document => document.View).OfType<QueryView>());
        var queryEditor = query.GetVisualDescendants().OfType<TextEditor>().Single();
        queryEditor.TextArea.Focus();
        await host.SettleAsync(3);
        Assert.True(queryEditor.TextArea.IsKeyboardFocusWithin, "The query editor did not take the keyboard.");

        Ctrl(w, Key.D, PhysicalKey.D);
        await host.SettleAsync(5);

        Assert.Equal(original, editor.Text);
    }

    /// <summary>Invokes a menu item by its header path, as a click on it does.</summary>
    private static void InvokeMenuItem(MainWindow window, string top, string item)
    {
        var menu = window.MenuEntriesForTests().Single(entry => entry.Header == top);
        menu.Children!.Single(entry => entry.Header == item).Action!.Invoke();
    }

    /// <summary>
    /// When the keyboard goes elsewhere, a chord waiting in the editor is
    /// abandoned and the status bar is put back, not left saying it waits.
    /// </summary>
    [AvaloniaFact]
    public async Task MovingTheKeyboardAwayCancelsAWaitingChordAndRestoresTheStatus()
    {
        using var host = new TestWindow(WriteProject(ProgramSource));
        var editor = await OpenProgramAsync(host, ProgramPath);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await host.SettleAsync(30);
        var before = vm.StatusMessage;

        Ctrl(w, Key.K, PhysicalKey.K);
        Assert.Equal(Waiting("Ctrl+K"), vm.StatusMessage);

        // A toolbar button outside the editor takes the keyboard.
        var elsewhere = w.GetVisualDescendants().OfType<Button>().First(b => b.Focusable && b.IsEffectivelyVisible);
        elsewhere.Focus();
        await host.SettleAsync(3);
        Assert.False(editor.TextArea.IsKeyboardFocusWithin, "The keyboard did not leave the editor.");

        Ctrl(w, Key.L, PhysicalKey.L);

        Assert.Equal(before, vm.StatusMessage);
    }

    // ---- Duplicate Line, at the edges -----------------------------------

    /// <summary>The last line has no line break after it, so the copy brings one of its own.</summary>
    [AvaloniaFact]
    public async Task CtrlDDuplicatesTheLastLineWhichHasNoNewlineAfterIt()
    {
        const string source = "Module Program\n    Sub Main()\n        Console.WriteLine(1)\n    End Sub\nEnd Module";
        using var host = new TestWindow(WriteProject(source));
        var editor = await OpenProgramAsync(host, ProgramPath);

        const int column = 3;
        var last = editor.Document.GetLineByNumber(5);
        editor.CaretOffset = last.Offset + column;

        Ctrl(host.Window, Key.D, PhysicalKey.D);

        Assert.Equal(source + "\nEnd Module", editor.Text);
        var copy = editor.Document.GetLineByNumber(6);
        Assert.Equal(copy.Offset + column, editor.CaretOffset);
    }

    /// <summary>A selection over whole lines is copied straight after itself, and the caret ends on the copy.</summary>
    [AvaloniaFact]
    public async Task CtrlDCopiesAMultiLineSelectionAfterItself()
    {
        const string block = "    Sub Main()\n        Console.WriteLine(1)\n";
        using var host = new TestWindow(WriteProject(CallSource));
        var editor = await OpenProgramAsync(host, ProgramPath);

        var start = CallSource.IndexOf(block, StringComparison.Ordinal);
        editor.Select(start, block.Length);

        Ctrl(host.Window, Key.D, PhysicalKey.D);

        Assert.Equal(CallSource.Replace(block, block + block), editor.Text);
        Assert.Equal(0, editor.SelectionLength);
        Assert.Equal(start + block.Length * 2, editor.CaretOffset);
    }

    // ---- Redo ---------------------------------------------------------

    /// <summary>
    /// Ctrl+Y and Ctrl+Shift+Z both redo in the code editor under the Basalt
    /// and Visual Studio schemes. Each scheme names one of them in its menu,
    /// so the other has to be answered without being listed.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Basalt", Key.Y, RawInputModifiers.Control, PhysicalKey.Y)]
    [InlineData("Basalt", Key.Z, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Z)]
    [InlineData("VisualStudio", Key.Y, RawInputModifiers.Control, PhysicalKey.Y)]
    [InlineData("VisualStudio", Key.Z, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Z)]
    public async Task RedoAnswersToCtrlYAndCtrlShiftZ(
        string scheme, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        ChooseScheme(scheme);
        using var host = new TestWindow(WriteProject(ProgramSource));
        var editor = await OpenProgramAsync(host, ProgramPath);
        var w = host.Window;

        var original = editor.Text;
        editor.CaretOffset = original.IndexOf("End Sub", StringComparison.Ordinal);
        w.KeyTextInput("' typed");
        var typed = editor.Text;
        Assert.NotEqual(original, typed);

        Ctrl(w, Key.Z, PhysicalKey.Z);
        Assert.True(await WaitUntilAsync(host, () => editor.Text == original, maxPasses: 30),
            "Ctrl+Z did not undo the typing, so the redo cannot be checked.");

        PressKey(w, key, modifiers, physical);

        Assert.True(await WaitUntilAsync(host, () => editor.Text == typed, maxPasses: 30),
            $"{modifiers} + {key} did not redo under the {scheme} scheme. The text is now: " +
            editor.Text.Replace("\n", " | ", StringComparison.Ordinal));
    }
}
