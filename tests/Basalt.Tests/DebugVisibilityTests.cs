using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Debugging;

namespace Basalt.Tests;

/// <summary>
/// Whether the window says that a debugging session is under way.
///
/// Pressing Start used to build and then, if the debugger was missing, write
/// one line into a panel that was behind another one: from the outside the
/// key did nothing at all. These cover the parts that make a session visible.
/// </summary>
public class DebugVisibilityTests
{
    [AvaloniaFact]
    public void HidesTheBannerWhileNothingIsBeingDebugged()
    {
        // A strip that is always there stops being read exactly when it
        // starts meaning something.
        using var host = new TestWindow();

        var banner = host.Window.FindControl<Border>("DebugBanner")!;

        Assert.False(banner.IsVisible);
    }

    [AvaloniaFact]
    public void ShowsTheBannerAsSoonAsStartingBegins()
    {
        using var host = new TestWindow();

        host.Window.ShowStartingBannerForTests("MyApp.dll");

        var banner = host.Window.FindControl<Border>("DebugBanner")!;
        var text = host.Window.FindControl<TextBlock>("DebugBannerText")!;

        Assert.True(banner.IsVisible);
        Assert.Contains("Debugging", text.Text ?? "");
    }

    [AvaloniaFact]
    public void NamesWhatIsBeingDebuggedInTheBanner()
    {
        // The name is what tells the user the right thing is starting.
        using var host = new TestWindow();

        host.Window.ShowStartingBannerForTests("MyApp.dll");

        var detail = host.Window.FindControl<TextBlock>("DebugBannerDetail")!;

        Assert.Equal("MyApp.dll", detail.Text);
    }

    [AvaloniaFact]
    public void TakesTheBannerAwayWhenTheSessionIsOver()
    {
        using var host = new TestWindow();
        host.Window.ShowStartingBannerForTests("MyApp.dll");

        // Nothing is running, so settling the state must clear it rather than
        // leave a banner for a session that never began.
        host.Window.ShowDebugStateForTests();

        var banner = host.Window.FindControl<Border>("DebugBanner")!;

        Assert.False(banner.IsVisible);
    }

    [AvaloniaFact]
    public void OffersAWayToStopFromTheBannerItself()
    {
        // Stopping is the one thing wanted while looking at the banner.
        using var host = new TestWindow();

        var stop = host.Window.FindControl<Button>("DebugBannerStop");

        Assert.NotNull(stop);
    }
}

/// <summary>The toolbar during a debugging session.</summary>
public class DebugToolbarTests
{
    [AvaloniaFact]
    public void GreysTheSteppingButtonsWhileNothingIsStopped()
    {
        // Stepping means nothing until the program is stopped; the buttons
        // were lit anyway, so pressing one did nothing and looked broken.
        var paused = false;

        var toolbar = new IdeToolbar();

        toolbar.Show([
            new ToolbarAction(IconKind.StepOver, "Step Over", () => { })
                { IsAvailable = () => paused }
        ]);

        Assert.False(toolbar.IsButtonEnabled("Step Over"));

        paused = true;
        toolbar.RefreshAvailability();

        Assert.True(toolbar.IsButtonEnabled("Step Over"));
    }
}

/// <summary>What the session says when it cannot debug at all.</summary>
public class MissingDebuggerTests
{
    /// <summary>
    /// Whether this machine has a debugger installed.
    ///
    /// These cover the case where it has none. Skipping them where one is
    /// present would leave a test that quietly asserts nothing, so the ones
    /// that need the absence say so and the rest run either way.
    /// </summary>
    private static bool NoDebuggerHere => DebugAdapterLocator.Find() is null;

