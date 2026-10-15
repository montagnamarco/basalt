using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;
using Dock.Model.Core;

namespace Basalt.Tests;

/// <summary>
/// More debugging journeys through a real <see cref="MainWindow"/>, with the real netcoredbg: stopping on a thrown
/// exception, the launch profile's arguments and environment under F5 and Ctrl+F5, attaching to a running process,
/// and debugging a test. Each is skipped when the adapter is not installed. The parts are in the files named
/// UserJourneyDebuggerTests2*.cs.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed partial class UserJourneyDebuggerTests2 : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-journey-debugger2-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-journey-debugger2-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings = Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    private readonly List<Process> _started = [];

    public UserJourneyDebuggerTests2() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        foreach (var process in _started)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.Dispose();
        }

        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    private string ProgramPath => Path.Combine(_root, "Program.vb");

    private static string L(string key) => Localizer.Get(key);

    /// <summary>A window with the program (a console project of the given source) open in its editor.</summary>
    private async Task<TestWindow> OpenProgramAsync(string source, string? launchSettings = null)
    {
        var project = Path.Combine(_root, "App.vbproj");
        File.WriteAllText(project, NetCoreDebugTargets.ExeProject);
        File.WriteAllText(ProgramPath, source);

        if (launchSettings is not null)
        {
            Directory.CreateDirectory(Path.Combine(_root, "My Project"));
            File.WriteAllText(Path.Combine(_root, "My Project", "launchSettings.json"), launchSettings);
        }

        var host = new TestWindow(project);
        await host.SettleAsync(10);

        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await vm.OpenFileAsync(ProgramPath);
        await host.SettleAsync(8);

        return host;
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

    private static void PressKey(MainWindow window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }

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

    private static T Field<T>(MainWindow window, string name) where T : class =>
        (T)typeof(MainWindow)
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static Dictionary<IDockable, Control> ContentsOf(MainWindow window) =>
        Field<Dictionary<IDockable, Control>>(window, "_contents");

    private static T PanelOf<T>(MainWindow window, Func<IdeDockFactory, IDockable> tool) where T : Control =>
        Assert.IsType<T>(ContentsOf(window)[tool(Field<IdeDockFactory>(window, "_factory"))]);

    private static DebugSession SessionOf(MainWindow window) => Field<DebugSession>(window, "_debug");

    private static AvaloniaEdit.TextEditor EditorOf(MainWindow window, string filePath) =>
        window.CodeEditorForTests(filePath)!.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();

    /// <summary>F9 on a line: a breakpoint set the way a person sets one.</summary>
    private static void SetBreakpointWithF9(MainWindow window, string filePath, int line)
    {
        var editor = EditorOf(window, filePath);
        editor.CaretOffset = editor.Document.GetLineByNumber(line).Offset;
        editor.TextArea.Focus();

        PressKey(window, Key.F9, RawInputModifiers.None, PhysicalKey.F9);

        Assert.Contains(line, window.CodeEditorForTests(filePath)!.BreakpointMargin.Lines);
    }

    /// <summary>What the program wrote to its console, which a console program debugged or run shows in a Terminal tab.</summary>
    private static string ConsoleTextOf(MainWindow window) =>
        string.Join("\n", ContentsOf(window).Values.OfType<TerminalView>().Select(t => t.ScreenText));

    private static string OutputOf(MainWindow window) =>
        string.Join("\n", ((MainWindowViewModel)window.DataContext!).BuildOutput);

    private static string StateOf(MainWindow w, string filePath)
    {
        var session = SessionOf(w);
        var callStack = PanelOf<CallStackPanel>(w, f => f.CallStack);
        var banner = w.FindControl<Border>("DebugBanner");
        var line = w.CodeEditorForTests(filePath)?.BreakpointMargin.CurrentLine;

        return $"margin line {line}; frames {callStack.Frames.Count}; banner visible {banner?.IsVisible}; "
            + $"session running {session.IsRunning}, paused {session.IsPaused}\n{OutputOf(w)}\n{ConsoleTextOf(w)}";
    }

    private async Task PauseAtAsync(TestWindow host, int line, string? filePath = null)
    {
        var w = host.Window;
        var path = filePath ?? ProgramPath;
        var callStack = PanelOf<CallStackPanel>(w, f => f.CallStack);

        var stopped = await PumpUntilAsync(host,
            () => w.CodeEditorForTests(path)?.BreakpointMargin.CurrentLine == line && callStack.Frames.Count > 0,
            TimeSpan.FromSeconds(90));

        Assert.True(stopped, $"The program did not stop at line {line}: {StateOf(w, path)}");
    }

    private static bool AdapterMissing => Basalt.Workspace.Debugging.DebugAdapterLocator.Find() is null;
}
