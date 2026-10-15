using System.Text.Json.Nodes;
using Basalt.Core.Services;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Debugging;

namespace Basalt.Tests;

/// <summary>
/// Which exceptions stop the debugger, how a stop on a thrown exception is told apart, and how a session lets go of
/// a process it attached to. Against the recording stand-in for the adapter: the requests are what is checked here,
/// and the live journeys in UserJourneyDebuggerTests2Exceptions prove netcoredbg does what they ask.
/// </summary>
public sealed class NetCoreDebugExceptionTests
{
    private static List<string> Commands(FakeDapConnection dap) => [.. dap.Requests.Select(r => r.Command)];

    private static string[] FiltersOfLast(FakeDapConnection dap) =>
        [.. dap.ArgumentsOf("setExceptionBreakpoints")!["filters"]!.AsArray().Select(f => f!.GetValue<string>())];

    [Fact]
    public async Task TheChoiceIsSentAfterTheBreakpointsAndBeforeConfigurationDone()
    {
        // The protocol's order: breakpoints and exception filters between "initialized" and "configurationDone".
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);

        await service.SetBreakpointsAsync("/app/Program.vb", [new Breakpoint("/app/Program.vb", 3)]);
        await service.SetExceptionBreaksAsync(new ExceptionBreaks(WhenThrown: true, WhenUserUnhandled: false));
        await service.LaunchAsync("/app/App.dll");

        var commands = Commands(dap);

