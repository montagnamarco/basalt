using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Workspace.Debugging;

namespace Basalt.Tests;

public sealed partial class UserJourneyDebuggerTests2
{
    [AvaloniaFact]
    public async Task TheAttachDialogListsProcessesWithNameIdAndTitleAndReturnsTheChosenOne()
    {
        DotnetProcessInfo[] processes = [new(4242, "dotnet", "dotnet /app/Web.dll"), new(77, "Worker", "")];
        var dialog = new AttachToProcessDialog(() => Task.FromResult<IReadOnlyList<DotnetProcessInfo>>(processes));

        await dialog.RefreshAsync();

        Assert.Equal([4242, 77], dialog.Choices.Select(p => p.ProcessId));
        Assert.Equal("", dialog.StatusTextForTests);

        dialog.SelectForTests(4242);
        dialog.AcceptForTests();

        Assert.Equal(4242, dialog.Result!.ProcessId);
    }

    [AvaloniaFact]
    public async Task TheAttachDialogSaysSoWhenNothingIsRunning()
    {
        var dialog = new AttachToProcessDialog(() => Task.FromResult<IReadOnlyList<DotnetProcessInfo>>([]));

        await dialog.RefreshAsync();

        Assert.Equal(L(StringKeys.AttachProcessNone), dialog.StatusTextForTests);
        dialog.AcceptForTests();
        Assert.Null(dialog.Result);
    }

    [AvaloniaFact]
    public async Task AttachingToARunningProgramHitsABreakpointShowsTheCallStackAndStopDetaches()
    {
        Assert.SkipWhen(AdapterMissing, "netcoredbg is not installed.");

        var (_, source, assembly) = await NetCoreDebugTargets.BuildAsync(
            Path.Combine(_root, "looper"), "Looper", NetCoreDebugTargets.Looper);
        var heartbeat = Path.Combine(_root, "heartbeat.txt");

        var program = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { assembly, heartbeat },
            UseShellExecute = false,
            RedirectStandardOutput = true
        })!;
        _started.Add(program);

        using var host = new TestWindow(Path.Combine(_root, "looper", "Looper.vbproj"));
        await host.SettleAsync(10);
        var w = host.Window;
        await ((Basalt.Shell.ViewModels.MainWindowViewModel)w.DataContext!).OpenFileAsync(source);
        await host.SettleAsync(8);

        Assert.True(await PumpUntilAsync(host, () => File.Exists(heartbeat), TimeSpan.FromSeconds(30)));

        try
        {
            SetBreakpointWithF9(w, source, NetCoreDebugTargets.LooperBreakpointLine);

            await w.AttachToProcessAsync(program.Id, "dotnet");
            await PauseAtAsync(host, NetCoreDebugTargets.LooperBreakpointLine, source);

            var callStack = PanelOf<CallStackPanel>(w, f => f.CallStack);
            Assert.Contains("Main", callStack.Frames[0].Method, StringComparison.Ordinal);
            Assert.True(SessionOf(w).DetachesOnStop);

            await w.StopDebuggingForTests();

            Assert.Contains(Localizer.Get(StringKeys.DebugDetached, program.Id), OutputOf(w), StringComparison.Ordinal);

            var count = File.ReadAllLines(heartbeat).Length;
            await Task.Delay(1500);

            Assert.False(program.HasExited);
            Assert.True(File.ReadAllLines(heartbeat).Length > count, "The program stopped ticking after Stop.");
        }
        finally
        {
            await w.StopDebuggingForTests();
        }
    }
}
