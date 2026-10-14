using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// A terminal tab that runs a program: how the program's exit reaches the caller, how Stop ends it, and what closing
/// the tab does to it. These use the Unix pseudo-terminal, as the other terminal journeys do, and are skipped on
/// Windows.
/// </summary>
public sealed class ProgramTerminalTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-program-terminal-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A terminal tab running <paramref name="script"/> with <c>/bin/sh</c>, as the run path does with dotnet.</summary>
    private TerminalView OpenProgram(string script) =>
        new(_root, () => ProgramTerminal.Connect("/bin/sh", ["-c", script], _root, null));

    /// <summary>
    /// Lets the interface thread run what is queued until the condition holds. The exit of a program is posted to it,
    /// so a test waiting for that must pump the dispatcher rather than just sleep.
    /// </summary>
    private static async Task<bool> PumpUntilAsync(Func<bool> condition, int milliseconds = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);

        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return true;
            await Task.Delay(25);
        }

        Dispatcher.UIThread.RunJobs();
        return condition();
    }

    [AvaloniaFact]
    public async Task AProgramsExitCodeReachesTheRunAndTheTabSaysSo()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The program runs on a Unix pseudo-terminal here.");

        var view = OpenProgram("echo ready; exit 3");
        var terminal = new ProgramTerminal(view);
        int? exitCode = null;
        terminal.Exited += (_, code) => exitCode = code;

        try
        {
            Assert.True(await PumpUntilAsync(() => exitCode is not null), "the exit was not reported.");

            Assert.Equal(3, exitCode);
            Assert.False(terminal.IsRunning);
            Assert.Contains("[process exited with code 3]", view.ScreenText);
        }
        finally
        {
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task AnExitBeforeTheRunSubscribesIsStillReported()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The program runs on a Unix pseudo-terminal here.");

        // A program that fails at once can exit before the caller has attached its handler, because the window
        // returns the terminal and the caller subscribes on the interface thread afterwards. Blocking here keeps the
        // interface thread from running anything, so the exit is raised while nobody is listening.
        var view = OpenProgram("exit 5");
        var terminal = new ProgramTerminal(view);

        try
        {
            Thread.Sleep(800);

            int? exitCode = null;
            terminal.Exited += (_, code) => exitCode = code;

            Assert.True(await PumpUntilAsync(() => exitCode is not null),
                "an exit raised before the subscription was lost.");
            Assert.Equal(5, exitCode);
        }
        finally
        {
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task StopEndsARunningProgramAndTheTabSaysHowItEnded()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The program runs on a Unix pseudo-terminal here.");

        var view = OpenProgram("sleep 30");
        var terminal = new ProgramTerminal(view);
        int? exitCode = null;
        terminal.Exited += (_, code) => exitCode = code;

        try
        {
            Assert.True(terminal.IsRunning);
            Assert.True(terminal.ProcessId > 0, "the running program has no process id.");

            terminal.Stop();

            Assert.True(await PumpUntilAsync(() => exitCode is not null), "the stopped program did not exit.");
            Assert.NotEqual(0, exitCode);
            Assert.False(terminal.IsRunning);
            Assert.Contains("[process exited with code", view.ScreenText);
        }
        finally
        {
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task ClosingTheTabEndsTheProgramWithoutReportingAnExit()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The program runs on a Unix pseudo-terminal here.");

        var view = OpenProgram("sleep 30");
        var terminal = new ProgramTerminal(view);
        var closed = false;
        var exits = 0;
        terminal.Closed += (_, _) => closed = true;
        terminal.Exited += (_, _) => exits++;

        view.Dispose();

        Assert.True(closed, "closing the tab did not tell the run.");
        Assert.False(terminal.IsRunning);

        // The program was ended by the close, not by itself: no exit is reported for it, now or once it is gone.
        await PumpUntilAsync(() => false, milliseconds: 500);
        Assert.Equal(0, exits);
    }

    [AvaloniaFact]
    public async Task AnExitBeforeTheRunWrapsTheTerminalIsStillReported()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The program runs on a Unix pseudo-terminal here.");

        // The terminal is built and its program exits before anyone has a handle on it: the handle is made only
        // afterwards, as the window does when the tab is ready. The exit must still reach the run.
        var view = OpenProgram("exit 5");

        try
        {
            Thread.Sleep(800);

            var terminal = new ProgramTerminal(view);
            int? exitCode = null;
            terminal.Exited += (_, code) => exitCode = code;

            Assert.True(await PumpUntilAsync(() => exitCode is not null),
                "an exit that came before the handle was made was lost.");
            Assert.Equal(5, exitCode);
            Assert.False(terminal.IsRunning);
        }
        finally
        {
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public void StopNeverThrowsEvenWhenTheTreeCannotBeEnded()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The program runs on a Unix pseudo-terminal here.");

        // Process.Kill(entireProcessTree: true) can fail with an AggregateException when the tree changes while it is
        // being ended. Stop runs on the interface thread for the Stop command, so it must report nothing rather than throw.
        var view = OpenProgram("sleep 30");
        var terminal = new ProgramTerminal(view,
            _ => throw new AggregateException("the process tree changed while it was being ended"));

        try
        {
            terminal.Stop();
        }
        finally
        {
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task ClosingTheTabEndsTheProgramsChildrenToo()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The program runs on a Unix pseudo-terminal here.");

        // The program starts a child in a process group of its own (job control), and waits for it, as "dotnet run"
        // starts the application. The terminal signals only the group it started, so a child in another group survives
        // unless the close ends the whole tree.
        var childFile = Path.Combine(_root, "child.pid");
        var view = OpenProgram("set -m; sleep 300 & echo $! > child.pid; wait");
        _ = new ProgramTerminal(view);

        try
        {
            int? child = null;
            Assert.True(await PumpUntilAsync(() =>
            {
                if (child is not null || !File.Exists(childFile)) return child is not null;
                if (int.TryParse(File.ReadAllText(childFile).Trim(), out var pid)) child = pid;
                return child is not null;
            }), "the program did not start its child.");

            Assert.False(ProcessIsGone(child!.Value), "the child was not running before the close.");

            // Closing waits for the terminal's output to end, and that waits for the child, which holds the terminal
            // open for minutes. So the close runs on a worker and must finish quickly, without the child's help.
            var closing = Task.Run(() => view.Dispose());
            var finished = await Task.WhenAny(closing, Task.Delay(10000)) == closing;

            Assert.True(finished, "closing the tab blocked on a child the program started and never ended.");
            Assert.True(await PumpUntilAsync(() => ProcessIsGone(child.Value), 5000),
                "the program's child kept running after its tab was closed.");
        }
        finally
        {
            view.Dispose();
        }
    }

    /// <summary>True when no process has the id, or it has exited.</summary>
    private static bool ProcessIsGone(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
