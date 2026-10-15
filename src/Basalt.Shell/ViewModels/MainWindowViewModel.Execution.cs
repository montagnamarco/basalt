using Basalt.Core.Localization;
using Basalt.Core.Model;
using Basalt.Workspace;
using Basalt.Workspace.Projects;
using CommunityToolkit.Mvvm.Input;

namespace Basalt.Shell.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string? _builtAssemblyPath;

    // The last build that succeeded, so a designer opened after it still gets the project's output.
    private (string Project, string Assembly, long BuildId)? _lastSuccessfulBuild;

    // Numbers the successful builds. A designer applies each id once, so the same build replayed to it is
    // not loaded twice, while a later build is loaded even when its output file has not changed.
    private long _successfulBuildCount;
    private event Action<string, string, long>? ProjectBuilt;

    /// <summary>
    /// Calls <paramref name="handler"/> with the project, the output assembly and the build id of each successful
    /// build, and at once with the last one that succeeded before the subscription, under the id it had then. A
    /// failed build calls nobody, so a designer keeps the controls of the build it already has.
    /// </summary>
    public IDisposable SubscribeToSuccessfulBuilds(Action<string, string, long> handler)
    {
        ProjectBuilt += handler;
        if (_lastSuccessfulBuild is { } last) handler(last.Project, last.Assembly, last.BuildId);
        return new Unsubscription(() => ProjectBuilt -= handler);
    }

    /// <summary>Raises a successful build as the build step does, for the designer's tests that replay or repeat one.</summary>
    internal void RaiseProjectBuiltForTests(string project, string assembly, long buildId) =>
        ProjectBuilt?.Invoke(project, assembly, buildId);

    private sealed class Unsubscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }

    /// <summary>
    /// Build Solution: the solution file when a solution is open, so each of its projects is built; a project
    /// opened alone is built as itself.
    /// </summary>
    [RelayCommand]
    private async Task BuildAsync()
    {
        await BuildProjectAsync(wholeSolution: true).ConfigureAwait(true);
    }

    /// <summary>Build of the startup project alone, whatever solution is open. The project entries of the menu use it.</summary>
    public async Task BuildStartupProjectAsync()
    {
        await BuildProjectAsync().ConfigureAwait(true);
    }

    /// <summary>Clean and then build of the startup project alone.</summary>
    public async Task RebuildStartupProjectAsync()
    {
        if (!await CleanStartupProjectAsync().ConfigureAwait(true)) return;
        await BuildStartupProjectAsync().ConfigureAwait(true);
    }

    internal Task<bool> BuildProjectForTestsAsync() => BuildProjectAsync();

    /// <summary>How many builds were started, for the tests that check a rebuild builds once.</summary>
    internal int BuildStartsForTests { get; private set; }

    private async Task<bool> BuildProjectAsync(bool wholeSolution = false)
    {
        var projectPath = StartupProject;
        var configuration = Configuration;

        // "dotnet build" takes a solution file as well as a project, so a solution build names the .sln. Without a
        // startup project the solution is still built, and then no project's output is taken for the run.
        var solutionTarget = wholeSolution && SolutionPath is { } solutionFile && SolutionFile.IsSolutionFile(solutionFile)
            ? solutionFile
            : null;
        var target = solutionTarget ?? projectPath;
        if (target is null)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusNoProject);
            return false;
        }

        // One build waiting is enough: a second click while the first still
        // waits behind another build would only queue the same work twice.
        if (_buildQueued)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusBuildAlreadyWaiting, BuildGate.Holder ?? Localizer.Get(StringKeys.GateBuild));
            return false;
        }

        BeginBusy();

        // Tracked so the status bar can offer to stop it: a build that has
        // gone wrong is one the user wants to end, not wait out. Started
        // before the wait, so Stop also ends a build that is still queued.
        var operation = Operations.Start(Localizer.Get(StringKeys.StatusBuilding));
        IDisposable? gate = null;

        try
        {
            // Both write the same output folder, so a build the Immediate
            // window (or a test run, or another build) has under way is
            // waited for, and the status says for whom.
            try
            {
                gate = await BuildGate.AcquireAsync(
                    Localizer.Get(StringKeys.GateBuild),
                    holder =>
                    {
                        _buildQueued = true;
                        StatusMessage = Localizer.Get(StringKeys.StatusWaitingForBuild, holder);
                    },
                    operation.Token).ConfigureAwait(true);
            }
            finally
            {
                _buildQueued = false;
            }

            _builtAssemblyPath = null;
            BuildOutput.Clear();
            Diagnostics.Clear();
            StatusMessage = Localizer.Get(StringKeys.StatusBuilding);

            // The compiler reads the disk, not the editors. Without this a
            // control drawn in the designer and never saved was simply not in
            // the build: the window came up empty and nothing said why, which
            // looked like the designer having lost the work.
            await SaveAllAsync().ConfigureAwait(true);

            // The build starts by clearing the output, so the explanation
            // given when the solution was opened is repeated here, where the
            // SDK's own NETSDK1100 is about to appear.
            if ((await Compatibility.GetAsync(target, operation.Token).ConfigureAwait(true)).SourcesMissing)
                AppendOutput(MissingWindowsDesktopSourcesMessage);

            StatusMessage = Localizer.Get(StringKeys.StatusBuilding);
            BuildStartsForTests++;
            var result = await _buildService
                .BuildAsync(target, configuration, operation.Token)
                .ConfigureAwait(true);

            _builtAssemblyPath = result.Succeeded ? result.OutputAssemblyPath : null;

            // A solution build prints the output of every project, and the one reported last is not necessarily the
            // startup project's. Only an assembly in the startup project's own folder is taken as its output.
            if (projectPath is null
                || (target != projectPath && _builtAssemblyPath is { } built && !IsInFolderOf(built, projectPath)))
                _builtAssemblyPath = null;

            // The designer reloads the project's own controls from this output (see
            // WinFormsDesignerView.DesignContext). Only a success is raised: a failed build keeps them.
            if (projectPath is not null && _builtAssemblyPath is { } builtAssembly)
            {
                var buildId = ++_successfulBuildCount;
                _lastSuccessfulBuild = (projectPath, builtAssembly, buildId);
                ProjectBuilt?.Invoke(projectPath, builtAssembly, buildId);
            }

            foreach (var diagnostic in result.Diagnostics) Diagnostics.Add(diagnostic);

            var errors = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            var warnings = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

            StatusMessage = result.Succeeded
                ? Localizer.Get(StringKeys.StatusBuildSucceeded, result.Duration.TotalSeconds, warnings)
                : Localizer.Get(StringKeys.StatusBuildFailed, errors, warnings);

            // Whether the startup project or configuration changed while this
            // build was running is decided here, so a caller that only needs
            // "built and still the target" can rely on this return value.
            // RunBuiltProjectAsync asks once more because it can be reached
            // without a build of its own.
            return result.Succeeded && (projectPath is null || LaunchTargetIsCurrent(projectPath, configuration));
        }
        catch (OperationCanceledException)
        {
            // Asked for, so it is reported as stopped rather than as a
            // failure the user has to look into.
            StatusMessage = Localizer.Get(StringKeys.StatusBuildStopped);
            return false;
        }
        catch (Exception ex)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusBuildInterrupted, ex.Message);
            return false;
        }
        finally
        {
            Operations.Finish(operation);
            gate?.Dispose();
            EndBusy();
        }
    }

    /// <summary>
    /// Builds the startup project and reports the assembly to debug.
    ///
    /// Returns null when the build failed or produced nothing to run, so the
    /// caller does not start a debugger against a stale assembly.
    /// </summary>
    public async Task<string?> BuildForDebuggingAsync()
    {
        if (StartupProject is null)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusNoProject);
            return null;
        }

        // BuildProjectAsync already checked the launch target is still the
        // one this call asked to build before returning true: checking again
        // here would be the second of three checks for one race.
        if (!await BuildProjectAsync().ConfigureAwait(true)) return null;

        return _builtAssemblyPath is { } assembly && File.Exists(assembly) ? assembly : null;
    }

    /// <summary>
    /// Which configuration is built and run.
    ///
    /// The toolbar offers Debug and Release and stores the choice; nothing
    /// read it, so Release built Debug and then looked for the result in the
    /// Debug folder — right by accident, and wrong the moment anything asked
    /// for the other one.
    /// </summary>
    public string Configuration { get; set; } = "Debug";

    /// <summary>The folder the debugged program should run in.</summary>
    public string? StartupDirectory => StartupProject is null
        ? null
        : Path.GetDirectoryName(Path.GetFullPath(StartupProject));

    [RelayCommand]
    private async Task RunAsync()
    {
        var projectPath = StartupProject;
        var configuration = Configuration;
        if (StartupProject is null)
        {
            // Without a project the file being edited is the program, when a
            // language compiles it (QuickBASIC); MSBuild is not asked for it.
            if (SourceFileToRun() is { } sourceFile) await RunSourceFileAsync(sourceFile).ConfigureAwait(true);
            else StatusMessage = Localizer.Get(StringKeys.StatusNoProject);
            return;
        }

        // Asked before building, not after: a build of a program that is
        // still running fails on Windows (the output is locked) and clears
        // the Problems list, only for the answer to be "already running".
        if (ReportIfAlreadyRunning() || ReportIfDebugging()) return;

        // Built before running: launching the build's own output, rather
        // than asking "dotnet run" to build again, means a build that failed
        // — or one whose target changed mid-build — never gets a process
        // started from a rebuild nobody saw the errors of.
        if (!await BuildProjectAsync().ConfigureAwait(true)) return;

        await RunBuiltProjectAsync(projectPath, configuration).ConfigureAwait(true);
    }

    /// <summary>
    /// Says so and returns true when the application is already running.
    ///
    /// The single "already running" check, made before anything builds.
    /// Silence used to read as nothing having happened at all: F5 or the
    /// Play button appeared to do nothing instead of saying why. A run that
    /// slips in during a build is still refused: ProcessRunService.Start
    /// throws, and the failure is reported.
    /// </summary>
    internal bool ReportIfAlreadyRunning()
    {
        // A console program in its terminal tab is a running program too.
        if (!_runService.IsRunning && !ConsoleRunIsRunning) return false;

        StatusMessage = Localizer.Get(StringKeys.StatusAlreadyRunning);
        return true;
    }

    /// <summary>
    /// A debug session counts as the program running: building over the
    /// debuggee's output fails on locked files, or swaps the program under it.
    /// </summary>
    internal bool ReportIfDebugging()
    {
        if (IsDebuggingProbe?.Invoke() != true) return false;

        StatusMessage = Localizer.Get(StringKeys.StatusAlreadyRunning);
        return true;
    }

    internal async Task RunBuiltProjectAsync(string? expectedProject = null, string? expectedConfiguration = null)
    {
        if (StartupProject is null) return;

        // Read first, off the interface thread, and before anything below is
        // looked at: everything after this await is one uninterrupted block
        // on the interface thread, so the launch target checked is the one
        // that starts. A failed reading is reported here and retried by the
        // next run.
        IReadOnlyList<string> compatibilityArguments;
        try
        {
            compatibilityArguments = await Compatibility.GetArgumentsAsync(StartupProject).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusStartFailed, ex.Message);
            AppendOutput(StatusMessage);
            return;
        }

        // A console program runs in a terminal tab, where the person can type to it. Read here, off the interface
        // thread, as the arguments above are, and only when there is a window to open the tab in.
        var runInTerminal = RunInTerminal;
        var startup = StartupProject;
        var console = runInTerminal is not null
            && await Task.Run(() => ConsoleProjects.IsConsole(startup)).ConfigureAwait(true);

        if (!LaunchTargetIsCurrent(expectedProject ?? StartupProject, expectedConfiguration ?? Configuration)) return;

        // Checked again after the build, not only before it: a run asked for while this build waited on the gate
        // passed the check in RunAsync, and must not start a second program next to the first. Nothing awaits between
        // this check and the start below, so no other run can start in the gap.
        if (ReportIfAlreadyRunning()) return;

        if (_builtAssemblyPath is not { } assembly || !File.Exists(assembly))
        {
            // The caller always builds first; this only happens when that
            // build produced no assembly at all, which it already reported.
            StatusMessage = Localizer.Get(StringKeys.StatusStartFailed, Localizer.Get(StringKeys.StatusNoBuildOutput));
            return;
        }

        if (console && runInTerminal is not null)
        {
            // The generation is taken as the redirected run takes its own below, so an exit from an earlier run is
            // recognised the same way whichever kind of run it was.
            await RunConsoleProjectAsync(runInTerminal, startup!, expectedConfiguration ?? Configuration,
                compatibilityArguments, ++_runGeneration).ConfigureAwait(true);
            return;
        }

        // Tagged with the generation that starts it, and attached before the
        // process starts: Process.Exited arrives on a threadpool thread and a
        // process that dies immediately could otherwise exit unheard. It also
        // arrives well after Stop asks the process to end, by when another
        // run may be the one anybody cares about.
        var generation = ++_runGeneration;
        var exited = MakeExitedHandler(generation);
        _runService.Exited += exited;

        try
        {
            // "dotnet run --no-build": the build above is what runs, and
            // launchSettings.json (environment, arguments, URLs) still
            // applies, which launching the dll directly would drop.
            // Settings an older IDE left in Properties/ move to where dotnet run reads them.
            if (StartupProject is { } launchTarget) LaunchSettingsLocation.MigrateLegacy(launchTarget);
            _runService.Start(StartupProject, configuration: expectedConfiguration ?? Configuration,
                compatibilityArguments);

            IsRunning = true;
            StatusMessage = Localizer.Get(StringKeys.StatusRunning);
        }
        catch (Exception ex)
        {
            _runService.Exited -= exited;
            StatusMessage = Localizer.Get(StringKeys.StatusStartFailed, ex.Message);
            AppendOutput(StatusMessage);
        }
    }

    /// <summary>
    /// Runs the build's own output directly when no debug adapter is
    /// installed, instead of starting a debug session that has nothing to
    /// attach to.
    ///
    /// Returns true when it ran this way, so the one caller — F5 with no
    /// adapter on the machine — only has to update its own debug-state UI
    /// rather than repeat the adapter check itself.
    /// </summary>
    internal async Task<bool> RunWithoutDebuggerIfUnavailableAsync(string startupProject, string configuration)
    {
        if (Basalt.Workspace.Debugging.DebugAdapterLocator.Find() is not null) return false;

        WriteOutput(Localizer.Get(StringKeys.DebugRunWithoutAdapter));
        await RunBuiltProjectAsync(startupProject, configuration).ConfigureAwait(true);
        return true;
    }

    /// <summary>
    /// Bumps the run generation and wires its exit handler exactly as
    /// <see cref="RunBuiltProjectAsync"/> does once the process starts, but
    /// without launching one. Exit-race tests simulate several generations
    /// this way instead of racing real process timing.
    /// </summary>
    internal int StartRunGenerationForTests()
    {
        var generation = ++_runGeneration;
        _runService.Exited += MakeExitedHandler(generation);
        IsRunning = true;
        StatusMessage = Localizer.Get(StringKeys.StatusRunning);
        return generation;
    }

    private EventHandler<int> MakeExitedHandler(int generation)
    {
        EventHandler<int>? handler = null;
        handler = (_, code) =>
        {
            _runService.Exited -= handler;
            OnRunExited(code, generation);
        };
        return handler;
    }

    internal bool LaunchTargetIsCurrent(string projectPath, string configuration)
    {
        if (StartupProject == projectPath && Configuration == configuration) return true;
        StatusMessage = Localizer.Get(StringKeys.StatusLaunchTargetChanged);
        return false;
    }

    [RelayCommand]
    private void StopRun()
    {
        // The generation being stopped, so its exit — which arrives later,
        // asynchronously — is recognised as expected rather than reported as
        // a crash.
        _stoppedRunGeneration = _runGeneration;
        _runService.Stop();
        StopConsoleRun();
        StopSourceFileRun();
        IsRunning = false;
        StatusMessage = Localizer.Get(StringKeys.StatusStopped);
    }

    /// <summary>The process the last build started, for the tests that check the arguments it was given.</summary>
    internal System.Diagnostics.ProcessStartInfo? LastBuildStartInfo => _buildService.LastStartInfo;

    private static bool IsInFolderOf(string file, string project) =>
        Path.GetFullPath(file).StartsWith(
            Path.GetDirectoryName(Path.GetFullPath(project)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>The startup project's name for a menu label, or the word "Project" when none is chosen.</summary>
    internal string StartupProjectNameForMenu =>
        (StartupProject is null ? null : Path.GetFileNameWithoutExtension(StartupProject))
        ?? Localizer.Get(StringKeys.MenuBuildProjectFallback);

    /// <summary>
    /// Brings what depends on the solution's projects up to date once a project's properties are saved.
    /// </summary>
    /// <remarks>
    /// The language service reads the project again, so a changed RootNamespace, Option Strict or Imports
    /// reaches the diagnostics of the open files without reopening the solution; those files are then checked
    /// again. The projects offered to run are read again too, so a project made an executable can be started.
    /// Before this, the Problems panel kept the answers of the old properties until the solution was reopened.
    /// </remarks>
    public async Task ProjectPropertiesSavedAsync()
    {
        if (SolutionPath is not { Length: > 0 } solution) return;

        await ReloadLanguageServiceAsync().ConfigureAwait(true);
        await RefreshRunnableProjectsAsync(solution).ConfigureAwait(true);
        await RefreshDiagnosticsOfAsync(OpenDocuments.ToList()).ConfigureAwait(true);
    }

    /// <summary>
    /// Reads the runnable projects and the startup project again, as opening the solution did.
    /// </summary>
    /// <remarks>
    /// The startup project the user chose stays while it still runs. Otherwise the one the solution starts is
    /// chosen, which is how a solution with a single executable behaves when it is opened.
    /// </remarks>
    private async Task RefreshRunnableProjectsAsync(string solution)
    {
        var catalog = await Task.Run(() => ProjectCatalog.Read(solution)).ConfigureAwait(true);

        RunnableProjects = catalog.Runnable;
        OnPropertyChanged(nameof(RunnableProjects));

        if (StartupProject is null || !RunnableProjects.Contains(StartupProject, StringComparer.Ordinal))
            StartupProject = catalog.Startup;

        OnPropertyChanged(nameof(StartupProject));
    }
}
