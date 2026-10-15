using Avalonia.Headless.XUnit;
using Basalt.Core.Services;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

public sealed partial class UserJourneyDebuggerTests2
{
    private const string XunitProject = """
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

    private const string XunitSource = """
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

    private static TestNode LeafOf(TestNode node) =>
        node.Test is not null ? node : node.Children.Select(LeafOf).First(n => n is not null);

    [AvaloniaFact]
    public async Task DebugTestStopsAtABreakpointInsideTheTestAndTheRunThenFinishes()
    {
        Assert.SkipWhen(AdapterMissing, "netcoredbg is not installed.");

        var root = Path.Combine(_root, "tests");
        var (project, source, _) = await NetCoreDebugTargets.BuildAsync(root, "CalcTests", XunitSource, XunitProject);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        await ((Basalt.Shell.ViewModels.MainWindowViewModel)w.DataContext!).OpenFileAsync(source);
        await host.SettleAsync(8);

        var panel = PanelOf<TestExplorerPanel>(w, f => f.Tests);
        panel.Show([new TestCase("CalcTests.AddsTwo", project)]);
        var leaf = LeafOf(panel.Roots[0]);
        panel.SelectForTests(leaf);

        SetBreakpointWithF9(w, source, 7);

        // Every stop, in order: the first must be the user's breakpoint, not a break in the test host's own code.
        var stops = new List<Basalt.Core.Services.StackFrame>();
        SessionOf(w).Paused += (_, frame) => stops.Add(frame);

        try
        {
            var debugging = w.DebugSelectedTestForTests();

            // The test host was held, the debugger attached to it, and the breakpoint inside the test is hit.
            await PauseAtAsync(host, 7, source);
            Assert.Contains("AddsTwo", PanelOf<CallStackPanel>(w, f => f.CallStack).Frames[0].Method, StringComparison.Ordinal);

            // Measured: the test host does not break by itself after the attach, so VSTEST_DEBUG_NOBP is not needed.
            Assert.Equal(7, stops[0].Line);
            Assert.Equal(source, stops[0].FilePath);

            await SessionOf(w).ContinueAsync();

            Assert.True(await PumpUntilAsync(host, () => debugging.IsCompleted, TimeSpan.FromSeconds(120)),
                "The test run did not finish after the breakpoint: " + StateOf(w, source));
            await debugging;

            // Read from the tree as it is now: a discovery started by the build may have rebuilt the nodes meanwhile.
            var shown = LeafOf(panel.Roots[0]);
            Assert.True(shown.Outcome == TestOutcome.Passed, $"{shown.Outcome}: {shown.Result?.Message}\n{OutputOf(w)}");
        }
        finally
        {
            await w.StopDebuggingForTests();
        }
    }

    [AvaloniaFact]
    public async Task StopDebuggingBeforeTheHostIsHeardOfEndsTheRunAndAttachesToNothing()
    {
        Assert.SkipWhen(AdapterMissing, "netcoredbg is not installed.");

        var (project, source, _) = await NetCoreDebugTargets.BuildAsync(
            Path.Combine(_root, "tests"), "CalcTests", UserJourneyDebuggerTests2Sources.Tests, UserJourneyDebuggerTests2Sources.TestProject);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        await ((Basalt.Shell.ViewModels.MainWindowViewModel)w.DataContext!).OpenFileAsync(source);
        await host.SettleAsync(8);

        var panel = PanelOf<TestExplorerPanel>(w, f => f.Tests);
        panel.Show([new TestCase("CalcTests.AddsTwo", project)]);
        panel.SelectForTests(LeafOf(panel.Roots[0]));
        SetBreakpointWithF9(w, source, 7);

        var attaches = 0;
        SessionOf(w).Started += (_, _) => attaches++;

        var debugging = w.DebugSelectedTestForTests();

        // The run is under way (building), and no host has been heard of yet: Shift+F5 now.
        Assert.True(await PumpUntilAsync(host,
            () => Field<object?>(w, "_debugTestRun") is not null, TimeSpan.FromSeconds(30)));
        await w.StopDebuggingForTests();

        Assert.True(await PumpUntilAsync(host, () => debugging.IsCompleted, TimeSpan.FromSeconds(60)),
            "Stopping did not end the held test run: " + StateOf(w, source));
        await debugging;

        // Nothing attached afterwards, and the debugger is not left on a host that is gone.
        await host.SettleAsync(10);
        Assert.Equal(0, attaches);
        Assert.False(SessionOf(w).IsRunning);
    }
}
