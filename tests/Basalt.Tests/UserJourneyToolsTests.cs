using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Core.Model;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Debugging;
using Dock.Model.Core;

namespace Basalt.Tests;

/// <summary>
/// End-to-end user journeys through a real <see cref="MainWindow"/> for the
/// tool panels around editing: build, run, the terminal, Git and the AI
/// assistant — each driven the way a person sitting at the IDE would drive
/// it, through the window's own command dispatch and the real controls a
/// panel shows, rather than by calling one method directly.
/// </summary>
public sealed class UserJourneyToolsTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-journey-tools-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string L(string key) => Localizer.Get(key);

    private static string WriteConsoleProject(string root, string body = "", string name = "App")
    {
        var projectPath = Path.Combine(root, $"{name}.vbproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Program.vb"),
            body.Length > 0
                ? body
                : "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");
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

    /// <summary>Pumps the dispatcher while a predicate holds, for work a build or run does on a worker thread.</summary>
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

    /// <summary>
    /// Clicks the margin at the vertical middle of a line, the way
    /// BreakpointMarginClickTests does: a real pointer event rather than a
    /// direct call into the margin's Toggle, which proved nothing about the
    /// click itself actually reaching it.
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

    // ------------------------------------------------------------------
    // Journey 1a: Build > Build Solution on a project with no errors
    // writes the build's own log to the Output panel.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task BuildSolutionWritesTheBuildLogToTheOutputPanel()
    {
        var project = WriteConsoleProject(_root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        InvokeMenu(w, L(StringKeys.MenuBuild), L(StringKeys.MenuBuildBuildSolution));

        await PumpUntilAsync(() => !vm.IsBusy, attempts: 300);

        Assert.NotEmpty(vm.BuildOutput);
        Assert.DoesNotContain(vm.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    // ------------------------------------------------------------------
    // Journey 1b: a project with a compile error shows the error in the
    // Problems panel, and double-clicking the row opens the file at the
    // line the compiler pointed at.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task BuildingAProjectWithACompileErrorShowsItInProblemsAndDoubleClickOpensTheFileAtTheLine()
    {
        var project = WriteConsoleProject(_root, """
            Module Program
                Sub Main()
                    NoSuchMethod()
                End Sub
            End Module
            """);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        InvokeMenu(w, L(StringKeys.MenuBuild), L(StringKeys.MenuBuildBuildSolution));

        await PumpUntilAsync(() => !vm.IsBusy, attempts: 300);

        var error = Assert.Single(vm.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(3, error.Line);

        var factory = FactoryOf(w);
        var contents = ContentsOf(w);

        var problems = Assert.IsType<ProblemsPanel>(contents[factory.Problems]);

        var row = problems.VisibleRows.Single(r => r.Id == error.Id);
        problems.Grid.SelectedItem = row;
        problems.Grid.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));

        await host.SettleAsync(10);
        await PumpUntilAsync(() => w.CurrentEditorForTests() is { CaretOffset: > 0 }, attempts: 50);

        Assert.Equal(Path.GetFullPath(error.FilePath!), Path.GetFullPath(vm.ActiveDocument?.FilePath ?? ""));

        var editor = w.CurrentEditorForTests();
        Assert.NotNull(editor);
        var openedLine = editor!.Document.GetLineByOffset(editor.CaretOffset).LineNumber;
        Assert.Equal(error.Line, openedLine);
    }

    /// <summary>
    /// The same double-click with another file already open in an editor: while the diagnostic's
    /// file was still being opened, that other editor was the only one realised, and the caret
    /// move landed there instead of in the file the diagnostic is about.
    /// </summary>
    [AvaloniaFact]
    public async Task DoubleClickingAProblemWithAnotherFileOpenMovesTheCaretOnlyInTheProblemsFile()
    {
        var project = WriteConsoleProject(_root, """
            Module Program
                Sub Main()
                    NoSuchMethod()
                End Sub
            End Module
            """);
        var otherPath = Path.Combine(_root, "Other.vb");
        File.WriteAllText(otherPath, "Module Other\n    Sub One()\n    End Sub\n\n    Sub Two()\n    End Sub\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(otherPath);
        await host.SettleAsync(10);
        var other = w.CodeEditorForTests(otherPath)!.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();
        other.CaretOffset = 0;

        InvokeMenu(w, L(StringKeys.MenuBuild), L(StringKeys.MenuBuildBuildSolution));
        await PumpUntilAsync(() => !vm.IsBusy, attempts: 300);
        var error = Assert.Single(vm.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var problems = Assert.IsType<ProblemsPanel>(ContentsOf(w)[FactoryOf(w).Problems]);
        problems.Grid.SelectedItem = problems.VisibleRows.Single(r => r.Id == error.Id);
        problems.Grid.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
        await host.SettleAsync(10);

        var programPath = Path.GetFullPath(error.FilePath!);
        await PumpUntilAsync(() => w.CodeEditorForTests(programPath) is not null, attempts: 50);
        var program = w.CodeEditorForTests(programPath)!.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();
        await PumpUntilAsync(() => program.CaretOffset > 0, attempts: 50);

        Assert.Equal(error.Line, program.Document.GetLineByOffset(program.CaretOffset).LineNumber);
        Assert.Equal(0, other.CaretOffset);
    }

    // ------------------------------------------------------------------
    // Journey 2: Run > Start Without Debugging shows the console app's own
    // output in the Output panel, and Run > Stop ends it.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task RunWithoutDebuggingShowsProgramOutputAndStopEndsIt()
    {
        var project = WriteConsoleProject(_root, """
            Module Program
                Sub Main()
                    Console.WriteLine("basalt-run-journey")
                    System.Threading.Thread.Sleep(5000)
                End Sub
            End Module
            """);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        InvokeMenu(w, L(StringKeys.MenuRun), L(StringKeys.MenuRunWithoutDebugging));

        // A console program runs in a Terminal tab, where it can be typed to: its output is there.
        string ConsoleText() => string.Join("\n", ((Dictionary<Dock.Model.Core.IDockable, Control>)typeof(MainWindow)
            .GetField("_contents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(w)!).Values.OfType<TerminalView>().Select(t => t.ScreenText));

        await PumpUntilAsync(
            () => ConsoleText().Contains("basalt-run-journey", StringComparison.Ordinal),
            attempts: 300);

        Assert.Contains("basalt-run-journey", ConsoleText(), StringComparison.Ordinal);
        Assert.True(vm.IsRunning);

        InvokeMenu(w, L(StringKeys.MenuRun), L(StringKeys.MenuRunStop));

        await PumpUntilAsync(() => !vm.IsRunning, attempts: 100);

        Assert.False(vm.IsRunning);
    }

    // ------------------------------------------------------------------
    // Journey 3: netcoredbg debugging. A breakpoint set by clicking the
    // margin, F5 breaking there, Locals showing the variable, Watch adding
    // an expression, F10 stepping, and Shift+F5 stopping and restoring the
    // design layout. Skipped, like Debugger*Tests, when netcoredbg is not
    // installed.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task DebuggingBreaksAtABreakpointShowsLocalsAndWatchStepsAndStopsRestoringTheLayout()
    {
        Assert.SkipWhen(DebugAdapterLocator.Find() is null, "netcoredbg is not installed.");

        var project = WriteConsoleProject(_root, """
            Module Program
                Sub Main()
                    Dim total As Integer = 0
                    For i As Integer = 1 To 3
                        total += i
                    Next
                    Console.WriteLine(total)
                End Sub
            End Module
            """);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var programFile = Path.Combine(_root, "Program.vb");
        await vm.OpenFileAsync(programFile);
        await host.SettleAsync(8);

        var code = w.GetVisualDescendants().OfType<CodeEditor>().Single();
        var editor = code.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();

        // Line 5 is "total += i": clicking its margin sets the breakpoint,
        // same as a user placing it before pressing F5.
        ClickLine(code.BreakpointMargin, editor, 5, w);
        Assert.Contains(5, code.BreakpointMargin.Lines);

        var factory = FactoryOf(w);
        var contents = ContentsOf(w);

        // F5: Debug > Start.
        InvokeMenu(w, L(StringKeys.MenuDebug), L(StringKeys.MenuDebugStart));

        var variables = Assert.IsType<VariablesPanel>(contents[factory.Variables]);

        await PumpUntilAsync(
            () => variables.Nodes.Any(n => n.Value.Name == "total"),
            attempts: 600, delayMs: 100);

        var totalBeforeStep = variables.Nodes.Single(n => n.Value.Name == "total").Value.Value;
        Assert.Equal("0", totalBeforeStep);
        Assert.Contains(variables.Nodes, n => n.Value.Name == "i" && n.Value.Value == "1");

        // Watch: adding an expression shows its value while stopped.
        var watch = Assert.IsType<WatchPanel>(contents[factory.Watch]);
        watch.Add("i");

        await PumpUntilAsync(
            () => watch.Entries.Any(e => e.Expression == "i" && e.Value == "1"),
            attempts: 200, delayMs: 50);

        Assert.Contains(watch.Entries, e => e.Expression == "i" && e.Value == "1");

        // F10: step over the addition; the loop variable itself is
        // unaffected, which only steps once rather than many times would
        // leave it.
        InvokeMenu(w, L(StringKeys.MenuDebug), L(StringKeys.MenuDebugStepOver));

        await PumpUntilAsync(
            () => variables.Nodes.Any(n => n.Value.Name == "total" && n.Value.Value == "1"),
            attempts: 200, delayMs: 50);

        Assert.Equal("1", variables.Nodes.Single(n => n.Value.Name == "total").Value.Value);

        // Shift+F5: Debug > Stop, which must also drop the debug layout back
        // to the design one.
        InvokeMenu(w, L(StringKeys.MenuDebug), L(StringKeys.MenuDebugStop));

        await PumpUntilAsync(() => !vm.IsRunning, attempts: 100, delayMs: 50);
        await host.SettleAsync(5);

        Assert.False(vm.IsRunning);
        var bottom = factory.BottomArea!.VisibleDockables!;
        Assert.DoesNotContain(factory.Variables, bottom);
        Assert.DoesNotContain(factory.Watch, bottom);
        Assert.DoesNotContain(factory.CallStack, bottom);
    }

    // ------------------------------------------------------------------
    // Journey 4: the Terminal panel. Typing a command and pressing Enter
    // runs it through a real shell, and closing the window afterwards does
    // not hang on the terminal's own child process.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task TypingIntoTheTerminalRunsItThroughARealShell()
    {
        using var host = new TestWindow();
        await host.SettleAsync(10);
        var w = host.Window;

        // View > New Terminal, the same action a user opening one would take.
        InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.MenuViewNewTerminal));
        await host.SettleAsync(10);

        var contents = ContentsOf(w);
        var terminal = contents.Values.OfType<TerminalView>().First();

        terminal.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Text = "echo basalt-journey"
        });
        terminal.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter
        });

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!terminal.ScreenText.Contains("basalt-journey", StringComparison.Ordinal)
               && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Contains("basalt-journey", terminal.ScreenText);

        // Closing the window must not hang on the terminal's live process.
        var closed = false;
        w.Closed += (_, _) => closed = true;
        host.Dispose();
        Assert.True(closed, "the window did not close.");
    }
}
