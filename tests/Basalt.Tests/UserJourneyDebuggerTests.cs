using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Debugging;
using Dock.Model.Core;

namespace Basalt.Tests;

/// <summary>
/// Debugging a small Visual Basic console program the way a person would,
/// through a real <see cref="MainWindow"/>: breakpoints set in the margin or
/// with F9, F5 to start, F10 and F11 to step, the Breakpoints, Call Stack,
/// Variables, Watch and Output panels read the way a user reads them, and the
/// editor and panels returning to rest once the program ends.
///
/// These run the real netcoredbg adapter. Each test is skipped when the
/// adapter is not installed, as the other debugger tests are.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class UserJourneyDebuggerTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-journey-debugger-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-journey-debugger-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public UserJourneyDebuggerTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    // ---- the program under debug ---------------------------------------
    //
    // Line numbers the tests rely on:
    //   6  total += numbers(i)   the loop body, the breakpoint of most journeys
    //   7  Report(total)         the call that F10 steps over and F11 steps into
    //   9  Console.WriteLine     after the loop, where the program is stopped at the end
    //  12-14                    Report, the called Sub; Step Into stops on its first line

    private const int LoopBodyLine = 6;
    private const int CallLine = 7;
    private const int AfterLoopLine = 9;
    private const int ReportFirstLine = 12;
    private const int ReportLastLine = 14;

    private const string LoopProgram = """
        Module Program
            Sub Main()
                Dim total As Integer = 0
                Dim numbers() As Integer = {2, 4, 6}
                For i As Integer = 0 To 2
                    total += numbers(i)
                    Report(total)
                Next
                Console.WriteLine("basalt-loop finished " & total)
            End Sub

            Sub Report(value As Integer)
                Console.WriteLine("basalt-loop running total " & value)
            End Sub
        End Module
        """;

    private string ProgramPath => Path.Combine(_root, "Program.vb");

    private string WriteProject()
    {
        var projectPath = Path.Combine(_root, "App.vbproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
                <DebugType>portable</DebugType>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(ProgramPath, LoopProgram);
        return projectPath;
    }

    /// <summary>A window with the program open in its editor, ready to be debugged.</summary>
    private async Task<TestWindow> OpenProgramAsync()
    {
        var host = new TestWindow(WriteProject());
        await host.SettleAsync(10);

        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(8);

        return host;
    }

    // ---- shared helpers ------------------------------------------------

    private static string L(string key) => Localizer.Get(key);

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

    private static void PressKey(MainWindow window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }

    /// <summary>
    /// Pumps the interface thread while a condition holds off. The debugger
    /// reports on a background thread and the session posts its events back
    /// to the dispatcher, so both the dispatcher and the layout must run.
    /// </summary>
    private static async Task<bool> PumpUntilAsync(TestWindow host, Func<bool> done, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < timeout)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            if (done()) return true;
            await host.SettleAsync(1);
        }

        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        return done();
    }

    private static IdeDockFactory FactoryOf(MainWindow window) =>
        (IdeDockFactory)typeof(MainWindow)
            .GetField("_factory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static Dictionary<IDockable, Control> ContentsOf(MainWindow window) =>
        (Dictionary<IDockable, Control>)typeof(MainWindow)
            .GetField("_contents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static T PanelOf<T>(MainWindow window, Func<IdeDockFactory, IDockable> tool) where T : Control =>
        Assert.IsType<T>(ContentsOf(window)[tool(FactoryOf(window))]);

    private static AvaloniaEdit.TextEditor EditorOf(MainWindow window, string filePath)
    {
        var code = window.CodeEditorForTests(filePath);
        Assert.NotNull(code);
        return code!.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();
    }

    /// <summary>
    /// Clicks the margin at the vertical middle of a line: a real pointer
    /// press on the margin, not a direct call into its toggle.
    /// </summary>
    private static void ClickMargin(MainWindow window, string filePath, int line)
    {
        var code = window.CodeEditorForTests(filePath);
        Assert.NotNull(code);

        var editor = EditorOf(window, filePath);
        var view = editor.TextArea.TextView;
        var visualLine = view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(line));

        var inView = new Point(0, visualLine.VisualTop - view.VerticalOffset + (visualLine.Height / 2));

        var margin = code!.BreakpointMargin;
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

    /// <summary>What the program has written so far, for a failure message.</summary>
    private static string OutputOf(MainWindowViewModel vm) => string.Join("\n", vm.BuildOutput);

    /// <summary>
    /// Waits until execution is stopped on a line, with the margin highlighting
    /// it and the Call Stack listing at least one frame.
    /// </summary>
    private async Task PauseAtAsync(TestWindow host, int line)
    {
        var w = host.Window;
        var callStack = PanelOf<CallStackPanel>(w, f => f.CallStack);

        var stopped = await PumpUntilAsync(host,
            () => w.CodeEditorForTests(ProgramPath)?.BreakpointMargin.CurrentLine == line
                && callStack.Frames.Count > 0,
            TimeSpan.FromSeconds(90));

        Assert.True(stopped,
            $"The program did not stop at line {line}.\n{OutputOf((MainWindowViewModel)w.DataContext!)}");
    }

    /// <summary>
    /// What the window says about the session right now, for a failure message:
    /// the margin's highlighted line, the frames listed, the banner and the
    /// session's own flags.
    /// </summary>
    private static string StateOf(MainWindow w, string filePath)
    {
        var session = typeof(MainWindow)
            .GetField("_debug", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(w) as DebugSession;
        var callStack = PanelOf<CallStackPanel>(w, f => f.CallStack);
        var banner = w.FindControl<Border>("DebugBanner");
        var line = w.CodeEditorForTests(filePath)?.BreakpointMargin.CurrentLine;
        var frames = string.Join(" | ", callStack.Frames.Select(f => $"{f.Method}:{f.Line}"));

        return $"margin line {line}; frames [{frames}]; banner visible {banner?.IsVisible}; "
            + $"session running {session?.IsRunning}, paused {session?.IsPaused}";
    }

    /// <summary>Types an expression into the Watch box and submits it with Enter.</summary>
    private static void AddWatch(WatchPanel watch, string expression)
    {
        watch.InputForTests.Text = expression;
        watch.InputForTests.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter
        });
    }

    // ==================================================================
    // Journey 1. F9 on a loop line, F5 stops there. The Breakpoints panel
    // lists the breakpoint, the Call Stack shows Main, and the Variables
    // panel shows the local with its value and its type.
    // ==================================================================
    [AvaloniaFact]
    public async Task F9OnALoopLineThenF5StopsThereWithTheBreakpointAndTheLocalsShown()
    {
        Assert.SkipWhen(DebugAdapterLocator.Find() is null, "netcoredbg is not installed.");

        using var host = await OpenProgramAsync();
        var w = host.Window;

        try
        {
            var editor = EditorOf(w, ProgramPath);
            editor.CaretOffset = editor.Document.GetLineByNumber(LoopBodyLine).Offset;
            editor.TextArea.Focus();

            PressKey(w, Key.F9, RawInputModifiers.None, PhysicalKey.F9);

            Assert.Contains(LoopBodyLine, w.CodeEditorForTests(ProgramPath)!.BreakpointMargin.Lines);

            InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolBreakpoints));
            var breakpoints = PanelOf<BreakpointsPanel>(w, f => f.Breakpoints);

            Assert.True(await PumpUntilAsync(host,
                () => breakpoints.Breakpoints.Any(b => b.Line == LoopBodyLine && b.Enabled),
                TimeSpan.FromSeconds(5)));

            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);
            await PauseAtAsync(host, LoopBodyLine);

            // The Breakpoints panel shows the row the user set, as file and line.
            Assert.True(await PumpUntilAsync(host,
                () => breakpoints.GetVisualDescendants().OfType<TextBlock>()
                    .Any(t => t.Text == "Program.vb:6"),
                TimeSpan.FromSeconds(5)),
                "The Breakpoints panel does not list Program.vb:6.");

            // Stopped in Main, before the loop body has run.
            var callStack = PanelOf<CallStackPanel>(w, f => f.CallStack);
            Assert.Single(callStack.Frames);
            Assert.Contains("Main", callStack.Frames[0].Method, StringComparison.Ordinal);

            var variables = PanelOf<VariablesPanel>(w, f => f.Variables);
            var total = variables.Nodes.Single(n => n.Value.Name == "total");

            Assert.Equal("0", total.Value.Value);
            Assert.Contains("int", total.Value.Type, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("numbers", variables.Nodes.Select(n => n.Value.Name));
        }
        finally
        {
            await w.StopDebuggingForTests();
        }
    }

    // ==================================================================
    // Journey 1b. Watches written in Visual Basic. netcoredbg evaluates C#,
    // so "total Mod 4" or "numbers(1)" all came back "not available" until
    // the IDE translated them. Stopped on the second pass, total is 6.
    // ==================================================================
    [AvaloniaFact]
    public async Task WatchesWrittenInVisualBasicShowTheirValues()
    {
        Assert.SkipWhen(DebugAdapterLocator.Find() is null, "netcoredbg is not installed.");

        using var host = await OpenProgramAsync();
        var w = host.Window;

        try
        {
            ClickMargin(w, ProgramPath, CallLine);
            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);
            await PauseAtAsync(host, CallLine);

            InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolWatch));
            var watch = PanelOf<WatchPanel>(w, f => f.Watch);

            string[] expressions =
            [
                "total Mod 4", "total <> 2", "numbers(1) * 10", "Not (total = 2)",
                "total > 1 AndAlso total < 10", "\"t=\" & total", "CStr(total)",
            ];

            foreach (var expression in expressions) AddWatch(watch, expression);

            Assert.True(await PumpUntilAsync(host,
                () => watch.Entries.Count == expressions.Length && watch.Entries.All(e => e.Value is not null || e.Failed),
                TimeSpan.FromSeconds(30)),
                "The watches were not evaluated.");

            string ValueOf(string expression) => watch.Entries.Single(e => e.Expression == expression).Value ?? "<not available>";

            Assert.Equal("2", ValueOf("total Mod 4"));
            Assert.Equal("false", ValueOf("total <> 2"));
            Assert.Equal("40", ValueOf("numbers(1) * 10"));
            Assert.Equal("false", ValueOf("Not (total = 2)"));
            Assert.Equal("true", ValueOf("total > 1 AndAlso total < 10"));
            Assert.Equal("\"t=2\"", ValueOf("\"t=\" & total"));
            Assert.Equal("\"2\"", ValueOf("CStr(total)"));
        }
        finally
        {
            await w.StopDebuggingForTests();
        }
    }

    // ==================================================================
    // Journey 1c. An unhandled exception three calls deep. The banner and the
    // Output say what was thrown, and the Variables panel and the watches
    // read the frame it was thrown in, and its callers once picked in the
    // Call Stack. netcoredbg answers for the frame above the one asked for
    // at such a stop, so the panel showed only $exception and "n" was not
    // available.
    // ==================================================================
    private const string ThrowingProgram = """
        Module Program
            Sub Main()
                Dim marker As Integer = 99
                Console.WriteLine("before " & marker)
                Middle(3)
            End Sub

            Sub Middle(depth As Integer)
                Dim middleLocal As String = "mid"
                Fail(depth)
            End Sub

            Sub Fail(n As Integer)
                Dim items() As Integer = {1, 2}
                Console.WriteLine(items(n))
            End Sub
        End Module
        """;

    [AvaloniaFact]
    public async Task AnUnhandledExceptionStopsWithItsMessageAndTheLocalsOfEachFrame()
    {
        Assert.SkipWhen(DebugAdapterLocator.Find() is null, "netcoredbg is not installed.");

        var project = WriteProject();
        File.WriteAllText(ProgramPath, ThrowingProgram);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(8);

        try
        {
            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);
            await PauseAtAsync(host, 15);

            const string thrown = "System.IndexOutOfRangeException: Index was outside the bounds of the array.";

            var banner = w.FindControl<TextBlock>("DebugBannerDetail")!;
            Assert.True(await PumpUntilAsync(host, () => banner.Text?.Contains(thrown, StringComparison.Ordinal) == true,
                TimeSpan.FromSeconds(10)), $"The banner does not say what was thrown: '{banner.Text}'.");
            Assert.Contains(vm.BuildOutput, line => line.Contains(thrown, StringComparison.Ordinal));

            InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolVariables));
            var variables = PanelOf<VariablesPanel>(w, f => f.Variables);
            Assert.True(await PumpUntilAsync(host, () => variables.Nodes.Any(n => n.Value.Name == "n"), TimeSpan.FromSeconds(10)),
                "Fail's locals are not shown: " + string.Join(", ", variables.Nodes.Select(n => n.Value.Name)));
            Assert.Equal("3", variables.Nodes.Single(n => n.Value.Name == "n").Value.Value);

            InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolWatch));
            var watch = PanelOf<WatchPanel>(w, f => f.Watch);
            AddWatch(watch, "n * 2");
            Assert.True(await PumpUntilAsync(host, () => watch.Entries.Single().Value == "6", TimeSpan.FromSeconds(10)),
                "The watch was not read in the frame that threw: " + watch.Entries.Single().Display);

            // Main, the last frame, read through the Call Stack.
            var callStack = PanelOf<CallStackPanel>(w, f => f.CallStack);
            callStack.SelectForTests(callStack.Frames.Count - 1);
            Assert.True(await PumpUntilAsync(host, () => variables.Nodes.Any(n => n.Value.Name == "marker"), TimeSpan.FromSeconds(10)),
                "Main's locals are not shown: " + string.Join(", ", variables.Nodes.Select(n => n.Value.Name)));
            Assert.Equal("99", variables.Nodes.Single(n => n.Value.Name == "marker").Value.Value);
        }
        finally
        {
            await w.StopDebuggingForTests();
        }
    }

    // ==================================================================
    // Journey 2. A breakpoint set by clicking the margin. F10 steps over the
    // call and the Watch value moves with it; F11 steps into the Sub and the
    // Call Stack lists the Sub above Main.
    // ==================================================================
    [AvaloniaFact]
    public async Task F10AndF11StepThroughTheLoopWhileTheWatchFollowsTheValueAndTheCallStackShowsTheSub()
    {
        Assert.SkipWhen(DebugAdapterLocator.Find() is null, "netcoredbg is not installed.");

        using var host = await OpenProgramAsync();
        var w = host.Window;

        try
        {
            ClickMargin(w, ProgramPath, LoopBodyLine);
            Assert.Contains(LoopBodyLine, w.CodeEditorForTests(ProgramPath)!.BreakpointMargin.Lines);

            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);
            await PauseAtAsync(host, LoopBodyLine);

            InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolWatch));
            var watch = PanelOf<WatchPanel>(w, f => f.Watch);

            AddWatch(watch, "total");

            Assert.True(await PumpUntilAsync(host,
                () => watch.Entries.Count == 1 && watch.Entries[0].Value == "0",
                TimeSpan.FromSeconds(10)));
            Assert.Equal("total = 0", watch.Entries[0].Display);

            // F10: the call on line 7 runs and the total moves to 2.
            PressKey(w, Key.F10, RawInputModifiers.None, PhysicalKey.F10);

            Assert.True(await PumpUntilAsync(host,
                () => w.CodeEditorForTests(ProgramPath)!.BreakpointMargin.CurrentLine == CallLine
                    && watch.Entries[0].Value == "2",
                TimeSpan.FromSeconds(30)),
                $"F10 did not reach line {CallLine} with total = 2.");
            Assert.Equal("total = 2", watch.Entries[0].Display);

            // F11: the call is entered, so the Sub is the innermost frame. The
            // stop lands on the Sub's declaration, which is inside it.
            var callStack = PanelOf<CallStackPanel>(w, f => f.CallStack);
            var code = w.CodeEditorForTests(ProgramPath)!;
            PressKey(w, Key.F11, RawInputModifiers.None, PhysicalKey.F11);

            Assert.True(await PumpUntilAsync(host,
                () => callStack.Frames.Count >= 2
                    && callStack.Frames[0].Method.Contains("Report", StringComparison.Ordinal)
                    && code.BreakpointMargin.CurrentLine is >= ReportFirstLine and <= ReportLastLine,
                TimeSpan.FromSeconds(30)),
                $"F11 did not enter Report. {StateOf(w, ProgramPath)}");

            Assert.Equal(callStack.Frames[0].Line, code.BreakpointMargin.CurrentLine);
            Assert.Contains("Main", callStack.Frames[1].Method, StringComparison.Ordinal);
        }
        finally
        {
            await w.StopDebuggingForTests();
        }
    }

    // ==================================================================
    // Journey 3. An array in the Variables panel can be opened to show its
    // elements.
    // ==================================================================

    /// <remarks>
    /// Expansion was broken in two places: <c>MainWindow.ToNode</c> asked for
    /// the children of reference 0, because <c>VariableValue</c> did not carry
    /// the adapter's reference, and nothing called
    /// <c>VariableNode.LoadChildrenAsync</c> when a node opened. Each defect,
    /// reintroduced once on its own, made this test fail.
    /// </remarks>
    [AvaloniaFact]
    public async Task AnArrayInTheVariablesPanelOpensToShowItsElements()
    {
        Assert.SkipWhen(DebugAdapterLocator.Find() is null, "netcoredbg is not installed.");

        using var host = await OpenProgramAsync();
        var w = host.Window;

        try
        {
            ClickMargin(w, ProgramPath, LoopBodyLine);

            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);
            await PauseAtAsync(host, LoopBodyLine);

            InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolVariables));
            var variables = PanelOf<VariablesPanel>(w, f => f.Variables);
            var numbers = variables.Nodes.Single(n => n.Value.Name == "numbers");

            Assert.True(numbers.HasChildren);

            // The tab is realised on a later layout pass, so the tree is awaited.
            TreeView? tree = null;
            Assert.True(await PumpUntilAsync(host,
                () => (tree = variables.GetVisualDescendants().OfType<TreeView>().SingleOrDefault()) is not null,
                TimeSpan.FromSeconds(10)),
                "The Variables panel shows no tree of values.");

            var item = Assert.IsType<TreeViewItem>(tree!.ContainerFromItem(numbers));

            item.IsExpanded = true;

            Assert.True(await PumpUntilAsync(host,
                () => item.GetVisualDescendants().OfType<TextBlock>()
                    .Any(t => t.Text?.StartsWith("[0]", StringComparison.Ordinal) == true),
                TimeSpan.FromSeconds(10)),
                "The array did not open to show its first element.");
        }
        finally
        {
            await w.StopDebuggingForTests();
        }
    }

    // ==================================================================
    // Journey 4. A breakpoint switched off in the Breakpoints panel, then
    // Continue: the program runs its loop to the end without stopping, and
    // the Output panel shows what the program wrote to the console.
    // ==================================================================
    [AvaloniaFact]
    public async Task ADisabledBreakpointIsIgnoredOnContinueAndTheOutputPanelShowsTheProgramsConsole()
    {
        Assert.SkipWhen(DebugAdapterLocator.Find() is null, "netcoredbg is not installed.");

        using var host = await OpenProgramAsync();
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        try
        {
            ClickMargin(w, ProgramPath, LoopBodyLine);

            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);
            await PauseAtAsync(host, LoopBodyLine);

            InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolBreakpoints));
            var breakpoints = PanelOf<BreakpointsPanel>(w, f => f.Breakpoints);

            Assert.True(await PumpUntilAsync(host,
                () => breakpoints.GetVisualDescendants().OfType<CheckBox>().Any(),
                TimeSpan.FromSeconds(5)));

            // The switch on the row, as a user clicks it.
            var row = breakpoints.GetVisualDescendants().OfType<CheckBox>().Single();
            row.IsChecked = false;

            Assert.True(await PumpUntilAsync(host,
                () => breakpoints.Breakpoints.Single(b => b.Line == LoopBodyLine).Enabled == false,
                TimeSpan.FromSeconds(5)));

            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);

            Assert.True(await PumpUntilAsync(host,
                () => vm.BuildOutput.Any(line => line.Contains("basalt-loop finished 12", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(90)),
                "The program did not run to the end.\n" + OutputOf(vm));

            // Each pass of the loop printed its running total, so the program
            // ran straight through the line it was stopped on before.
            Assert.Contains(vm.BuildOutput, line => line.Contains("basalt-loop running total 2", StringComparison.Ordinal));
            Assert.Contains(vm.BuildOutput, line => line.Contains("basalt-loop running total 6", StringComparison.Ordinal));
            Assert.Contains(vm.BuildOutput, line => line.Contains("basalt-loop running total 12", StringComparison.Ordinal));

            InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolOutput));
            var output = PanelOf<OutputPanel>(w, f => f.Output);
            Assert.True(await PumpUntilAsync(host,
                () => output.GetVisualDescendants().OfType<SelectableTextBlock>()
                    .Any(t => t.Text?.Contains("basalt-loop finished 12", StringComparison.Ordinal) == true),
                TimeSpan.FromSeconds(5)),
                "The Output panel does not show the program's console output.");
        }
        finally
        {
            await w.StopDebuggingForTests();
        }
    }

    // ==================================================================
    // Journey 5. A program that ends while stopped leaves no trace in the
    // panels: the Call Stack, Variables and Watch values clear and the editor
    // drops its line highlight.
    // ==================================================================
    [AvaloniaFact]
    public async Task WhenTheProgramEndsTheDebugPanelsClearAndTheEditorIsNotHighlighted()
    {
        Assert.SkipWhen(DebugAdapterLocator.Find() is null, "netcoredbg is not installed.");

        using var host = await OpenProgramAsync();
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        try
        {
            ClickMargin(w, ProgramPath, AfterLoopLine);

            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);
            await PauseAtAsync(host, AfterLoopLine);

            InvokeMenu(w, L(StringKeys.MenuView), L(StringKeys.ToolWatch));
            var watch = PanelOf<WatchPanel>(w, f => f.Watch);
            AddWatch(watch, "total");

            Assert.True(await PumpUntilAsync(host,
                () => watch.Entries.Count == 1 && watch.Entries[0].Value == "12",
                TimeSpan.FromSeconds(10)));

            var callStack = PanelOf<CallStackPanel>(w, f => f.CallStack);
            var variables = PanelOf<VariablesPanel>(w, f => f.Variables);
            Assert.NotEmpty(callStack.Frames);
            Assert.NotEmpty(variables.Nodes);

            // Continue, and the program runs out and reports its exit code.
            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);

            Assert.True(await PumpUntilAsync(host,
                () => vm.BuildOutput.Any(line => line.Contains("exited with code", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(60)),
                "The program did not report its exit.\n" + OutputOf(vm));

            Assert.Contains(vm.BuildOutput, line => line.Contains("basalt-loop finished 12", StringComparison.Ordinal));

            Assert.IsType<EmptyState>(callStack.Content);
            Assert.Empty(callStack.Frames);
            Assert.Empty(variables.Nodes);
            Assert.Null(watch.Entries[0].Value);
            Assert.Equal("total = …", watch.Entries[0].Display);

            var code = w.CodeEditorForTests(ProgramPath)!;
            Assert.Equal(0, code.BreakpointMargin.CurrentLine);
            Assert.False(EditorOf(w, ProgramPath).IsReadOnly);
        }
        finally
        {
            await w.StopDebuggingForTests();
        }
    }

    /// <remarks>
    /// The session used to count as running for as long as the adapter process
    /// lived, and netcoredbg outlives the program it debugs: the banner kept
    /// saying "Running" and a second F5 did nothing until Stop. The service now
    /// records the program's "exited" event. Reintroduced once (IsRunning judged
    /// by the adapter alone), this test failed.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheDebugBannerGoesAwayWhenTheProgramEnds()
    {
        Assert.SkipWhen(DebugAdapterLocator.Find() is null, "netcoredbg is not installed.");

        using var host = await OpenProgramAsync();
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        try
        {
            ClickMargin(w, ProgramPath, AfterLoopLine);

            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);
            await PauseAtAsync(host, AfterLoopLine);

            var banner = w.FindControl<Border>("DebugBanner")!;
            Assert.True(banner.IsVisible, "The debug banner is not shown while stopped.");

            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);

            Assert.True(await PumpUntilAsync(host,
                () => vm.BuildOutput.Any(line => line.Contains("exited with code", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(60)),
                "The program did not report its exit.\n" + OutputOf(vm));

            // The program has ended, so the banner must stop claiming it runs.
            Assert.True(await PumpUntilAsync(host, () => !banner.IsVisible, TimeSpan.FromSeconds(10)),
                "The banner still shows a running session after the program exited. " + StateOf(w, ProgramPath));
        }
        finally
        {
            await w.StopDebuggingForTests();
        }
    }
}
