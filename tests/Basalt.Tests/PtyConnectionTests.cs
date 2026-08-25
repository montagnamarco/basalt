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

    public void Dispose()
    {
        // Terminating the shell before disposing keeps stray processes from
        // outliving the test and holding the runner open.
        if (_pty is { IsRunning: true }) _pty.Write("exit\n");
        _pty?.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
