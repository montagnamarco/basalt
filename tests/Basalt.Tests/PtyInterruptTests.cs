using Basalt.Workspace.Terminal;


namespace Basalt.Tests;

/// <summary>
/// Ctrl+C in a terminal stops the program in front: a program run in its own tab, or the command a shell is
/// running, while the shell itself carries on.
/// </summary>
/// <remarks>
/// The pseudo-terminal is no session's controlling terminal, so the terminal driver sends no SIGINT of its own:
/// the ^C was echoed and the program kept running. These tests run real processes and wait for them to end.
/// </remarks>
public sealed class PtyInterruptTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-pty-interrupt-").FullName;

    private IPtyConnection? _pty;

    public void Dispose()
    {
        _pty?.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task CtrlCEndsAProgramRunningInItsOwnTerminal()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses ConPTY, which delivers Ctrl+C itself.");

        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pty = new UnixPtyConnection("sleep", ["30"], _root);
        _pty.Exited += (_, code) => exited.TrySetResult(code);

        await Task.Delay(500);
        Assert.True(_pty.IsRunning, "sleep ended before it was interrupted.");

        _pty.Interrupt();

        var finished = await Task.WhenAny(exited.Task, Task.Delay(5000));
        Assert.True(finished == exited.Task, "Ctrl+C did not end the program.");
    }

    [Fact]
    public async Task CtrlCEndsTheShellsCommandAndLeavesTheShellRunning()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses ConPTY, which delivers Ctrl+C itself.");

        // The command reports its own pid and becomes the sleep, so the test can see that very process end. The
        // echo of typed text proves nothing here; the process does.
        var transcript = new System.Text.StringBuilder();
        _pty = new UnixPtyConnection("/bin/sh", ["-i"], _root, runsShell: true);
        _pty.OutputReceived += (_, text) => { lock (transcript) transcript.Append(text); };

        _pty.Write("sh -c 'echo SLEEPER=$$; exec sleep 30'\n");

        int? sleeper = null;
        Assert.True(await WaitForAsync(() =>
        {
            lock (transcript)
            {
                var match = System.Text.RegularExpressions.Regex.Match(transcript.ToString(), @"SLEEPER=(\d+)");
                if (match.Success) sleeper = int.Parse(match.Groups[1].Value);
                return match.Success;
            }
        }), "the command did not start.");
        await Task.Delay(300);
        Assert.Equal(0, UnixPty.kill(sleeper!.Value, 0));

        _pty.Interrupt();

        Assert.True(await WaitForAsync(() => UnixPty.kill(sleeper.Value, 0) != 0, 5000), "Ctrl+C did not end the command.");
        Assert.True(_pty.IsRunning, "Ctrl+C ended the shell itself.");
    }

    [Fact]
    public async Task AProgramThatReadsCtrlCAsAKeyIsNotInterrupted()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses ConPTY.");

        // With ISIG off, as ssh or a program reading Ctrl+C as a key sets it, Ctrl+C is a byte, not a signal.
        var transcript = new System.Text.StringBuilder();
        _pty = new UnixPtyConnection("/bin/sh", ["-c", "stty -isig; echo READY; sleep 2; echo SURVIVED"], _root);
        _pty.OutputReceived += (_, text) => { lock (transcript) transcript.Append(text); };

        Assert.True(await WaitForAsync(() => { lock (transcript) return transcript.ToString().Contains("READY", StringComparison.Ordinal); }),
            "the program did not start.");

        _pty.Interrupt();

        Assert.True(await WaitForAsync(() => { lock (transcript) return transcript.ToString().Contains("SURVIVED", StringComparison.Ordinal); }),
            "Ctrl+C interrupted a program that had turned interrupts off: " + transcript);
    }

    [Fact]
    public async Task ClosingAShellWithACommandRunningLeavesThisProcessAlone()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses ConPTY.");

        // A shell passes the SIGHUP of its closing on to the group its commands run in. That group was the IDE's
        // own, and closing such a tab ended the IDE (status 129). This test process plays the IDE: if the signal
        // reached it, the run would end here rather than fail.
        var pty = new UnixPtyConnection("/bin/sh", ["-i"], _root, runsShell: true);
        pty.Write("sleep 30\n");
        await Task.Delay(800);

        // Its own group, measured while it runs: a dead pid has no group to compare.
        Assert.NotEqual(UnixPty.getpgid(pty.ProcessId), UnixPty.getpgid(Environment.ProcessId));

        pty.Dispose();
        await Task.Delay(500);
    }

    [Fact]
    public async Task AShellThatExitsWithAJobInTheBackgroundLeavesThisProcessAlone()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses ConPTY.");

        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pty = new UnixPtyConnection("/bin/sh", ["-i"], _root, runsShell: true);
        _pty.Exited += (_, code) => exited.TrySetResult(code);

        _pty.Write("sleep 30 &\n");
        await Task.Delay(500);
        _pty.Write("exit\n");

        var finished = await Task.WhenAny(exited.Task, Task.Delay(5000));
        Assert.True(finished == exited.Task, "the shell did not exit.");
    }

    [Fact]
    public async Task TheTerminalIsTheControllingTerminalOfItsChild()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses ConPTY.");

        // A program reading /dev/tty finds the terminal only when it is the session's controlling terminal.
        var transcript = new System.Text.StringBuilder();
        _pty = new UnixPtyConnection("/bin/sh", ["-c", "echo tty-ok > /dev/tty && echo opened; sleep 5"], _root);
        _pty.OutputReceived += (_, text) => { lock (transcript) transcript.Append(text); };

        Assert.True(await WaitForAsync(() => { lock (transcript) return transcript.ToString().Contains("tty-ok", StringComparison.Ordinal); }),
            "/dev/tty could not be opened: " + transcript);
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, int milliseconds = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(50);
        }

        return condition();
    }
}