    [Fact]
    public async Task SaysWhereToPutTheDebuggerWhenItIsNotInstalled()
    {
        // Naming what is missing and stopping there leaves the user with a
        // name and nowhere to go.
        Assert.SkipUnless(NoDebuggerHere, "a debugger is installed here");

        using var session = new DebugSession { MarshalToInterface = true };

        var said = "";
        session.Failed += (_, message) => said = message;

        await session.StartAsync("/nowhere/MyApp.dll");

        Assert.Contains("netcoredbg", said);
        Assert.Contains("PATH", said);
        Assert.Contains("BASALT_NETCOREDBG", said);
    }

    [Fact]
    public async Task LeavesNothingRunningWhenTheDebuggerIsMissing()
    {
        // The banner and the buttons read this: a session that half-started
        // would light them for a debugger that is not there.
        Assert.SkipUnless(NoDebuggerHere, "a debugger is installed here");

        using var session = new DebugSession { MarshalToInterface = true };

        await session.StartAsync("/nowhere/MyApp.dll");

        Assert.False(session.IsRunning);
    }

    [Fact]
    public void LooksForTheDebuggerUnderTheRuntimeItIsRunningOn()
    {
        // A copy put in the folder the message names has to be a copy the
        // locator then looks in: the message and the search are one promise,
        // and they are written in two different files.
        var rid = DebugAdapterLocator.RuntimeIdentifier;

        Assert.Matches("^(osx|win|linux)-(arm64|x64|x86)$", rid);
    }
}

