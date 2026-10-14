using Basalt.Core.Localization;
using Basalt.Workspace;

namespace Basalt.Shell.ViewModels;

/// <summary>
/// Running a console program in a terminal tab, where the person can type to it.
/// </summary>
/// <remarks>
/// The run path in <c>MainWindowViewModel.Execution.cs</c> decides which run a project gets. This file starts the
/// terminal run and keeps it: IsRunning, Stop and the status messages follow it as they follow the redirected run.
/// </remarks>
public sealed partial class MainWindowViewModel
{
    /// <summary>The program's launcher. Run Without Debugging starts a project through the .NET CLI.</summary>
    private const string DotnetProgram = "dotnet";

    /// <summary>
    /// Opens a terminal tab for a console program. Set by the window, which owns the tabs.
    /// </summary>
    /// <remarks>
    /// Null where there is no window, as in the tests. A console program then runs as every other program does,
    /// with its output in the Output panel, so nothing depends on a window being there.
    /// </remarks>
    public RunInTerminalHandler? RunInTerminal { get; set; }

    /// <summary>The program running in a terminal tab, when the last run was a console program.</summary>
    private IProgramRun? _consoleRun;

    /// <summary>True while a program started in a terminal tab has not ended.</summary>
    private bool ConsoleRunIsRunning => _consoleRun is { IsRunning: true };

    /// <summary>
    /// Starts the built console program in a new terminal tab titled with the project's name.
    /// </summary>
    /// <remarks>
    /// The arguments are those of the redirected run, so both kinds of run start the same program the same way.
    /// The tab stays open after the program ends, and the exit code arrives through the same handler as the
    /// redirected run's, so the status reads the same.
    /// </remarks>
    private async Task RunConsoleProjectAsync(
        RunInTerminalHandler runInTerminal,
        string projectPath,
        string configuration,
        IReadOnlyList<string> compatibilityArguments,
        int generation)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Directory.GetCurrentDirectory();
        var title = Path.GetFileNameWithoutExtension(projectPath);
        var arguments = RunArguments(projectPath, configuration, compatibilityArguments);

        IProgramRun run;
        try
        {
            run = await runInTerminal(title, DotnetProgram, arguments, directory, LaunchEnvironment())
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusStartFailed, ex.Message);
            AppendOutput(StatusMessage);
            return;
        }

        // The tab says why the program did not start; the next run asks for a new tab.
        if (run.ProcessId < 0)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusRunInTerminalFailed);
            AppendOutput(StatusMessage);
            return;
        }

        // Subscribed before anything else is set: the exit of a program that fails at once is raised as soon as
        // it is seen, and the run must hear of it.
        _consoleRun = run;
        run.Exited += (_, code) => OnRunExited(code, generation);
        run.Closed += (_, _) => OnConsoleTabClosed(generation);

        IsRunning = true;
        StatusMessage = Localizer.Get(StringKeys.StatusRunning);
    }

    /// <summary>
    /// The tab was closed. Closing it ends a program still running in it, so the run is reported stopped, as Stop
    /// would have reported it.
    /// </summary>
    private void OnConsoleTabClosed(int generation) => OnUiThread(() =>
    {
        // A run that already ended said how it ended, and a newer run owns the status: neither is overwritten.
        if (generation != _runGeneration || !IsRunning) return;

        _stoppedRunGeneration = generation;
        IsRunning = false;
        StatusMessage = Localizer.Get(StringKeys.StatusStopped);
    });

    /// <summary>Ends a console program running in a terminal tab. The tab stays, showing how it ended.</summary>
    private void StopConsoleRun() => _consoleRun?.Stop();

    /// <summary>
    /// The arguments that run a built project: "dotnet run" against the build's own output, with the same
    /// properties the build used. Used by the redirected run and the terminal run alike.
    /// </summary>
    internal static IReadOnlyList<string> RunArguments(
        string projectPath,
        string configuration,
        IReadOnlyList<string> compatibilityArguments) =>
        ["run", "--project", projectPath, "-c", configuration, "--no-build", .. compatibilityArguments];

    /// <summary>
    /// The language the .NET CLI reports in, as the redirected run sets it. A language the user chose stays.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? LaunchEnvironment() =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DotnetProcess.LanguageVariable))
            ? new Dictionary<string, string> { [DotnetProcess.LanguageVariable] = Localizer.InterfaceLanguage }
            : null;
}
