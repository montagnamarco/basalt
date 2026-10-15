using Avalonia.Threading;
using Basalt.Core.Localization;
using Basalt.Core.Services;
using Basalt.Workspace.Testing;

namespace Basalt.Shell;

/// <summary>
/// Debug Test in the Test Explorer: the test runs with its host held, and the debugger attaches to the host.
/// </summary>
/// <remarks>
/// The runner is told to wait (VSTEST_HOST_DEBUG=1, in the environment of that run alone, through
/// <see cref="DotnetTestService.DebugAsync"/>) and prints the process it waits in. Waiting for the debugger and never
/// attaching hung the run for good, which is what this used to do. Here the process id is read from the output as
/// it is printed and the session attaches to it; breakpoints set in the test then stop it. If the attach fails the run
/// is cancelled, which ends the host it started.
/// </remarks>
public partial class MainWindow
{
    /// <summary>Debugs the selected test.</summary>
    private async Task DebugSelectedTestAsync()
    {
        if (_tests is null) return;

        var chosen = _tests.SelectedTests;

        if (chosen.Count != 1)
        {
            ViewModel.WriteOutput("[test] " + Localizer.Get(StringKeys.TestSelectSingleToDebug));
            return;
        }

        // One session at a time: the test host would be attached to over the program already being debugged.
        if (_debug.IsRunning || _testRunInProgress)
        {
            ViewModel.WriteOutput($"[debug] {Localizer.Get(StringKeys.DebugAlreadyDebugging)}");
            return;
        }

        // Same as Run Tests: debug what is on disk, not a stale save.
        await ViewModel.SaveAllAsync();

        var test = chosen[0];

        ViewModel.WriteOutput($"[test] {Localizer.Get(StringKeys.TestDebugging, test.FullyQualifiedName)}");

        _tests.MarkRunning(chosen);
        _testRunInProgress = true;
        _toolbar?.RefreshAvailability();

        using var run = new CancellationTokenSource();
        _debugTestRun = run;
        IReadOnlyList<TestResult> results = [];

        try
        {
            results = await _testService
                .DebugAsync(
                    test.ProjectPath,
                    DotnetTestService.FilterFor([test.FullyQualifiedName]),
                    // On a thread of the pool, while the host waits: the attach is made on the interface thread.
                    processId => Dispatcher.UIThread.Post(() => Guarded.Run(
                        () => AttachToTestHostAsync(processId, run), ViewModel.WriteOutput, "test")),
                    run.Token)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            ViewModel.WriteOutput($"[test] {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            // The attach failed or the user stopped the run: the rows leave their "running" state below.
        }
        finally
        {
            _testRunInProgress = false;
            _debugTestRun = null;
        }

        _tests.ShowResults(results);
        _toolbar?.RefreshAvailability();
    }

    /// <summary>Debugs the test selected in the Test Explorer, as its Debug button does. For tests.</summary>
    internal Task DebugSelectedTestForTests() => DebugSelectedTestAsync();

    /// <summary>The debug run of a test under way, ended by Stop Debugging: the held test host is killed with it.</summary>
    private CancellationTokenSource? _debugTestRun;

    /// <summary>
    /// Ends the test run that is being debugged, if there is one. Called when debugging is stopped: the test host would
    /// otherwise finish the test on its own, and a retry of the run would hold a second host with nobody to attach.
    /// </summary>
    private void CancelDebuggedTestRun()
    {
        try
        {
            _debugTestRun?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run ended in the meantime.
        }
    }

    /// <summary>Attaches the session to the test host that is waiting, or ends the run if it cannot.</summary>
    private async Task AttachToTestHostAsync(int processId, CancellationTokenSource run)
    {
        // Stopped before the host was heard of: there is nothing to attach to, and no second attach to make.
        if (run.IsCancellationRequested) return;

        ShowTool(_factory.Output);

        _debugTarget = Localizer.Get(StringKeys.DebugTheProgram);
        ShowStartingBanner();

        // Not detached on Stop: a test whose debugging is stopped is a test run that ends.
        await _debug.AttachAsync(processId, programPath: null, detachOnStop: false);

        // Stop pressed while the attach was under way: it ended the run, so the session just attached goes too.
        if (run.IsCancellationRequested && _debug.IsRunning) await _debug.StopAsync();

        if (!_debug.IsRunning && !run.IsCancellationRequested)
        {
            // The host would wait for ever for a debugger that is not coming.
            ViewModel.WriteOutput("[test] " + Localizer.Get(StringKeys.TestDebugAttachFailed, processId));
            await run.CancelAsync();
        }

        ShowDebugState();
    }
}
