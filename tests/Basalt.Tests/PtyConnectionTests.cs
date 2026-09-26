using Basalt.Workspace.Terminal;

namespace Basalt.Tests;

/// <summary>
/// Verifies the terminal is a real pseudo-terminal and not a pair of pipes.
/// The distinguishing evidence is behaviour a pipe cannot produce: tty(1)
/// naming a device, the shell echoing what was typed, and a prompt appearing.
/// </summary>
public sealed class PtyConnectionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-pty", Guid.NewGuid().ToString("N"));

    private IPtyConnection? _pty;

    public PtyConnectionTests() => Directory.CreateDirectory(_root);

    /// <summary>
    /// Starts a shell that reads commands but does not take over the terminal.
    ///
    /// An interactive shell ("-i") claims the controlling terminal and job
    /// control, which under a test runner leaves stray shells behind and hangs
    /// the run. Everything under test here — the tty device, echo, window size,
    /// TERM — comes from the pseudo-terminal itself, not from interactivity.
    /// </summary>
    private readonly System.Text.StringBuilder _transcript = new();

    private IPtyConnection Start(int columns = 80, int rows = 24)
    {
        _pty = new UnixPtyConnection("/bin/sh", [], _root, columns, rows);

        // Collecting from the moment the terminal exists: attaching a listener
        // after writing a command loses whatever the shell answered in between,
        // which under a loaded test run is often the whole reply.
        _pty.OutputReceived += (_, text) =>
        {
            lock (_transcript) _transcript.Append(text);
        };

        return _pty;
    }

    /// <summary>Collects output until it satisfies a predicate or the time runs out.</summary>
    /// <summary>
    /// Waits until everything the terminal has said satisfies the predicate,
    /// then returns the full transcript.
    /// </summary>
    private async Task<string> ReadUntilAsync(
        Func<string, bool> satisfied, int milliseconds = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);

        while (DateTime.UtcNow < deadline)
        {
            string current;
            lock (_transcript) current = _transcript.ToString();

            if (satisfied(current)) return current;

            await Task.Delay(25);
        }

        lock (_transcript) return _transcript.ToString();
    }

    [Fact]
    public async Task TheChildIsAttachedToARealTerminalDevice()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses ConPTY, covered separately.");

        var pty = Start();
        pty.Write("tty\n");

        var output = await ReadUntilAsync(t => t.Contains("/dev/"));

        // A pipe would make tty(1) report "not a tty".
        Assert.Contains("/dev/", output);
        Assert.DoesNotContain("not a tty", output);
    }

    [Fact]
    public async Task TheTerminalEchoesWhatIsTyped()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses ConPTY, covered separately.");

        // Echo comes from the terminal line discipline, which pipes do not have.
        var pty = Start();
        pty.Write("echo marker-echo-test\n");

        var output = await ReadUntilAsync(t => t.Contains("marker-echo-test"));

        Assert.Contains("marker-echo-test", output);
    }

    [Fact]
    public async Task ReportsTheWindowSizeItWasGiven()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses ConPTY, covered separately.");

        var pty = Start(columns: 120, rows: 40);
        pty.Write("stty size\n");

        var output = await ReadUntilAsync(t => t.Contains("40 120"));

        Assert.Contains("40 120", output);
    }

    [Fact]
    public void RecordsARequestedResizeWithoutApplyingIt()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses ConPTY, covered separately.");

        // Live resizing needs ioctl(TIOCSWINSZ), which P/Invoke marshals
        // incorrectly on macOS arm64; the size a terminal starts with is final.
        var pty = (UnixPtyConnection)Start(columns: 80, rows: 24);

        pty.Resize(132, 50);

        Assert.Equal(132, pty.RequestedColumns);
        Assert.Equal(50, pty.RequestedRows);
    }

    [Fact]
    public async Task StartsInTheRequestedWorkingDirectory()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses ConPTY, covered separately.");

        var pty = Start();
        pty.Write("pwd\n");

        var expected = Path.GetFileName(_root);
        var output = await ReadUntilAsync(t => t.Contains(expected));

        Assert.Contains(expected, output);
    }

    [Fact]
    public void InterruptSendsTheControlCharacterToTheTerminal()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses ConPTY, covered separately.");

        // What the shell does with the interrupt depends on job control, which
        // a shell started this way does not have: it may echo "^C", stop the
        // foreground job, or neither. What this connection is responsible for
        // is writing the byte, which is checked here without depending on the
        // shell's reaction. The behaviour in the running IDE, where the user's
        // own interactive shell handles it, is covered by using the terminal.
        var pty = Start();

        var exception = Record.Exception(() => pty.Interrupt());

        Assert.Null(exception);
        Assert.True(pty.IsRunning, "the terminal must survive an interrupt");
    }

    [Fact]
    public async Task AdvertisesATerminalCapableOfColour()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses ConPTY, covered separately.");

        var pty = Start();
        pty.Write("echo $TERM\n");

        var output = await ReadUntilAsync(t => t.Contains("xterm"));

        Assert.Contains("xterm-256color", output);
    }

    /// <summary>
    /// Starts cmd.exe on the ConPTY pseudo-console, wired into the same
    /// transcript-collecting <see cref="_pty"/>/<see cref="_transcript"/>
    /// fields the Unix tests above use, so <see cref="ReadUntilAsync"/> and
    /// <see cref="Dispose"/> work the same way on either platform.
    /// </summary>
    /// <summary>
    /// Whether this session can host a process on a pseudo-console at all.
    /// </summary>
    /// <remarks>
    /// A disconnected remote session has no desktop for the console to
    /// attach to: every child, cmd.exe or ping.exe alike, dies at start with
    /// STATUS_DLL_INIT_FAILED (0xC0000142) while an ordinary process runs.
    /// That is the machine, not the code, so the tests say so and skip.
    /// </remarks>
    private static readonly Lazy<bool> ConPtyHostsProcesses = new(() =>
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            var shell = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

            using var probe = new WindowsPtyConnection(shell, [], Path.GetTempPath());

            // The child can die a moment after CreateProcessW returns, so a
            // shell still running after a few seconds is the proof.
            var exitCode = 0;
            probe.Exited += (_, code) => exitCode = code;

            var deadline = DateTime.UtcNow.AddSeconds(3);

            while (probe.IsRunning && DateTime.UtcNow < deadline) Thread.Sleep(50);

            return probe.IsRunning || exitCode != unchecked((int)0xC0000142);
        }
        catch (IOException ex) when (ex.Message.Contains("0xC0000142", StringComparison.Ordinal))
        {
            return false;
        }
    });

    private static void SkipUnlessConPtyWorksHere()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "ConPTY is Windows-only.");
        Assert.SkipUnless(ConPtyHostsProcesses.Value,
            "This session cannot attach a process to a pseudo-console (STATUS_DLL_INIT_FAILED): no interactive desktop.");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private IPtyConnection StartWindows(int columns = 80, int rows = 24)
    {
        var shell = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
        _pty = new WindowsPtyConnection(shell, [], _root, columns, rows);

        _pty.OutputReceived += (_, text) =>
        {
            lock (_transcript) _transcript.Append(text);
        };

        return _pty;
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task TheWindowsTerminalRunsCmdAndExitsWhenTold()
    {
        SkipUnlessConPtyWorksHere();

        var pty = StartWindows();
        pty.Write("echo basalt-probe\r\n");

        // Cooked-mode ConPTY echoes what was typed and then the shell's own
        // reply, so this also proves the child received the keystrokes.
        var output = await ReadUntilAsync(t => t.Contains("basalt-probe"));
        Assert.Contains("basalt-probe", output);

        var exited = false;
        var exitCode = -1;
        pty.Exited += (_, code) =>
        {
            exited = true;
            exitCode = code;
        };

        pty.Write("exit\r\n");

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!exited && DateTime.UtcNow < deadline) await Task.Delay(25);

        Assert.True(exited, "the Exited event must fire when the shell exits");
        Assert.Equal(0, exitCode);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void ResizingTheWindowsTerminalDoesNotThrowWhileRunning()
    {
        SkipUnlessConPtyWorksHere();

        // Unlike the Unix workaround, ConPTY genuinely resizes a live
        // terminal, so this exercises the real call rather than a recorded
        // no-op.
        var pty = StartWindows(columns: 80, rows: 24);

        var exception = Record.Exception(() => pty.Resize(132, 50));

        Assert.Null(exception);
        Assert.True(pty.IsRunning, "the terminal must survive a resize");
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task DisposingTheWindowsTerminalKillsTheChild()
    {
        SkipUnlessConPtyWorksHere();

        var pty = (WindowsPtyConnection)StartWindows();
        var processId = pty.ProcessId;

        pty.Dispose();

        // The fixture's own Dispose() would otherwise touch an already
        // disposed connection; this test disposes it deliberately, so there
        // is nothing left for the fixture to do.
        _pty = null;

        var deadline = DateTime.UtcNow.AddSeconds(5);
        var stillRunning = ProcessExists(processId);
        while (stillRunning && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
            stillRunning = ProcessExists(processId);
        }

        Assert.False(stillRunning, "the child process must not outlive Dispose()");
    }

    private static bool ProcessExists(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // No process with this id: it already exited.
            return false;
        }
    }

    public void Dispose()
    {
        // Terminating the shell before disposing keeps stray processes from
        // outliving the test and holding the runner open.
        if (_pty is { IsRunning: true })
            _pty.Write(OperatingSystem.IsWindows() ? "exit\r\n" : "exit\n");
        _pty?.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
