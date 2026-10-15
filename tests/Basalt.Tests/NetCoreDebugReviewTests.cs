using System.Diagnostics;
using System.Text.Json.Nodes;
using Basalt.Core.Services;
using Basalt.Workspace;
using Basalt.Workspace.Debugging;
using Basalt.Workspace.Testing;
using StackFrame = Basalt.Core.Services.StackFrame;

namespace Basalt.Tests;

/// <summary>
/// What the review of the exception, attach, launch-profile and Debug Test work asked for: a debugger nobody
/// configured stops on an unhandled exception all the same, a disconnect is answered before the adapter is closed,
/// the launch profile is read as "dotnet run" reads it, the process list is the user's own, and a held test run ends.
/// </summary>
public sealed class NetCoreDebugReviewTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-review-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ---- 1. filters: [] changes nothing about an unhandled exception -----------------

    private const string UnhandledThrower = """
        Module Program
            Sub Main()
                Console.WriteLine("start")
                Throw New InvalidOperationException("boom unhandled")
            End Sub
        End Module
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnhandledExceptionStopsWithItsTypeAndMessageWhetherOrNotTheFiltersWereSent(bool choiceMadeAndEmpty)
    {
        // Measured with netcoredbg 3.2.0 through the real service: even after setExceptionBreakpoints with no filter
        // at all, the exception nothing handles stops, with its type and message.
        Assert.SkipWhen(DebugAdapterLocator.Find() is null, "netcoredbg is not installed.");

        var (_, _, assembly) = await NetCoreDebugTargets.BuildAsync(_root, "App", UnhandledThrower);
        using var service = new NetCoreDebugService(DebugAdapterLocator.Find()!);
        var paused = new TaskCompletionSource();
        service.Paused += (_, _) => paused.TrySetResult();

        if (choiceMadeAndEmpty) await service.SetExceptionBreaksAsync(new ExceptionBreaks(false, false));

        await service.LaunchAsync(assembly);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal("System.InvalidOperationException: boom unhandled", service.StoppedException);
        Assert.False(service.StoppedOnThrow);
    }

    // ---- 4. the disconnect is answered before the adapter is closed --------------------

    [Fact]
    public async Task StopWaitsForTheAdaptersAnswerToDisconnectBeforeClosingIt()
    {
        var dap = new FakeDapConnection { HoldDisconnect = new TaskCompletionSource() };
        var service = new NetCoreDebugService(() => dap);
        await service.AttachAsync(4242, null, detachOnStop: true);

        var stopping = service.StopAsync();
        await Task.Delay(200);

        Assert.False(stopping.IsCompleted);
        Assert.True(dap.IsRunning, "The adapter was closed before it answered the disconnect.");

        dap.HoldDisconnect.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(dap.IsRunning);
    }

    [Fact]
    public async Task AnAdapterThatNeverAnswersTheDisconnectIsClosedAfterTheTimeout()
    {
        var dap = new FakeDapConnection { HoldDisconnect = new TaskCompletionSource() };
        var service = new NetCoreDebugService(() => dap) { DisconnectTimeout = TimeSpan.FromMilliseconds(200) };
        await service.LaunchAsync("/app/App.dll");

        await service.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(dap.IsRunning);
    }

    [Fact]
    public async Task ADetachWhileStoppedAtABreakpointLeavesTheProcessRunning()
    {
        // Measured: netcoredbg detaches from a process stopped at a breakpoint without continuing it first, and the
        // process runs on (its heartbeat keeps growing).
        Assert.SkipWhen(DebugAdapterLocator.Find() is null, "netcoredbg is not installed.");

        var (_, source, assembly) = await NetCoreDebugTargets.BuildAsync(_root, "Looper", NetCoreDebugTargets.Looper);
        var heartbeat = Path.Combine(_root, "heartbeat.txt");

        using var program = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { assembly, heartbeat },
            UseShellExecute = false,
            RedirectStandardOutput = true
        })!;

        try
        {
            while (!File.Exists(heartbeat)) await Task.Delay(50);

            using var service = new NetCoreDebugService(DebugAdapterLocator.Find()!);
            var paused = new TaskCompletionSource<StackFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.Paused += (_, f) => paused.TrySetResult(f);

            await service.SetBreakpointsAsync(source, [new Breakpoint(source, NetCoreDebugTargets.LooperBreakpointLine)]);
            await service.AttachAsync(program.Id, assembly, detachOnStop: true);
            await paused.Task.WaitAsync(TimeSpan.FromSeconds(60));

            await service.StopAsync();

            var count = File.ReadAllLines(heartbeat).Length;
            await Task.Delay(1500);

            Assert.False(program.HasExited);
            Assert.True(File.ReadAllLines(heartbeat).Length > count + 2, "The process stayed suspended after the detach.");
        }
        finally
        {
            if (!program.HasExited) program.Kill(entireProcessTree: true);
        }
    }

    // ---- 5. the small ones --------------------------------------------------------------

    [Fact]
    public async Task AUserUnhandledStopIsNotCalledAThrownException()
    {
        var dap = new FakeDapConnection
        {
            ExceptionInfoAnswer = FakeDapConnection.Answer(true, new JsonObject
            {
                ["breakMode"] = "userUnhandled",
                ["details"] = new JsonObject { ["fullTypeName"] = "System.InvalidOperationException", ["message"] = "boom" }
            })
        };
        var service = new NetCoreDebugService(() => dap);
        var paused = new TaskCompletionSource();
        service.Paused += (_, _) => paused.TrySetResult();

        await service.LaunchAsync("/app/App.dll");
        dap.Raise("stopped", new JsonObject { ["threadId"] = 1, ["reason"] = "exception" });
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("System.InvalidOperationException: boom", service.StoppedException);
        Assert.False(service.StoppedOnThrow);
    }

    [Fact]
    public async Task ChoosingWhileTheAdapterIsBeingClosedDoesNotThrow()
    {
        // A closed DapClient answers IsRunning with InvalidOperationException (its process is gone): the choice made
        // in that moment is dropped, not raised at the user.
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);
        await service.LaunchAsync("/app/App.dll");

        dap.IsRunningThrows = true;

        await service.SetExceptionBreaksAsync(new ExceptionBreaks(true, false));
    }

    private string Project(string launchSettings)
    {
        var project = Path.Combine(_root, "App.vbproj");
        File.WriteAllText(project, NetCoreDebugTargets.ExeProject);
        Directory.CreateDirectory(Path.Combine(_root, "My Project"));
        File.WriteAllText(Path.Combine(_root, "My Project", "launchSettings.json"), launchSettings);

        return project;
    }

    [Fact]
    public void TheFirstProfileThatRunsTheProjectIsTheOneUsed()
    {
        var project = Project("""
            { "profiles": {
                "IIS Express": { "commandName": "IISExpress", "commandLineArgs": "wrong" },
                "App": { "commandName": "Project", "commandLineArgs": "right" } } }
            """);

        Assert.Equal(["right"], DebugLaunchProfile.For(project, _root).Arguments);
    }

    [Fact]
    public void AFileWithNoProfileThatRunsTheProjectAppliesNothing()
    {
        var project = Project("""{ "profiles": { "Exe": { "commandName": "Executable", "commandLineArgs": "wrong" } } }""");

        var options = DebugLaunchProfile.For(project, _root);

        Assert.Empty(options.Arguments);
        Assert.Equal(_root, options.WorkingDirectory);
    }

    [Fact]
    public void CommandLineArgsThatAreNotAStringGiveAClearErrorNamingTheFile()
    {
        var project = Project("""{ "profiles": { "App": { "commandName": "Project", "commandLineArgs": ["a", "b"] } } }""");

        var error = Assert.Throws<InvalidDataException>(() => DebugLaunchProfile.For(project, _root));

        Assert.Contains("launchSettings.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AProcessOfAnotherUserIsNotOffered()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows endpoints are pipes of the user's own session.");

        File.WriteAllText(Path.Combine(_root, $"dotnet-diagnostic-{Environment.ProcessId}-1-socket"), "");
        var me = DotnetProcessLister.CurrentUserId!.Value;

        Assert.Single(DotnetProcessLister.List([_root], excludedProcessId: -1, currentUserId: me));
        Assert.Empty(DotnetProcessLister.List([_root], excludedProcessId: -1, currentUserId: me + 1));
    }

    // ---- 2. Debug Test --------------------------------------------------------------------

    [Fact]
    public async Task ATestProjectForSeveralFrameworksIsRefusedWithAMessage()
    {
        var project = Path.Combine(_root, "Multi.csproj");
        File.WriteAllText(project, """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>net9.0;net10.0</TargetFrameworks></PropertyGroup></Project>""");
        var service = new DotnetTestService(new BuildGate());

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DebugAsync(project, null, _ => { }));

        Assert.Contains("Multi.csproj", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(service.RunningProcessIds);
    }

    private async Task<string> BuildTestProjectAsync()
    {
        var (project, _, _) = await NetCoreDebugTargets.BuildAsync(
            Path.Combine(_root, "tests"), "CalcTests", UserJourneyDebuggerTests2Sources.Tests, UserJourneyDebuggerTests2Sources.TestProject);

        return project;
    }

    /// <summary>Waits for a process to be gone: the run's end must take its test host with it.</summary>
    private static async Task AssertGoneAsync(int processId)
    {
        for (var i = 0; i < 100; i++)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited) return;
            }
            catch (ArgumentException)
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"The test host (process {processId}) was still running after the run ended.");
    }

    /// <summary>The dotnet processes whose command line names the folder: a test host left behind by a run.</summary>
    private static List<string> ProcessesRunningFrom(string folder)
    {
        if (OperatingSystem.IsWindows()) return [];

        using var ps = Process.Start(new ProcessStartInfo("ps", "-ww -eo pid=,args=")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        })!;
        var output = ps.StandardOutput.ReadToEnd();
        ps.WaitForExit();

        return [.. output.Split('\n').Where(l => l.Contains(folder, StringComparison.Ordinal) && l.Contains("dotnet", StringComparison.Ordinal))];
    }

    [Fact]
    public async Task ATestHostThatNeverNamesItsProcessEndsTheRunWithAMessage()
    {
        var project = await BuildTestProjectAsync();

        // The host is really started and really waits for a debugger; only the service is kept from hearing which
        // process it is, so the wait for its id is what runs out. The test hears it, to check that host is then gone.
        var hostId = 0;
        var service = new DotnetTestService(new BuildGate())
        {
            HostTimeout = TimeSpan.FromSeconds(15),
            HostLineReader = line =>
            {
                if (DotnetTestService.HostProcessIdIn(line) is { } id) hostId = id;
                return null;
            }
        };
        var told = 0;

        var timeout = await Assert.ThrowsAsync<TimeoutException>(
            () => service.DebugAsync(project, null, _ => told++).WaitAsync(TimeSpan.FromMinutes(3)));

        Assert.Contains("process id", timeout.Message, StringComparison.Ordinal);
        Assert.Empty(service.RunningProcessIds);
        Assert.Equal(0, told);

        // The host was running and waiting when the time ran out, and it went with the run.
        Assert.NotEqual(0, hostId);
        await AssertGoneAsync(hostId);
    }

    [Fact]
    public async Task CancellingWhileTheHostIsHeldEndsTheRunWithoutASecondHost()
    {
        var project = await BuildTestProjectAsync();
        var service = new DotnetTestService(new BuildGate());
        using var stop = new CancellationTokenSource();
        var told = 0;
        var hostId = 0;

        var run = service.DebugAsync(project, null, id => { told++; hostId = id; stop.Cancel(); }, stop.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromMinutes(3)));

        Assert.Equal(1, told);
        Assert.Empty(service.RunningProcessIds);

        // The host that was held is gone, not left waiting for a debugger.
        await AssertGoneAsync(hostId);
    }

    // ---- the race between stops, and the stop of a dead adapter --------------------------

    [Fact]
    public async Task TwoOverlappingStopsLeaveNoExceptionAndDoNotTouchASessionStartedMeanwhile()
    {
        var first = new FakeDapConnection { HoldDisconnect = new TaskCompletionSource() };
        var second = new FakeDapConnection();
        using var session = new Basalt.Shell.ViewModels.DebugSession();

        await session.StartWithAsync(new NetCoreDebugService(() => first), "/app/App.dll", "/app");

        var firstStop = session.StopAsync();      // waits for the adapter's answer
        await session.StopAsync();                // Shift+F5 again: nothing left to stop, and no exception

        // A new session starts while the first is still being closed.
        await session.StartWithAsync(new NetCoreDebugService(() => second), "/app/App.dll", "/app");

        first.HoldDisconnect.SetResult();
        await firstStop.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(session.IsRunning, "The first stop ended the session that was started after it.");
        Assert.True(second.IsRunning);
    }

    [Fact]
    public async Task AnAttachStoppedWhileItIsUnderWayEndsQuietly()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);

        // Stop pressed at the moment the adapter is asked to attach.
        dap.OnRequest = command =>
        {
            if (command == "attach") service.StopAsync().GetAwaiter().GetResult();
        };

        await service.AttachAsync(4242, null, detachOnStop: true);

        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task ALaunchStoppedWhileItIsUnderWayEndsQuietly()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);
        dap.OnRequest = command =>
        {
            if (command == "initialize") service.StopAsync().GetAwaiter().GetResult();
        };

        await service.LaunchAsync("/app/App.dll");

        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task StoppingADeadAdapterDoesNotWaitOutTheTimeout()
    {
        var dap = new FakeDapConnection { HoldDisconnect = new TaskCompletionSource() };
        var service = new NetCoreDebugService(() => dap) { DisconnectTimeout = TimeSpan.FromSeconds(30) };
        await service.LaunchAsync("/app/App.dll");

        dap.Kill();

        var clock = Stopwatch.StartNew();
        await service.StopAsync();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"Stopping a dead adapter took {clock.Elapsed}.");
    }

    [Fact]
    public async Task TheSynchronousStopOfAWindowClosingWaitsAtMostAboutASecond()
    {
        // An adapter that is alive but does not answer: the interface thread must not be held for the whole timeout.
        var dap = new FakeDapConnection { HoldDisconnect = new TaskCompletionSource() };
        var service = new NetCoreDebugService(() => dap) { DisconnectTimeout = TimeSpan.FromSeconds(30) };
        await service.LaunchAsync("/app/App.dll");

        var clock = Stopwatch.StartNew();
        service.Dispose();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), $"Disposing took {clock.Elapsed}.");
        Assert.False(dap.IsRunning);
    }

    // ---- the small ones -------------------------------------------------------------------

    [Fact]
    public void AProcessThatVanishesWhileBeingReadDoesNotHideTheOthers()
    {
        void Process(int id, bool withCommandLine)
        {
            var directory = Path.Combine(_root, id.ToString());
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "status"), $"Name:\tdotnet\nUid:\t{id}\t{id}\t{id}\t{id}\n");
            if (withCommandLine) File.WriteAllText(Path.Combine(directory, "cmdline"), "dotnet\0App.dll\0");
        }

        Process(100, true);
        Process(200, false);   // gone between its status and its command line
        Process(300, true);

        var facts = DotnetProcessLister.FactsFromProc(_root, [100, 200, 300]);

        Assert.Equal([100, 300], facts.Keys.Order());
        Assert.Equal(300u, facts[300].Owner);
        Assert.Equal("dotnet App.dll", facts[100].CommandLine);
    }

    [Fact]
    public void AProfileThatIsNotAnObjectIsSkipped()
    {
        var project = Project("""{ "profiles": { "note": "text", "App": { "commandName": "Project", "commandLineArgs": "x" } } }""");

        Assert.Equal(["x"], DebugLaunchProfile.For(project, _root).Arguments);

        var onlyText = Project("""{ "profiles": { "note": "text" } }""");

        Assert.Empty(DebugLaunchProfile.For(onlyText, _root).Arguments);
    }

    [Fact]
    public async Task DebugTestOnATestingPlatformProjectIsRefusedAtOnce()
    {
        var project = Path.Combine(_root, "Mtp.vbproj");
        File.WriteAllText(project, """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner></PropertyGroup></Project>""");
        var service = new DotnetTestService(new BuildGate());

        var clock = Stopwatch.StartNew();
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DebugAsync(project, null, _ => { }));

        Assert.Contains("Mtp.vbproj", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Testing.Platform", refusal.Message, StringComparison.Ordinal);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Empty(service.RunningProcessIds);
    }

    [Fact]
    public async Task ALateExitStartAndFaultOfAReplacedServiceLeaveTheNewSessionAlone()
    {
        var old = new FakeDapConnection();
        var current = new FakeDapConnection();
        using var session = new Basalt.Shell.ViewModels.DebugSession();
        var exits = 0;
        var starts = 0;
        session.Exited += (_, _) => exits++;
        session.Started += (_, _) => starts++;

        await session.StartWithAsync(new NetCoreDebugService(() => old), "/app/App.dll", "/app");
        await session.StartWithAsync(new NetCoreDebugService(() => current), "/app/App.dll", "/app");

        // The first service reports late, as the interpreter does from its own thread.
        old.Raise("process", new JsonObject());
        old.Raise("exited", new JsonObject { ["exitCode"] = 0 });

        Assert.Equal(0, exits);
        Assert.Equal(0, starts);
        Assert.True(session.IsRunning);

        // The current one is still heard.
        current.Raise("process", new JsonObject());
        current.Raise("exited", new JsonObject { ["exitCode"] = 0 });

        Assert.Equal(1, starts);
        Assert.Equal(1, exits);
    }
}

/// <summary>The Visual Basic test project the Debug Test tests share.</summary>
internal static class UserJourneyDebuggerTests2Sources
{
    public const string TestProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <RootNamespace></RootNamespace>
            <IsPackable>false</IsPackable>
            <DebugType>portable</DebugType>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
            <PackageReference Include="xunit" Version="2.9.3" />
            <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
          </ItemGroup>
        </Project>
        """;

    public const string Tests = """
        Imports Xunit

        Public Class CalcTests
            <Fact>
            Public Sub AddsTwo()
                Dim a As Integer = 2
                Dim b As Integer = a + 2
                Assert.Equal(4, b)
            End Sub
        End Class
        """;
}