        Assert.True(commands.IndexOf("setBreakpoints") < commands.IndexOf("setExceptionBreakpoints"));
        Assert.True(commands.IndexOf("setExceptionBreakpoints") < commands.IndexOf("configurationDone"));
        Assert.Equal(["all"], FiltersOfLast(dap));
    }

    [Fact]
    public async Task BothChoicesSendBothFilters()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);

        await service.SetExceptionBreaksAsync(new ExceptionBreaks(true, true));
        await service.LaunchAsync("/app/App.dll");

        Assert.Equal(["all", "user-unhandled"], FiltersOfLast(dap));
    }

    [Fact]
    public async Task UserUnhandledAloneSendsOnlyThatFilter()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);

        await service.SetExceptionBreaksAsync(new ExceptionBreaks(false, true));
        await service.LaunchAsync("/app/App.dll");

        Assert.Equal(["user-unhandled"], FiltersOfLast(dap));
    }

    [Fact]
    public async Task NothingIsSentWhenNobodyChose()
    {
        // netcoredbg stops on an unhandled exception by itself; a session nobody configured keeps doing exactly that.
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);

        await service.LaunchAsync("/app/App.dll");

        Assert.DoesNotContain("setExceptionBreakpoints", Commands(dap));
    }

    [Fact]
    public async Task AChangeDuringTheSessionIsSentAtOnce()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);

        await service.LaunchAsync("/app/App.dll");
        await service.SetExceptionBreaksAsync(new ExceptionBreaks(true, false));

        Assert.Equal(["all"], FiltersOfLast(dap));

        await service.SetExceptionBreaksAsync(new ExceptionBreaks(false, false));

        Assert.Empty(FiltersOfLast(dap));
        Assert.Equal(2, Commands(dap).Count(c => c == "setExceptionBreakpoints"));
    }

    [Fact]
    public async Task ARefusedFilterIsReportedAndTheSessionGoesOn()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);
        Exception? fault = null;
        service.Faulted += (_, ex) => fault = ex;

        await service.LaunchAsync("/app/App.dll");
        dap.SetExceptionBreakpointsAnswer = FakeDapConnection.Answer(false, new JsonObject(), "Unknown filter");
        await service.SetExceptionBreaksAsync(new ExceptionBreaks(true, false));

        Assert.Contains("Unknown filter", fault?.Message, StringComparison.Ordinal);
        Assert.True(service.IsRunning);
    }

    private static JsonObject ThrownInfo(string breakMode) => FakeDapConnection.Answer(true, new JsonObject
    {
        ["breakMode"] = breakMode,
        ["exceptionId"] = "CLR/System.InvalidOperationException",
        ["details"] = new JsonObject
        {
            ["fullTypeName"] = "System.InvalidOperationException",
            ["message"] = "boom handled"
        }
    });

    private static async Task<NetCoreDebugService> StoppedOnAsync(string breakMode)
    {
        var dap = new FakeDapConnection { ExceptionInfoAnswer = ThrownInfo(breakMode) };
        var service = new NetCoreDebugService(() => dap);
        var paused = new TaskCompletionSource();
        service.Paused += (_, _) => paused.TrySetResult();

        await service.LaunchAsync("/app/App.dll");
        dap.Raise("stopped", new JsonObject { ["threadId"] = 1, ["reason"] = "exception" });
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));

        return service;
    }

    [Fact]
    public async Task AStopAtTheThrowCarriesTheTypeAndMessageAndIsMarkedThrown()
    {
        var service = await StoppedOnAsync("always");

        Assert.Equal("System.InvalidOperationException: boom handled", service.StoppedException);
        Assert.True(service.StoppedOnThrow);
    }

    [Fact]
    public async Task AStopOnAnUnhandledExceptionIsNotMarkedThrown()
    {
        var service = await StoppedOnAsync("unhandled");

        Assert.Equal("System.InvalidOperationException: boom handled", service.StoppedException);
        Assert.False(service.StoppedOnThrow);
    }

    [Fact]
    public async Task TheSessionPassesTheChoiceOnWhenItStartsAndWhenItChanges()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);
        using var session = new DebugSession();

        // Chosen before any session exists, as the window does from the saved settings.
        await session.SetExceptionBreaksAsync(new ExceptionBreaks(true, false));
        await session.StartWithAsync(service, "/app/App.dll", "/app");

        Assert.Equal(["all"], FiltersOfLast(dap));

        await session.SetExceptionBreaksAsync(new ExceptionBreaks(false, true));

        Assert.Equal(["user-unhandled"], FiltersOfLast(dap));
    }

    // ---- letting go of a process -------------------------------------------------

    private static bool TerminatesOnDisconnect(FakeDapConnection dap) =>
        dap.ArgumentsOf("disconnect")!["terminateDebuggee"]!.GetValue<bool>();

    [Fact]
    public async Task StoppingAProgramTheIdeLaunchedEndsIt()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);

        await service.LaunchAsync("/app/App.dll");
        await service.StopAsync();

        Assert.True(TerminatesOnDisconnect(dap));
        Assert.False(service.DetachesOnStop);
    }

    [Fact]
    public async Task StoppingAProcessTheUserAttachedToDetachesAndLeavesItRunning()
    {
        // Measured on netcoredbg 3.2.0: terminateDebuggee false detaches, true kills the attached process.
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);

        await service.AttachAsync(4242, programPath: null, detachOnStop: true);
        await service.StopAsync();

        Assert.False(TerminatesOnDisconnect(dap));
        Assert.True(service.DetachesOnStop);
    }

    [Fact]
    public async Task AProgramTheIdeStartedInATerminalIsStillEndedWithTheSession()
    {
        // The terminal debug path attaches too, but the program is the IDE's own: it is not let go.
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);

        await service.AttachAsync(4242, "/app/App.dll");
        await service.StopAsync();

        Assert.True(TerminatesOnDisconnect(dap));
    }

    [Fact]
    public async Task ARefusedAttachDoesNotKillTheProcessItWasMeantToLetGo()
    {
        var dap = new FakeDapConnection
        {
            AttachAnswer = FakeDapConnection.Answer(false, new JsonObject(), "Failed to attach to process 4242")
        };
        var service = new NetCoreDebugService(() => dap);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AttachAsync(4242, null, detachOnStop: true));

        Assert.False(TerminatesOnDisconnect(dap));
    }

    [Fact]
    public async Task LaunchingAfterAnAttachGoesBackToEndingTheProgram()
    {
        var adapters = new List<FakeDapConnection>();
        var service = new NetCoreDebugService(() =>
        {
            adapters.Add(new FakeDapConnection());
            return adapters[^1];
        });

        await service.AttachAsync(4242, null, detachOnStop: true);
        await service.LaunchAsync("/app/App.dll");

        Assert.False(service.DetachesOnStop);

        await service.StopAsync();

        Assert.False(TerminatesOnDisconnect(adapters[0]));
        Assert.True(TerminatesOnDisconnect(adapters[1]));
    }

    [Fact]
    public async Task StoppingIsNotAnExitToReport()
    {
        // The adapter closing after our own disconnect would otherwise be told as "the program exited with code 0",
        // which after a detach is false: the process is still running.
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);
        var exits = new List<int>();
        service.Exited += (_, code) => exits.Add(code);

        await service.AttachAsync(4242, null, detachOnStop: true);
        await service.StopAsync();

        Assert.Empty(exits);
    }

    [Fact]
    public async Task ASessionTheIdeLaunchedDoesNotDetach()
    {
        using var session = new DebugSession();

        Assert.False(session.DetachesOnStop);

        await session.StartWithAsync(new NetCoreDebugService(() => new FakeDapConnection()), "/app/App.dll", "/app");

        Assert.False(session.DetachesOnStop);
    }
}