/// <summary>
/// Debugging a program that was built and is really run.
///
/// The panels and the banner all live on the interface thread, while the
/// debug adapter answers on a reader thread of its own. Every test that
/// raised the events by hand passed while the real thing did nothing, so
/// these go through a compiled program and a real adapter.
/// </summary>
public class RealDebugSessionTests
{
    /// <summary>A tiny program built once, or null where the SDK cannot.</summary>
    private static string? BuildProbe(string name, string body)
    {
        if (DebugAdapterLocator.Find() is null) return null;

        var dir = Path.Combine(Path.GetTempPath(), "basalt-debug-" + name);

        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, $"{name}.vbproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <DebugType>portable</DebugType>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(dir, "Program.vb"), body);

        var build = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "dotnet", $"build \"{dir}\" -v q --nologo")
        { RedirectStandardOutput = true, RedirectStandardError = true });

        if (build is null) return null;

        build.WaitForExit(180_000);

        return build.ExitCode == 0 ? dir : null;
    }

    [AvaloniaFact]
    public async Task StopsOnSubMainOfAProgramThatNeverEndsByItself()
    {
        // The case that was reported: a breakpoint on Sub Main of an app with
        // a window. It bound and was hit all along — the debugger said so —
        // but the news arrived on the reader thread and nothing was drawn.
        var dir = BuildProbe("submain",
            """
            Module Program
                Sub Main()
                    Console.WriteLine("started")
                    Threading.Thread.Sleep(Threading.Timeout.Infinite)
                End Sub
            End Module
            """);

        Assert.SkipWhen(dir is null, "no debugger or no SDK here");

        using var session = new DebugSession { MarshalToInterface = true };

        var paused = new TaskCompletionSource<Basalt.Core.Services.StackFrame>();
        session.Paused += (_, f) => paused.TrySetResult(f);

        await session.ToggleBreakpointAsync(Path.Combine(dir!, "Program.vb"), 2);

        await session.StartAsync(
            Path.Combine(dir!, "bin/Debug/net10.0/submain.dll"), dir);

        var hit = await Task.WhenAny(paused.Task, Task.Delay(60_000));

        try
        {
            Assert.True(hit == paused.Task, "the breakpoint on Sub Main was never reported");
            Assert.Equal(2, (await paused.Task).Line);
        }
        finally
        {
            await session.StopAsync();
        }
    }

    [AvaloniaFact]
    public async Task CarriesOnFromABreakpointAndStopsAtTheNextOne()
    {
        // Resuming is what a breakpoint is for: stopping once and having no
        // way onwards but the keyboard is half a debugger.
        var dir = BuildProbe("resume",
            """
            Module Program
                Sub Main()
                    Dim a = 1
                    Dim b = 2
                    Threading.Thread.Sleep(Threading.Timeout.Infinite)
                End Sub
            End Module
            """);

        Assert.SkipWhen(dir is null, "no debugger or no SDK here");

        using var session = new DebugSession { MarshalToInterface = true };

        var stops = new List<int>();
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();

        session.Paused += (_, f) =>
        {
            stops.Add(f.Line);

            if (stops.Count == 1) first.TrySetResult();
            else second.TrySetResult();
        };

        var source = Path.Combine(dir!, "Program.vb");

        await session.ToggleBreakpointAsync(source, 3);
        await session.ToggleBreakpointAsync(source, 4);

        await session.StartAsync(
            Path.Combine(dir!, "bin/Debug/net10.0/resume.dll"), dir);

        try
        {
            Assert.True(await Task.WhenAny(first.Task, Task.Delay(60_000)) == first.Task,
                "never stopped at the first breakpoint");

            await session.ContinueAsync();

            Assert.True(await Task.WhenAny(second.Task, Task.Delay(60_000)) == second.Task,
                "continuing did not reach the second breakpoint");

            Assert.Equal([3, 4], stops);
        }
        finally
        {
            await session.StopAsync();
        }
    }

    [AvaloniaFact]
    public async Task BreaksIntoAProgramThatWouldNeverStopOnItsOwn()
    {
        // Without this a program with no breakpoint in reach — one sitting on
        // its window — can only be killed, never looked at.
        // Prints first, so the test can wait for the program to be running
        // its own code rather than for the runtime to have been loaded.
        var dir = BuildProbe("breakin",
            """
            Module Program
                Sub Main()
                    Console.WriteLine("in main")
                    Threading.Thread.Sleep(Threading.Timeout.Infinite)
                End Sub
            End Module
            """);

        Assert.SkipWhen(dir is null, "no debugger or no SDK here");

        using var session = new DebugSession { MarshalToInterface = true };

        var inMain = new TaskCompletionSource();
        var paused = new TaskCompletionSource();

        session.OutputReceived += (_, t) =>
        {
            if (t.Contains("in main")) inMain.TrySetResult();
        };

        session.Paused += (_, _) => paused.TrySetResult();

        await session.StartAsync(
            Path.Combine(dir!, "bin/Debug/net10.0/breakin.dll"), dir);

        try
        {
            // "Started" says the runtime is up, which comes while modules are
            // still loading: there is no managed code to interrupt yet, and
            // pausing then is refused. Its own output is the proof it is in
            // Main and can be broken into.
            Assert.True(await Task.WhenAny(inMain.Task, Task.Delay(60_000)) == inMain.Task,
                "the program never reached Main");

            Assert.True(session.CanPause, "a running .NET program should be interruptible");

            await session.PauseAsync();

            Assert.True(await Task.WhenAny(paused.Task, Task.Delay(60_000)) == paused.Task,
                "breaking in never stopped the program");
        }
        finally
        {
            await session.StopAsync();
        }
    }

    [AvaloniaFact]
    public async Task SaysAProgramIsRunningEvenWhenItNeverStops()
    {
        // Without this the banner sat on "starting" for a program that had
        // started perfectly well: nothing else is ever said about one that
        // holds a window open.
        var dir = BuildProbe("running",
            """
            Module Program
                Sub Main()
                    Threading.Thread.Sleep(Threading.Timeout.Infinite)
                End Sub
            End Module
            """);

        Assert.SkipWhen(dir is null, "no debugger or no SDK here");

        using var session = new DebugSession { MarshalToInterface = true };

        var started = new TaskCompletionSource();
        session.Started += (_, _) => started.TrySetResult();

        await session.StartAsync(
            Path.Combine(dir!, "bin/Debug/net10.0/running.dll"), dir);

        var up = await Task.WhenAny(started.Task, Task.Delay(60_000));

        try
        {
            Assert.True(up == started.Task, "nothing ever said the program was running");
        }
        finally
        {
            await session.StopAsync();
        }
    }
}
