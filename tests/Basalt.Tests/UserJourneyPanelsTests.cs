using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Debugging;
using Dock.Model.Core;
using Microsoft.Data.Sqlite;

namespace Basalt.Tests;

/// <summary>
/// End-to-end user journeys through a real <see cref="MainWindow"/> for the
/// tool panels around the editor: the database panel, the debugger's
/// Breakpoints, Call Stack and Variables panels, Find References, and the
/// docking of tool panels. Each is driven the way a person would drive it:
/// menus, shortcuts, real pointer presses and the controls a panel shows.
/// </summary>
public sealed class UserJourneyPanelsTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-journey-panels-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string L(string key) => Localizer.Get(key);

    private static string WriteConsoleProject(string root, string body)
    {
        var projectPath = Path.Combine(root, "App.vbproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Program.vb"), body);
        return projectPath;
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

    /// <summary>Pumps the dispatcher while a predicate holds, for work done on a worker thread.</summary>
    private static async Task PumpUntilAsync(Func<bool> done, int attempts = 200, int delayMs = 30)
    {
        for (var i = 0; i < attempts && !done(); i++)
        {
            await Task.Delay(delayMs);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        }
    }

    private static IdeDockFactory FactoryOf(MainWindow window) =>
        (IdeDockFactory)typeof(MainWindow)
            .GetField("_factory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static Dictionary<IDockable, Control> ContentsOf(MainWindow window) =>
        (Dictionary<IDockable, Control>)typeof(MainWindow)
            .GetField("_contents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

    /// <summary>A real press and release at the middle of a control, as a mouse would send it.</summary>
    private static async Task ClickAsync(TestWindow host, Visual target)
    {
        var centre = new Point(target.Bounds.Width / 2, target.Bounds.Height / 2);
        var point = target.TranslatePoint(centre, host.Window)
            ?? throw new InvalidOperationException("The control is not on screen to click.");

        host.Window.MouseDown(point, MouseButton.Left);
        host.Window.MouseUp(point, MouseButton.Left);
        await host.SettleAsync(3);
    }

    /// <summary>
    /// The double-click the tree and list controls act on. The headless window
    /// sends single presses only, so the double tap is raised on the row after
    /// the real press, as the other journeys do.
    /// </summary>
    private static void DoubleTap(Interactive row) =>
        row.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));

    /// <summary>
    /// Clicks the margin at the vertical middle of a line, a real pointer event
    /// rather than a direct call into the margin's Toggle.
    /// </summary>
    private static void ClickLine(
        BreakpointMargin margin, AvaloniaEdit.TextEditor editor, int line, Visual window)
    {
        var view = editor.TextArea.TextView;
        var visualLine = view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(line));

        var inView = new Point(
            0, visualLine.VisualTop - view.VerticalOffset + (visualLine.Height / 2));

        var origin = margin.TranslatePoint(new Point(0, 0), window)
            ?? throw new InvalidOperationException("The margin is not in the window.");

        margin.RaiseEvent(new PointerPressedEventArgs(
            margin,
            new Pointer(0, PointerType.Mouse, isPrimary: true),
            margin,
            new Point(margin.Bounds.Width / 2, inView.Y + origin.Y),
            timestamp: 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));
    }

    /// <summary>The 1-based line the caret is on in an editor.</summary>
    private static int CaretLine(AvaloniaEdit.TextEditor editor) =>
        editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber;

    // ------------------------------------------------------------------
    // Journey 2: debugging. A breakpoint in a Sub called from Main; F5 stops
    // there; Call Stack lists both frames; double-clicking the Main frame
    // opens Program.vb at the call line; Variables shows the local; the
    // breakpoint is disabled in the Breakpoints panel and Continue runs to
    // the end. Skipped when netcoredbg is not installed.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task BreakpointInASubShowsBothFramesAndTheLocalAndContinueRunsToTheEnd()
    {
        Assert.SkipWhen(DebugAdapterLocator.Find() is null, "netcoredbg is not installed.");

        // Line 4 calls Helper; line 9 is inside Helper, after doubled is set.
        var project = WriteConsoleProject(_root, """
            Module Program
                Sub Main()
                    Dim answer As Integer = 41
                    Helper(answer)
                End Sub

                Sub Helper(value As Integer)
                    Dim doubled As Integer = value * 2
                    Console.WriteLine("basalt-panels " & doubled)
                End Sub
            End Module
            """);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(Path.Combine(_root, "Program.vb"));
        await host.SettleAsync(8);

        var code = w.GetVisualDescendants().OfType<CodeEditor>().Single();
        var editor = code.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();

        ClickLine(code.BreakpointMargin, editor, 9, w);
        Assert.Contains(9, code.BreakpointMargin.Lines);

        var factory = FactoryOf(w);
        var contents = ContentsOf(w);

        InvokeMenu(w, L(StringKeys.MenuDebug), L(StringKeys.MenuDebugStart));

        var callStack = Assert.IsType<CallStackPanel>(contents[factory.CallStack]);
        var variables = Assert.IsType<VariablesPanel>(contents[factory.Variables]);
        var breakpoints = Assert.IsType<BreakpointsPanel>(contents[factory.Breakpoints]);

        await PumpUntilAsync(() => callStack.Frames.Count >= 2, attempts: 600, delayMs: 100);

        Assert.Equal(2, callStack.Frames.Count);
        Assert.Contains("Helper", callStack.Frames[0].Method, StringComparison.Ordinal);
        Assert.Contains("Main", callStack.Frames[1].Method, StringComparison.Ordinal);

        // The local inside Helper, read while stopped in the innermost frame.
        await PumpUntilAsync(() => variables.Nodes.Any(n => n.Value.Name == "doubled"), attempts: 200, delayMs: 50);
        Assert.Equal("82", variables.Nodes.Single(n => n.Value.Name == "doubled").Value.Value);

        // Double-click the Main frame: Program.vb opens with the caret on the call line.
        var frames = callStack.GetVisualDescendants().OfType<ListBox>().Single();
        var mainFrame = frames.GetVisualDescendants().OfType<ListBoxItem>().ElementAt(1);
        await ClickAsync(host, mainFrame);
        DoubleTap(mainFrame);

        await PumpUntilAsync(() => w.CurrentEditorForTests() is { CaretOffset: > 0 } current
            && Path.GetFileName(vm.ActiveDocument?.FilePath) == "Program.vb"
            && CaretLine(current) == 4, attempts: 100);

        Assert.Equal("Program.vb", Path.GetFileName(vm.ActiveDocument?.FilePath));
        Assert.Equal(4, CaretLine(w.CurrentEditorForTests()!));

        // The user brings the Breakpoints tab forward, as View > Breakpoints does.
        InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolBreakpoints));
        await host.SettleAsync(5);

        // Disable the breakpoint from the Breakpoints panel's own checkbox.
        await PumpUntilAsync(() => breakpoints.Breakpoints.Any(b => b.Line == 9), attempts: 100);
        var row = breakpoints.GetVisualDescendants().OfType<CheckBox>().Single();
        row.IsChecked = false;
        await host.SettleAsync(3);

        Assert.False(breakpoints.Breakpoints.Single(b => b.Line == 9).Enabled);

        // Continue runs to the end: the program's own output appears, in the Terminal tab a console program
        // is debugged in.
        InvokeMenu(w, L(StringKeys.MenuDebug), L(StringKeys.MenuDebugContinue));

        string ConsoleText() => string.Join("\n", ((Dictionary<Dock.Model.Core.IDockable, Control>)typeof(MainWindow)
            .GetField("_contents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(w)!).Values.OfType<TerminalView>().Select(t => t.ScreenText));

        await PumpUntilAsync(
            () => ConsoleText().Contains("basalt-panels 82", StringComparison.Ordinal) && !vm.IsRunning,
            attempts: 600, delayMs: 100);

        Assert.Contains("basalt-panels 82", ConsoleText(), StringComparison.Ordinal);
        Assert.False(vm.IsRunning);
    }

    // ------------------------------------------------------------------
    // Journey 3: Find References. Shift+F12 on a Sub used in three places
    // across two files lists three rows grouped by file; double-clicking
    // each lands on its line; running it again on another symbol replaces
    // the list.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ShiftF12ListsReferencesGroupedByFileAndDoubleClickLandsOnEachLine()
    {
        var project = WriteConsoleProject(_root, """
            Module Program
                Sub Main()
                    Helper()
                    Helper()
                    Other.Run()
                End Sub

                Sub Helper()
                End Sub
            End Module
            """);
        var otherPath = Path.Combine(_root, "Other.vb");
        File.WriteAllText(otherPath, """
            Module Other
                Sub Run()
                    Program.Helper()
                End Sub
            End Module
            """);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        var programPath = Path.Combine(_root, "Program.vb");

        await vm.OpenFileAsync(programPath);
        await host.SettleAsync(8);

        var editor = w.CurrentEditorForTests()!;
        editor.Focus();
        editor.CaretOffset = editor.Text.IndexOf("Sub Helper", StringComparison.Ordinal) + "Sub ".Length + 1;

        var factory = FactoryOf(w);
        var references = Assert.IsType<ReferencesPanel>(ContentsOf(w)[factory.References]);

        w.KeyPress(Key.F12, RawInputModifiers.Shift, PhysicalKey.F12, null);

        // Three uses of Helper, in two files, and the declaration itself: the
        // language server includes the declaration, as Find All References does
        // in other IDEs, so four rows.
        await PumpUntilAsync(() => references.Rows.Count == 4, attempts: 300, delayMs: 50);

        Assert.Equal(4, references.Rows.Count);

        // Grouped by file: all rows of one file sit together, so each file starts one block.
        var files = references.Rows.Select(r => Path.GetFileName(r.Location.FilePath)).ToList();
        var blocks = files.Where((f, i) => i == 0 || files[i - 1] != f).ToList();
        Assert.Equal(blocks.Distinct().Count(), blocks.Count);

        // Double-click each row: the file opens with the caret on that line.
        var list = references.ListForTests;
        for (var i = 0; i < references.Rows.Count; i++)
        {
            var row = references.Rows[i];
            var item = list.GetVisualDescendants().OfType<ListBoxItem>().ElementAt(i);
            await ClickAsync(host, item);
            DoubleTap(item);

            var expectedPath = Path.GetFullPath(row.Location.FilePath);
            var expectedLine = row.Location.Range.Start.Line;

            await PumpUntilAsync(() => Path.GetFullPath(vm.ActiveDocument?.FilePath ?? "") == expectedPath
                && w.CurrentEditorForTests() is { } current && CaretLine(current) == expectedLine, attempts: 100);

            Assert.Equal(expectedPath, Path.GetFullPath(vm.ActiveDocument?.FilePath ?? ""));
            Assert.Equal(expectedLine, CaretLine(w.CurrentEditorForTests()!));
        }

        await vm.OpenFileAsync(otherPath);
        await host.SettleAsync(8);
        var otherEditor = w.CurrentEditorForTests()!;
        otherEditor.Focus();
        otherEditor.CaretOffset = otherEditor.Text.IndexOf("Sub Run", StringComparison.Ordinal) + "Sub ".Length + 1;
        w.KeyPress(Key.F12, RawInputModifiers.Shift, PhysicalKey.F12, null);

        // Run is called once (Program.vb) and declared once (Other.vb).
        await PumpUntilAsync(() => references.Rows.Count == 2, attempts: 300, delayMs: 50);

        Assert.Equal(2, references.Rows.Count);
        Assert.DoesNotContain(references.Rows, r => r.Preview.Contains("Helper", StringComparison.Ordinal));
        Assert.Contains("Run", references.Summary, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Journey 4: docking. A tool closed by the user comes back from View
    // into the area it lives in; a tool floated out of its area docks back
    // into it. Headless: no native drag gestures.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ClosedToolReopensFromViewInItsAreaAndAFloatingToolDocksBack()
    {
        using var host = new TestWindow();
        await host.SettleAsync(10);
        var w = host.Window;
        var factory = FactoryOf(w);

        // Toolbox is anchored in its own area; closing and reopening keeps it there.
        var toolboxArea = factory.ToolboxArea!;
        factory.RemoveDockable(factory.Toolbox, collapse: false);
        Assert.DoesNotContain(factory.Toolbox, toolboxArea.VisibleDockables ?? []);

        InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolToolbox));
        await host.SettleAsync(5);

        Assert.Same(toolboxArea, factory.Toolbox.Owner);
        Assert.Contains(factory.Toolbox, toolboxArea.VisibleDockables!);

        // References lives in the bottom strip; the same round trip keeps it there.
        var bottom = factory.BottomArea!;
        factory.RemoveDockable(factory.References, collapse: false);
        Assert.DoesNotContain(factory.References, bottom.VisibleDockables!);

        InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolReferences));
        await host.SettleAsync(5);

        Assert.Same(bottom, factory.References.Owner);
        Assert.Contains(factory.References, bottom.VisibleDockables!);

        // Float it out of the bottom strip, then dock it back into that strip.
        factory.FloatDockable(factory.References);
        await host.SettleAsync(5);

        var floating = Assert.IsAssignableFrom<IDock>(factory.References.Owner);
        Assert.NotSame(bottom, floating);
        Assert.DoesNotContain(factory.References, bottom.VisibleDockables!);

        factory.MoveDockable(floating, bottom, factory.References, null);
        await host.SettleAsync(5);

        Assert.Same(bottom, factory.References.Owner);
        Assert.Contains(factory.References, bottom.VisibleDockables!);
    }
}
