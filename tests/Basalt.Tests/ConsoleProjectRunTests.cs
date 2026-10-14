using Avalonia.Headless.XUnit;
using Basalt.Core.Localization;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Which run a project gets. A console program starts in a terminal tab, through the window's delegate; anything
/// else, or a run with no window to open a tab in, keeps the redirected run. The terminal itself is faked here, so
/// these need no pseudo-terminal and run on every platform. The journey through a real tab is in
/// UserJourneyRunInTerminalTests.
/// </summary>
public sealed class ConsoleProjectRunTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-console-run-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string L(string key, params object[] args) => Localizer.Get(key, args);

    /// <summary>A project on the given output type that builds and does nothing.</summary>
    private string WriteProject(string outputType = "Exe", string name = "App", string extraProperties = "")
    {
        var path = Path.Combine(_root, name + ".vbproj");
        File.WriteAllText(path, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>{outputType}</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
                {extraProperties}
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Program.vb"),
            "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");
        return path;
    }

    /// <summary>A terminal run the test ends by hand, in place of a program in a real tab.</summary>
    private sealed class FakeRun(int processId = 4242) : IProgramRun
    {
        public int ProcessId { get; } = processId;

        public bool IsRunning { get; private set; } = true;

        public int StopCalls { get; private set; }

        public event EventHandler<int>? Exited;

        public event EventHandler? Closed;

        public void Stop() => StopCalls++;

        /// <summary>The program ends with this code, as a real one does.</summary>
        public void Exit(int code)
        {
            IsRunning = false;
            Exited?.Invoke(this, code);
        }

        /// <summary>The tab was closed, which ends the program.</summary>
        public void CloseTab()
        {
            IsRunning = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Records the calls the view model makes to open a terminal tab, and hands back one run.</summary>
    private sealed class RecordingWindow
    {
        public List<(string Title, string Program, IReadOnlyList<string> Arguments, string Directory)> Calls { get; } = [];

        public FakeRun Run { get; init; } = new();

        public Task<IProgramRun> OpenTabAsync(
            string title,
            string program,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string>? environment)
        {
            Calls.Add((title, program, arguments, workingDirectory));
            return Task.FromResult<IProgramRun>(Run);
        }
    }

    [AvaloniaFact]
    public async Task AConsoleProgramRunsInATerminalTabUnderItsProjectsName()
    {
        using var viewModel = new MainWindowViewModel();
        await viewModel.OpenSolutionAsync(WriteProject());
        var window = new RecordingWindow();
        viewModel.RunInTerminal = window.OpenTabAsync;

        await viewModel.RunCommand.ExecuteAsync(null);

        // The tab is titled with the project, and the program is started the way the redirected run starts it.
        var call = Assert.Single(window.Calls);
        Assert.Equal("App", call.Title);
        Assert.Equal("dotnet", call.Program);
        Assert.Equal(Path.GetFullPath(_root), Path.GetFullPath(call.Directory));
        Assert.Equal("run", call.Arguments[0]);
        var arguments = call.Arguments.ToList();
        Assert.Equal(Path.Combine(_root, "App.vbproj"), arguments[arguments.IndexOf("--project") + 1]);
        Assert.Contains("--no-build", call.Arguments);

        // The run is tracked as the redirected one is, and the redirected path did not start a second process.
        Assert.True(viewModel.IsRunning);
        Assert.Empty(viewModel.LastLaunchArgumentsForTests);
        Assert.Equal(L(StringKeys.StatusRunning), viewModel.StatusMessage);
    }

    [AvaloniaFact]
    public async Task TheProgramsExitEndsTheRunWithItsExitCode()
    {
        using var viewModel = new MainWindowViewModel();
        await viewModel.OpenSolutionAsync(WriteProject());
        var window = new RecordingWindow();
        viewModel.RunInTerminal = window.OpenTabAsync;
        await viewModel.RunCommand.ExecuteAsync(null);

        window.Run.Exit(0);

        Assert.False(viewModel.IsRunning);
        Assert.Equal(L(StringKeys.StatusRunExited, 0), viewModel.StatusMessage);
    }

    [AvaloniaFact]
    public async Task StopKillsTheTerminalProgramAndItsLaterExitIsNotReportedAsACrash()
    {
        using var viewModel = new MainWindowViewModel();
        await viewModel.OpenSolutionAsync(WriteProject());
        var window = new RecordingWindow();
        viewModel.RunInTerminal = window.OpenTabAsync;
        await viewModel.RunCommand.ExecuteAsync(null);

        viewModel.StopRunCommand.Execute(null);

        Assert.Equal(1, window.Run.StopCalls);
        Assert.False(viewModel.IsRunning);
        Assert.Equal(L(StringKeys.StatusStopped), viewModel.StatusMessage);

        // The kill is reported late, as a process's exit is: the status must keep saying it was stopped.
        window.Run.Exit(137);

        Assert.Equal(L(StringKeys.StatusStopped), viewModel.StatusMessage);
    }

    [AvaloniaFact]
    public async Task ARunAskedForWhileTheTabsProgramRunsSaysSoBeforeBuilding()
    {
        using var viewModel = new MainWindowViewModel();
        await viewModel.OpenSolutionAsync(WriteProject());
        var window = new RecordingWindow();
        viewModel.RunInTerminal = window.OpenTabAsync;
        await viewModel.RunCommand.ExecuteAsync(null);

        viewModel.Diagnostics.Add(new Basalt.Core.Model.IdeDiagnostic(
            "X1", "kept", Basalt.Core.Model.DiagnosticSeverity.Warning, "Program.vb", 1, 1));
        await viewModel.RunCommand.ExecuteAsync(null);

        // Refused before the build, which would otherwise clear the diagnostics and start a second program.
        Assert.Single(window.Calls);
        Assert.Contains(viewModel.Diagnostics, d => d.Message == "kept");
        Assert.Equal(L(StringKeys.StatusAlreadyRunning), viewModel.StatusMessage);

        viewModel.StopRunCommand.Execute(null);
    }

    [AvaloniaFact]
    public async Task ClosingTheTabWhileTheProgramRunsEndsTheRun()
    {
        using var viewModel = new MainWindowViewModel();
        await viewModel.OpenSolutionAsync(WriteProject());
        var window = new RecordingWindow();
        viewModel.RunInTerminal = window.OpenTabAsync;
        await viewModel.RunCommand.ExecuteAsync(null);

        window.Run.CloseTab();

        Assert.False(viewModel.IsRunning);
        Assert.Equal(L(StringKeys.StatusStopped), viewModel.StatusMessage);
    }

    [AvaloniaFact]
    public async Task AProgramThatCouldNotStartInItsTabSaysSoAndIsNotRunning()
    {
        using var viewModel = new MainWindowViewModel();
        await viewModel.OpenSolutionAsync(WriteProject());
        var window = new RecordingWindow { Run = new FakeRun(processId: -1) };
        viewModel.RunInTerminal = window.OpenTabAsync;

        await viewModel.RunCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsRunning);
        Assert.Equal(L(StringKeys.StatusRunInTerminalFailed), viewModel.StatusMessage);
    }

    [AvaloniaFact]
    public async Task WithoutAWindowAConsoleProgramKeepsTheRedirectedRun()
    {
        // The tests and any host with no tabs: the run it always had, with its arguments recorded as before.
        using var viewModel = new MainWindowViewModel();
        await viewModel.OpenSolutionAsync(WriteProject());
        Assert.Null(viewModel.RunInTerminal);

        await viewModel.RunCommand.ExecuteAsync(null);

        var arguments = viewModel.LastLaunchArgumentsForTests.ToList();
        Assert.Equal("run", arguments[0]);
        Assert.Contains("--no-build", arguments);

        viewModel.StopRunCommand.Execute(null);
    }

    [AvaloniaFact]
    public async Task AWindowsExecutableKeepsTheRedirectedRunEvenWithATerminalAvailable()
    {
        // A windowed program has no console to type into, so its output stays in the Output panel.
        using var viewModel = new MainWindowViewModel();
        await viewModel.OpenSolutionAsync(WriteProject(outputType: "WinExe"));
        var window = new RecordingWindow();
        viewModel.RunInTerminal = window.OpenTabAsync;

        await viewModel.RunCommand.ExecuteAsync(null);

        Assert.Empty(window.Calls);
        Assert.NotEmpty(viewModel.LastLaunchArgumentsForTests);

        viewModel.StopRunCommand.Execute(null);
    }

    [AvaloniaFact]
    public async Task ARunAskedForWhileAnEarlierBuildIsStillGoingDoesNotOpenASecondTab()
    {
        using var viewModel = new MainWindowViewModel();
        await viewModel.OpenSolutionAsync(WriteProject());
        var window = new RecordingWindow();
        viewModel.RunInTerminal = window.OpenTabAsync;

        // The first run is in its tab. A second run (Run or F5) that passed its own check before a build, and whose
        // build finishes now, reaches the launch step while the first is still going. It must not open a second tab.
        await viewModel.RunCommand.ExecuteAsync(null);
        Assert.Single(window.Calls);

        await viewModel.RunBuiltProjectAsync();

        Assert.Single(window.Calls);
        Assert.Equal(L(StringKeys.StatusAlreadyRunning), viewModel.StatusMessage);

        viewModel.StopRunCommand.Execute(null);
    }
}
