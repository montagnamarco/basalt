using System.Reflection;
using Avalonia.Headless.XUnit;
using Basalt.Core.Services;
using Basalt.Shell;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The main window's title, which must read like Visual Basic 6's and
/// Visual Studio's: "Basalt" alone, the solution's name with "[design]"
/// once one is open, "[run]" while the program runs and "[break]" while
/// paused in the debugger.
/// </summary>
public sealed class WindowTitleTests
{
    [Fact]
    public void IsJustTheApplicationNameWithNothingOpen()
    {
        Assert.Equal("Basalt", WindowTitle.For(null, isRunning: false, isPaused: false));
        Assert.Equal("Basalt", WindowTitle.For("", isRunning: false, isPaused: false));
    }

    [Fact]
    public void NamesTheSolutionAndSaysDesignWhenNothingIsRunning()
    {
        Assert.Equal("Calculator - Basalt [design]",
            WindowTitle.For("/home/user/Calculator.sln", isRunning: false, isPaused: false));
    }

    [Fact]
    public void SaysRunWhileTheProgramRuns()
    {
        Assert.Equal("Calculator - Basalt [run]",
            WindowTitle.For("/home/user/Calculator.sln", isRunning: true, isPaused: false));
    }

    [Fact]
    public void SaysBreakWhilePausedInTheDebugger()
    {
        Assert.Equal("Calculator - Basalt [break]",
            WindowTitle.For("/home/user/Calculator.sln", isRunning: true, isPaused: true));
    }

    /// <summary>
    /// Drives the real window end to end: opening a project, starting a
    /// fake debug session, pausing it, resuming it and stopping it, checking
    /// the window's own <see cref="MainWindow.Title"/> at each step.
    /// </summary>
    [AvaloniaFact]
    public async Task TheRealWindowFollowsTheSolutionAndTheDebugger()
    {
        var root = Directory.CreateTempSubdirectory("basalt-title-").FullName;
        var project = Path.Combine(root, "Audit.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <MyType>Empty</MyType>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Program.vb"),
            "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        using var host = new TestWindow();
        await host.SettleAsync(2);
        var w = host.Window;

        Assert.Equal("Basalt", w.Title);

        var vm = (MainWindowViewModel)w.DataContext!;
        await vm.OpenSolutionAsync(project);
        await host.SettleAsync(2);

        Assert.Equal("Audit - Basalt [design]", w.Title);

        var debug = DebugSessionOf(w);
        var fake = new FakeDebugService();
        await debug.StartWithAsync(fake, "fake.dll", null);
        await host.SettleAsync(2);

        Assert.Equal("Audit - Basalt [run]", w.Title);

        fake.RaisePaused();
        await host.SettleAsync(2);

        Assert.Equal("Audit - Basalt [break]", w.Title);

        fake.RaiseResumed();
        await host.SettleAsync(2);

        Assert.Equal("Audit - Basalt [run]", w.Title);

        await debug.StopAsync();
        await host.SettleAsync(2);

        Assert.Equal("Audit - Basalt [design]", w.Title);
    }

    private static DebugSession DebugSessionOf(MainWindow window) =>
        (DebugSession)typeof(MainWindow)
            .GetField("_debug", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;

    /// <summary>
    /// A minimal stand-in for netcoredbg, controlled by the test rather than
    /// by a real debuggee: only the title's own four states matter here.
    /// </summary>
    private sealed class FakeDebugService : IDebugSessionService
    {
        public bool IsRunning { get; private set; } = true;
        public bool IsPaused { get; private set; }

        public event EventHandler<StackFrame>? Paused;
        public event EventHandler? Resumed;
        public event EventHandler<int>? Exited;
        public event EventHandler<string>? OutputReceived;
        public event EventHandler? Started;
        public event EventHandler<Exception>? Faulted;

        public void RaisePaused()
        {
            IsPaused = true;
            Paused?.Invoke(this, new StackFrame("Main", null, 1));
        }

        public void RaiseResumed()
        {
            IsPaused = false;
            Resumed?.Invoke(this, EventArgs.Empty);
        }

        public Task LaunchAsync(string assemblyPath, string? workingDirectory = null, CancellationToken ct = default)
        {
            Started?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task AttachAsync(int processId, CancellationToken ct = default) => Task.CompletedTask;

        public Task SetBreakpointsAsync(
            string filePath, IReadOnlyList<Breakpoint> breakpoints, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ContinueAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StepOverAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StepIntoAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StepOutAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<StackFrame>> GetCallStackAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StackFrame>>([]);

        public Task<IReadOnlyList<VariableValue>> GetLocalsAsync(
            int frameIndex, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VariableValue>>([]);

        public Task<string?> EvaluateAsync(string expression, int frameIndex, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<VariableValue>> ExpandAsync(int reference, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VariableValue>>([]);

        public Task StopAsync(CancellationToken ct = default)
        {
            IsRunning = false;
            Exited?.Invoke(this, 0);
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }
}
