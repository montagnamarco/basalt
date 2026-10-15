using System.Diagnostics;
using Basalt.Core.Services;
using Basalt.Workspace.Debugging;
using StackFrame = Basalt.Core.Services.StackFrame;

namespace Basalt.Tests;

/// <summary>
/// Attaching to a running .NET process: finding the processes that can be attached to, and the debugger attached to
/// one that really runs, with its breakpoints, Call Stack and Break All, letting it go on running when stopped.
/// </summary>
public sealed class NetCoreDebugAttachTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-attach-").FullName;
    private readonly List<Process> _started = [];

    public void Dispose()
    {
        foreach (var process in _started)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.Dispose();
        }

        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ---- the list of processes -----------------------------------------------------

    private string Endpoint(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "");

        return path;
    }

    [Fact]
    public void AProcessWithADiagnosticsEndpointIsListedByNameAndId()
    {
        var self = Environment.ProcessId;
        Endpoint($"dotnet-diagnostic-{self}-1234567-socket");
        Endpoint("dotnet-diagnostic-notanumber-1-socket");
        Endpoint("unrelated.txt");

        var found = DotnetProcessLister.List([_root], excludedProcessId: -1);

        var me = Assert.Single(found);
        Assert.Equal(self, me.ProcessId);
        Assert.Equal(Process.GetCurrentProcess().ProcessName, me.Name);
    }

    [Fact]
    public void AnEndpointOfAProcessThatHasGoneIsNotListed()
    {
        // Left behind by a program that crashed.
        Endpoint("dotnet-diagnostic-2000000000-1-socket");

        Assert.Empty(DotnetProcessLister.List([_root], excludedProcessId: -1));
    }

    [Fact]
    public void AnEndpointOlderThanTheProcessThatHasItsIdNowBelongsToACrashedOne()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows endpoints are pipes, which go with their process.");

        var path = Endpoint($"dotnet-diagnostic-{Environment.ProcessId}-1-socket");
        File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-30));

        Assert.Empty(DotnetProcessLister.List([_root], excludedProcessId: -1));
    }

    [Fact]
    public void TheIdeItselfIsNotOffered()
    {
        Endpoint($"dotnet-diagnostic-{Environment.ProcessId}-1-socket");

        Assert.Empty(DotnetProcessLister.List([_root], excludedProcessId: Environment.ProcessId));
    }

    [Fact]
    public void AMissingDirectoryListsNothing() =>
        Assert.Empty(DotnetProcessLister.List([Path.Combine(_root, "missing")], excludedProcessId: -1));

    // ---- a real program ------------------------------------------------------------

    private async Task<(Process Process, string Heartbeat, string Source, string Assembly)> StartLooperAsync()
    {
        var (_, source, assembly) = await NetCoreDebugTargets.BuildAsync(_root, "Looper", NetCoreDebugTargets.Looper);
        var heartbeat = Path.Combine(_root, "heartbeat.txt");

        var process = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { assembly, heartbeat },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        _started.Add(process);

        // Running, not merely started: the file is written by the loop.
        await WaitUntilAsync(() => File.Exists(heartbeat), TimeSpan.FromSeconds(30), "The program did not start.");

        return (process, heartbeat, source, assembly);
    }

    private static async Task WaitUntilAsync(Func<bool> done, TimeSpan timeout, string failure)
    {
        var clock = Stopwatch.StartNew();

        while (!done())
        {
            Assert.True(clock.Elapsed < timeout, failure);
            await Task.Delay(50);
        }
    }

    private static int LineCount(string path)
    {
        try { return File.ReadAllLines(path).Length; }
        catch (IOException) { return -1; }
    }

    [Fact]
    public async Task ARunningProgramIsListedAndCanBeDebuggedThenLetGo()
    {
        Assert.SkipWhen(DebugAdapterLocator.Find() is null, "netcoredbg is not installed.");

        var (program, heartbeat, source, assembly) = await StartLooperAsync();

        // Listed: the runtime opens its diagnostics endpoint as it starts, the file the lister looks for.
        IReadOnlyList<DotnetProcessInfo> listed = [];
        await WaitUntilAsync(
            () => (listed = DotnetProcessLister.List()).Any(p => p.ProcessId == program.Id),
            TimeSpan.FromSeconds(30), "The running program was not listed.");

        var entry = listed.Single(p => p.ProcessId == program.Id);
        Assert.Equal("dotnet", entry.Name);

        // Which program "dotnet" is running: the command line where the process has no window title to give.
        if (!OperatingSystem.IsWindows()) Assert.Contains("Looper.dll", entry.Title, StringComparison.Ordinal);

        using var service = new NetCoreDebugService(DebugAdapterLocator.Find()!);
        var paused = new TaskCompletionSource<StackFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Paused += (_, frame) => paused.TrySetResult(frame);

        await service.SetBreakpointsAsync(source, [new Breakpoint(source, NetCoreDebugTargets.LooperBreakpointLine)]);
        await service.AttachAsync(program.Id, assembly, detachOnStop: true);

        // The breakpoint set before attaching is hit in the next pass of the loop.
        var frame = await paused.Task.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(NetCoreDebugTargets.LooperBreakpointLine, frame.Line);
        Assert.Contains("Main", frame.Method, StringComparison.Ordinal);

        var stack = await service.GetCallStackAsync();
        Assert.Contains("Main", stack[0].Method, StringComparison.Ordinal);

        // Stopped there, the program does not tick.
        var frozen = LineCount(heartbeat);
        await Task.Delay(700);
        Assert.Equal(frozen, LineCount(heartbeat));

        // Break All: with the breakpoint gone, the running program is stopped wherever it is.
        await service.SetBreakpointsAsync(source, []);
        var broken = new TaskCompletionSource<StackFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Paused += (_, f) => broken.TrySetResult(f);
        await service.ContinueAsync();
        await WaitUntilAsync(() => LineCount(heartbeat) > frozen + 1, TimeSpan.FromSeconds(20), "The program did not go on.");
        await service.PauseAsync();
        await broken.Task.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(service.IsPaused);

        await service.ContinueAsync();

        // Stop detaches: the process is still there, and still ticking.
        await service.StopAsync();
        var beforeWait = LineCount(heartbeat);
        await Task.Delay(1500);

        Assert.False(program.HasExited, "Stopping the session ended the process it had only attached to.");
        Assert.True(LineCount(heartbeat) > beforeWait, "The process stopped ticking after the debugger let go.");
    }

    [Fact]
    public async Task StoppingASessionThatLaunchedTheProgramStillEndsIt()
    {
        // The control for the test above: a plain attach, as the terminal debug path makes, ends its program.
        Assert.SkipWhen(DebugAdapterLocator.Find() is null, "netcoredbg is not installed.");

        var (program, heartbeat, _, assembly) = await StartLooperAsync();

        using var service = new NetCoreDebugService(DebugAdapterLocator.Find()!);
        await service.AttachAsync(program.Id, assembly);
        await service.StopAsync();

        Assert.True(program.WaitForExit(15000), "A program attached to without detachOnStop should end with the session.");
    }
}
