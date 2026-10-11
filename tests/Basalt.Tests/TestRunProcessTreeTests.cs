using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Core.Services;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Basalt.Workspace.Testing;

namespace Basalt.Tests;

/// <summary>
/// The process trees a test run leaves behind.
///
/// "dotnet test" starts a test host of its own, and the test host runs the
/// tests. A run that is cancelled, a window that closes during a run, or a
/// run that finishes must not leave either of them alive. The only processes
/// checked are the ones the runner started: the dotnet processes it reports
/// and the test host the project writes its own id into. Nothing scans the
/// system by name, because other sessions run "dotnet test" too.
///
/// The project is the same xUnit Visual Basic fixture the Test Explorer
/// journeys use, reduced to the two facts this needs.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class TestRunProcessTreeTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-run-tree-").FullName;

    private readonly string _pidFile;

    private readonly string _project;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-run-tree-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    /// <summary>Every process id the runner reported or the test host wrote, so none is left running.</summary>
    private readonly HashSet<int> _started = [];

    public TestRunProcessTreeTests()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

        var directory = Path.Combine(_root, "VbTests");
        Directory.CreateDirectory(directory);

        _pidFile = Path.Combine(_root, "testhost.pid");
        _project = Path.Combine(directory, "VbTests.vbproj");

        File.WriteAllText(_project, TestProjectXml);
        File.WriteAllText(Path.Combine(directory, "HoldTests.vb"),
            HoldSource.Replace("@PID_FILE@", _pidFile, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        // Only what this test started: a run still alive at the end of a
        // failing test would otherwise hold the runner and the build gate.
        foreach (var id in _started) KillIfAlive(id);

        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    // ---- fixture ------------------------------------------------------

    /// <summary>Same project file as the Test Explorer journeys.</summary>
    private const string TestProjectXml = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <RootNamespace></RootNamespace>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
            <PackageReference Include="xunit.v3" Version="3.2.2" />
            <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
          </ItemGroup>
        </Project>
        """;

    /// <summary>
    /// <c>Quick</c> finishes at once, so a run of it completes. <c>Waits</c>
    /// holds the test host for ten minutes, so there is a live run to stop.
    /// Both write the test host's process id where the test can read it.
    /// </summary>
    private const string HoldSource = """
        Imports Xunit

        Namespace Sample
            Public Class HoldTests

                <Fact>
                Public Sub Quick()
                    System.IO.File.WriteAllText("@PID_FILE@", System.Environment.ProcessId.ToString())
                End Sub

                <Fact>
                Public Sub Waits()
                    System.IO.File.WriteAllText("@PID_FILE@", System.Environment.ProcessId.ToString())
                    System.Threading.Thread.Sleep(600000)
                End Sub

            End Class
        End Namespace
        """;

    private static string WaitsFilter => DotnetTestService.FilterFor(["Sample.HoldTests.Waits"]);

    private static string QuickFilter => DotnetTestService.FilterFor(["Sample.HoldTests.Quick"]);

    // ---- helpers ------------------------------------------------------

    /// <summary>
    /// Polls the runner until the condition holds, recording every dotnet
    /// process it starts on the way. It must run while the run is in progress:
    /// a process that has already exited cannot be found in the runner's list.
    /// </summary>
    private async Task<bool> ObserveAsync(DotnetTestService service, Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < TimeSpan.FromMinutes(4))
        {
            foreach (var id in service.RunningProcessIds) _started.Add(id);

            if (condition()) return true;

            await Task.Delay(20);
        }

        return condition();
    }

    /// <summary>The test host's id, once the test has written it.</summary>
    private int? TestHostProcessId()
    {
        if (!File.Exists(_pidFile)) return null;

        if (!int.TryParse(File.ReadAllText(_pidFile).Trim(), out var id)) return null;

        _started.Add(id);
        return id;
    }

    private static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Asserts the process is gone. A process that has just been killed can
    /// take a moment to be reaped, so the check allows a short wait; a process
    /// that is still there after that is a leak.
    /// </summary>
    private static async Task AssertGoneAsync(int processId, string what)
    {
        var clock = Stopwatch.StartNew();

        while (IsAlive(processId) && clock.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(50);

        Assert.False(IsAlive(processId), $"{what} (process {processId}) is still running.");
    }

    private static void KillIfAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    /// <summary>
    /// Waits until the test host has written its id, or the run has ended
    /// without reaching it (a build failure, say), which the assertion names.
    /// </summary>
    private async Task<int> WaitForTestHostAsync(DotnetTestService service, Func<bool> runEnded)
    {
        int? testHost = null;

        await ObserveAsync(service, () => (testHost = TestHostProcessId()) is not null || runEnded());

        Assert.True(testHost is not null, "The test host never started running the test.");
        return testHost!.Value;
    }

    private static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < timeout)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }

        return condition();
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(target)!;

    private static string L(string key) => Localizer.Get(key);

    private static void InvokeMenu(MainWindow window, params string[] path)
    {
        IReadOnlyList<MenuEntry> level = window.MenuEntriesForTests();
        MenuEntry? hit = null;

        foreach (var header in path)
        {
            hit = level.FirstOrDefault(e => e.Header == header)
                ?? throw new InvalidOperationException("menu entry not found: " + string.Join(" > ", path));
            level = hit.Children ?? [];
        }

        if (hit!.Action is { } action) action();
        else hit.Command!.Execute(null);
    }

    // ------------------------------------------------------------------
    // A run that completes leaves nothing behind. This is the guard for the
    // normal exit: the runner must not start anything it does not end.
    // ------------------------------------------------------------------
    [Fact]
    public async Task ACompletedRunLeavesNoProcessBehind()
    {
        var service = new DotnetTestService(new BuildGate());

        var run = service.RunAsync(_project, QuickFilter);
        await ObserveAsync(service, () => run.IsCompleted);

        var results = await run;
        var testHost = TestHostProcessId();

        Assert.Single(results);
        Assert.Equal(TestOutcome.Passed, results[0].Outcome);
        Assert.NotNull(testHost);
        Assert.NotEmpty(_started);
        foreach (var id in _started) await AssertGoneAsync(id, "A dotnet process of the finished run");
    }

    // ------------------------------------------------------------------
    // A run cancelled through its token ends the dotnet test it started and
    // the test host that dotnet test started in turn.
    // ------------------------------------------------------------------
    [Fact]
    public async Task CancellingTheRunEndsTheDotnetTestAndTheTestHostItStarted()
    {
        var service = new DotnetTestService(new BuildGate());
        using var cancel = new CancellationTokenSource();

        var run = service.RunAsync(_project, WaitsFilter, cancel.Token);

        var testHost = await WaitForTestHostAsync(service, () => run.IsCompleted);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.NotEmpty(_started);
        foreach (var id in _started) await AssertGoneAsync(id, "A dotnet process of the cancelled run");
        await AssertGoneAsync(testHost, "The test host of the cancelled run");
    }

    // ------------------------------------------------------------------
    // Closing the window while a run is in progress ends the run's processes.
    // Nothing else closes them: the window is gone, and the run it started
    // would otherwise go on until the test host finished on its own.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ClosingTheWindowDuringARunEndsTheRunsProcessTree()
    {
        var host = OpenWindowWithTests();
        var closed = false;

        try
        {
            var testHost = await StartRunAllInWindowAsync(host);

            host.Dispose();
            closed = true;

            Assert.NotEmpty(_started);
            foreach (var id in _started) await AssertGoneAsync(id, "A dotnet process of the run in the closed window");
            await AssertGoneAsync(testHost, "The test host of the run in the closed window");
        }
        finally
        {
            if (!closed) host.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Opening another solution while a run is in progress ends the run of the
    // one that was open, so its results do not land in the new solution.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task OpeningAnotherSolutionEndsTheRunOfTheOneBeforeIt()
    {
        var other = Path.Combine(_root, "Empty.slnx");
        File.WriteAllText(other, "<Solution />");

        using var host = OpenWindowWithTests();

        var testHost = await StartRunAllInWindowAsync(host);

        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await vm.OpenSolutionAsync(other);

        Assert.NotEmpty(_started);
        foreach (var id in _started) await AssertGoneAsync(id, "A dotnet process of the run of the previous solution");
        await AssertGoneAsync(testHost, "The test host of the run of the previous solution");
    }

    // ------------------------------------------------------------------
    // Pressing Stop on the toolbar while the tests run ends them: the button
    // is the user's only way to stop a run that is still going.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task TheToolbarStopButtonEndsTheTestRun()
    {
        using var host = OpenWindowWithTests();

        var testHost = await StartRunAllInWindowAsync(host);

        var stop = host.Window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => ToolTip.GetTip(b) as string == L(StringKeys.MenuRunStop));

        Assert.NotNull(stop);
        Assert.True(stop!.IsEnabled, "Stop is greyed out while the tests run.");

        stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.NotEmpty(_started);
        foreach (var id in _started) await AssertGoneAsync(id, "A dotnet process of the stopped test run");
        await AssertGoneAsync(testHost, "The test host of the stopped test run");
    }

    /// <summary>A window with the xUnit solution open, as the journeys open one.</summary>
    private TestWindow OpenWindowWithTests()
    {
        var solution = Path.Combine(_root, "Tests.slnx");
        File.WriteAllText(solution, """
            <Solution>
              <Project Path="VbTests/VbTests.vbproj" />
            </Solution>
            """);

        return new TestWindow(solution);
    }

    /// <summary>
    /// Waits for discovery, runs every test from the menu, and returns once the
    /// test host is inside the long-running test. The runner's processes are
    /// recorded in <see cref="_started"/>.
    /// </summary>
    private async Task<int> StartRunAllInWindowAsync(TestWindow host)
    {
        await host.SettleAsync(10);

        var w = host.Window;
        var panel = Field<TestExplorerPanel>(w, "_tests");
        var service = Field<DotnetTestService>(w, "_testService");

        // Discovery runs on open; Run All has nothing to run until it has finished.
        Assert.True(await WaitUntilAsync(host, () => panel.AllTests.Count == 2, TimeSpan.FromMinutes(4)),
            "the xUnit tests were not discovered.");

        InvokeMenu(w, L(StringKeys.MenuTest), L(StringKeys.MenuTestRunAll));

        return await WaitForTestHostAsync(service, () => false);
    }
}
