using Avalonia.Headless.XUnit;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// What happens when a process, a build or a debugger reports from its own
/// thread.
/// </summary>
/// <remarks>
/// Every one of those arrives on a thread the interface does not own, and
/// everything listening is a control. Setting a property from there raises
/// PropertyChanged on that thread, the handler reads DataContext, and Avalonia
/// throws — which took the whole IDE down every time a run ended.
/// </remarks>
public class BackgroundThreadSafetyTests
{
    [AvaloniaFact]
    public async Task RaisesTheChangeOnTheInterfaceThread()
    {
        // The real invariant. Asserting that nothing throws proves nothing
        // here: the headless platform does not enforce thread affinity, so a
        // control read from the wrong thread is quietly allowed and the test
        // passes against the very bug it is meant to catch. Which thread the
        // handler runs on is the thing that actually differs.
        var vm = new MainWindowViewModel();

        var onInterfaceThread = false;
        var raised = false;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MainWindowViewModel.IsRunning)) return;

            onInterfaceThread = Avalonia.Threading.Dispatcher.UIThread.CheckAccess();
            raised = true;
        };

        vm.SetRunningForTests(true);
        raised = false;

        // Raised the way Process.Exited raises it: from the pool, not from
        // the interface.
        await Task.Run(() => vm.ReportRunExitedForTests(0));

        for (var i = 0; i < 50 && !raised; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(raised, "the change was never reported");
        Assert.True(onInterfaceThread,
            "IsRunning changed on a worker thread, where every handler is a control");
    }

    [AvaloniaFact]
    public async Task StillReportsThatTheRunEnded()
    {
        // Moving the work to the interface thread must not lose it: the
        // status has to come back to "not running" or Stop stays lit forever.
        var vm = new MainWindowViewModel();

        vm.SetRunningForTests(true);

        await Task.Run(() => vm.ReportRunExitedForTests(3));

        for (var i = 0; i < 20 && vm.IsRunning; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.False(vm.IsRunning);
    }
}
