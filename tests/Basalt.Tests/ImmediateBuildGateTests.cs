using Basalt.Core.Services;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Basalt.Workspace.Immediate;
using Basalt.Workspace.Testing;

namespace Basalt.Tests;

/// <summary>
/// The gate every build of the startup project passes through, so that the
/// toolbar, a run, a publish, tests and the Immediate window never run two
/// dotnet builds over one obj folder, and the busy flag that goes with it.
/// </summary>
public sealed class ImmediateBuildGateTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "basalt-gate-tests", Guid.NewGuid().ToString("N"));

    public ImmediateBuildGateTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private MainWindowViewModel ViewModelWithProject()
    {
        var vm = new MainWindowViewModel();
        vm.StartupProject = Path.Combine(_folder, "Missing.vbproj");

        return vm;
    }

    private static async Task<T> WaitForAsync<T>(Func<T?> read) where T : class
    {
        var until = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < until)
        {
            if (read() is { } found) return found;
            await Task.Delay(20);
        }

        throw new TimeoutException("The condition never became true.");
    }

    [Fact]
    public async Task ABuildThatEndsInAnyWayGivesTheGateBack()
    {
        using var vm = ViewModelWithProject();

        // The project does not exist, so the build fails (or throws) somewhere
        // after the gate was taken. Either way the gate must come back.
        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.BuildGate.CurrentCount);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ABuildStoppedWhileItWaitsLeavesTheGateAndTheBusyFlagToTheOneThatHoldsThem()
    {
        using var vm = ViewModelWithProject();

        // Another build (A) is under way: it holds the gate and the busy flag.
        var other = await vm.BuildGate.AcquireAsync("the build in progress");
        vm.BeginBusy();

        var waiting = vm.BuildCommand.ExecuteAsync(null);
        var operation = await WaitForAsync(() => vm.Operations.Current);

        Assert.Contains("Waiting for the build in progress", vm.StatusMessage);

        operation.Cancel();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10));

        // Still A's: stopping the queued one must not clear the flag under it.
        Assert.True(vm.IsBusy);
        Assert.Equal(0, vm.BuildGate.CurrentCount);

        other.Dispose();
        vm.EndBusy();

        Assert.False(vm.IsBusy);
        Assert.Equal(1, vm.BuildGate.CurrentCount);
    }

    [Fact]
    public async Task ASecondBuildRequestWhileOneIsQueuedSaysSoInsteadOfDoingNothing()
    {
        using var vm = ViewModelWithProject();
        using var other = await vm.BuildGate.AcquireAsync("the Immediate window's build");

        var first = vm.BuildCommand.ExecuteAsync(null);
        var operation = await WaitForAsync(() => vm.Operations.Current);

        // Not through the command, which refuses a second concurrent run on its own.
        var second = await vm.BuildQueuedStatusForTestsAsync();

        Assert.Contains("already waiting", second);
        Assert.Contains("Immediate window's build", second);

        operation.Cancel();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task APublishWaitsForTheGateBeforeClearingOrAnnouncing()
    {
        using var vm = ViewModelWithProject();
        vm.BuildOutput.Add("the earlier build's output");
        vm.StatusMessage = "earlier status";
        using var other = await vm.BuildGate.AcquireAsync("the test run");

        var request = new Basalt.Workspace.Publishing.PublishRequest(
            vm.StartupProject!, [], Path.Combine(_folder, "out"));

        var publish = vm.MakeExecutableAsync(request);
        var operation = await WaitForAsync(() => vm.Operations.Current);

        Assert.Contains("Waiting for the test run", vm.StatusMessage);
        Assert.Contains("the earlier build's output", vm.BuildOutput);

        operation.Cancel();
        Assert.Null(await publish.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("the earlier build's output", vm.BuildOutput);
        Assert.False(vm.IsBusy);
        Assert.Equal(0, vm.BuildGate.CurrentCount);
    }

    [Fact]
    public async Task ATestRunWaitsForTheGateAndReportsWhoItWaitsFor()
    {
        var gate = new BuildGate();
        var service = new DotnetTestService(gate);
        var waitingFor = new TaskCompletionSource<string>();
        var ended = new TaskCompletionSource();
        service.WaitingForGate += (_, holder) => waitingFor.TrySetResult(holder);
        service.WaitEnded += (_, _) => ended.TrySetResult();

        var project = Path.Combine(_folder, "Tests.vbproj");
        File.WriteAllText(project, "<Project />");
        using var other = await gate.AcquireAsync("the build in progress");
        using var cancel = new CancellationTokenSource();

        var run = service.RunAsync(project, null, cancel.Token);

        Assert.Equal("the build in progress", await waitingFor.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Null(service.LastStartInfo); // dotnet has not been started

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, gate.CurrentCount); // still the other's
    }

    [Fact]
    public async Task ADebugSessionCountsAsTheProgramRunningForTheImmediateBuild()
    {
        using var vm = ViewModelWithProject();
        vm.IsDebuggingProbe = () => true;

        Assert.True(vm.ReportIfDebugging());
        Assert.Contains("already running", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);

        var target = await vm.ResolveImmediateTargetAsync(default);

        Assert.Contains("in progress", target.BuildError);
        Assert.Equal(1, vm.BuildGate.CurrentCount);
    }

    [Fact]
    public async Task ALineWaitingForTheGateIsRefusedIfARunStartedMeanwhile()
    {
        var project = Path.Combine(_folder, "App.vbproj");
        File.WriteAllText(project, "<Project />");
        var gate = new BuildGate();
        var busy = false;
        var built = false;

        var resolver = new ImmediateTargetResolver(
            () => project, () => "Debug", () => busy,
            (_, _, _) =>
            {
                built = true;
                return Task.FromResult(new BuildResult(true, [], "x.dll", TimeSpan.Zero));
            },
            gate);

        var other = await gate.AcquireAsync("the build in progress");
        var line = resolver.ResolveAsync(default);

        await Task.Delay(100);
        Assert.False(line.IsCompleted);

        // The run starts while the line waits, then the gate opens.
        busy = true;
        other.Dispose();

        var target = await line.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(built);
        Assert.Contains("in progress", target.BuildError);
        Assert.Equal(1, gate.CurrentCount);
    }
}
