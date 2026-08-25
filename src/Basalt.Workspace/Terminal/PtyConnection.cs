using System.Runtime.InteropServices;
using System.Text;

namespace Basalt.Workspace.Terminal;

/// <summary>
/// A live pseudo-terminal: the child process believes it is attached to a real
/// terminal, so shells print prompts, programs emit colour, and full-screen
/// applications such as vim and top work.
/// </summary>
public interface IPtyConnection : IDisposable
{
    bool IsRunning { get; }

    /// <summary>Raw text produced by the child, ANSI escape sequences included.</summary>
    event EventHandler<string>? OutputReceived;

    event EventHandler<int>? Exited;

    /// <summary>Sends keystrokes to the terminal exactly as typed.</summary>
    void Write(string text);

    /// <summary>Tells the child its window changed size, so it can redraw.</summary>
    void Resize(int columns, int rows);

    /// <summary>Sends an interrupt, the equivalent of pressing Ctrl+C.</summary>
    void Interrupt();
}

/// <summary>
/// Pseudo-terminal for macOS and Linux.
///
/// Uses openpty plus a normal process launch rather than forkpty: forking a
/// multi-threaded .NET process and not immediately calling exec can deadlock,
/// because only the forking thread survives in the child while locks held by
/// other threads stay locked forever.
/// </summary>
public sealed class UnixPtyConnection : IPtyConnection
{
    /// <summary>ETX, the byte a terminal sends when Ctrl+C is pressed.</summary>
    private const string InterruptCharacter = "\u0003";

    private readonly int _master;
    private readonly int _worker;
    private readonly System.Diagnostics.Process _process;
    private readonly CancellationTokenSource _reading = new();
    private readonly Lock _writeGate = new();
    private bool _disposed;

    public UnixPtyConnection(
        string shellPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int columns = 80,
        int rows = 24,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var size = new UnixPty.WinSize { Cols = (ushort)columns, Rows = (ushort)rows };

        if (UnixPty.openpty(out _master, out _worker, IntPtr.Zero, IntPtr.Zero, ref size) != 0)
            throw new IOException(
                $"Could not allocate a pseudo-terminal: {Marshal.GetLastWin32Error()}");

        var startInfo = new System.Diagnostics.ProcessStartInfo(shellPath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        // TERM tells programs which escape sequences they may emit; xterm-256color
        // is what terminal emulators advertise and what our parser understands.
        startInfo.Environment["TERM"] = "xterm-256color";
        startInfo.Environment["COLORTERM"] = "truecolor";
        startInfo.Environment["LINES"] = rows.ToString();
        startInfo.Environment["COLUMNS"] = columns.ToString();

        if (environment is not null)
            foreach (var (key, value) in environment) startInfo.Environment[key] = value;

        RedirectToWorkerTerminal(startInfo);

        _process = new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.Exited += (_, _) => Exited?.Invoke(this, _process.ExitCode);
        _process.Start();

        // The worker end belongs to the child now: holding it open here would
        // stop us from ever seeing end-of-file when the child exits.
        UnixPty.close(_worker);

        _ = Task.Run(PumpOutputAsync);
    }

    public bool IsRunning => !_process.HasExited;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<int>? Exited;

    public void Write(string text)
    {
        if (!IsRunning) return;

        var bytes = Encoding.UTF8.GetBytes(text);
        lock (_writeGate) UnixPty.write(_master, bytes, bytes.Length);
    }

    /// <summary>
    /// Records the requested size without applying it to the running terminal.
    ///
    /// Resizing needs ioctl(TIOCSWINSZ), and ioctl is variadic: P/Invoke passes
    /// its payload incorrectly on macOS arm64, so the call reports success while
    /// the kernel reads the wrong bytes. An equivalent C program applies the
    /// size correctly, which places the fault in the interop layer rather than
    /// in the call itself. Rather than fail silently, the size a terminal was
    /// created with stays in effect for its lifetime.
    /// </summary>
    public void Resize(int columns, int rows)
    {
        RequestedColumns = columns;
        RequestedRows = rows;
    }

    /// <summary>Size last requested; the live terminal keeps its initial size.</summary>
    public int RequestedColumns { get; private set; }

    public int RequestedRows { get; private set; }

    /// <summary>
    /// Writes the interrupt character into the terminal rather than signalling
    /// the shell directly, so the foreground job receives it, which is what
    /// pressing Ctrl+C in a terminal actually does.
    /// </summary>
    public void Interrupt() => Write(InterruptCharacter);

    private async Task PumpOutputAsync()
    {
        var buffer = new byte[8192];
        var decoder = Encoding.UTF8.GetDecoder();
        var characters = new char[8192];

        while (!_reading.IsCancellationRequested)
        {
            nint count;
            try
            {
                count = await Task.Run(() => UnixPty.read(_master, buffer, buffer.Length))
                    .ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            // Zero or negative means the child closed its end of the terminal.
            if (count <= 0) break;

            // A multi-byte character can straddle two reads, so the decoder is
            // kept across iterations instead of decoding each buffer alone.
            var produced = decoder.GetChars(buffer, 0, (int)count, characters, 0);
            if (produced > 0) OutputReceived?.Invoke(this, new string(characters, 0, produced));
        }
    }

    /// <summary>
    /// Points the child standard streams at the worker end of the terminal.
    ///
    /// .NET offers no supported way to hand a raw file descriptor to a child,
    /// so /bin/sh performs the redirection with its own syntax and then replaces
    /// itself with the real command, leaving no extra process behind.
    /// </summary>
    private void RedirectToWorkerTerminal(System.Diagnostics.ProcessStartInfo startInfo)
    {
        var command = startInfo.FileName;
        var arguments = startInfo.ArgumentList.ToList();
        var quoted = string.Join(' ', new[] { command }.Concat(arguments).Select(Quote));

        startInfo.FileName = "/bin/sh";
        startInfo.ArgumentList.Clear();
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add($"exec {quoted} <&{_worker} >&{_worker} 2>&{_worker}");
    }

    private static string Quote(string value) =>
        value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is '/' or '.' or '-' or '_')
            ? value
            : "'" + value.Replace("'", "'\\''") + "'";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _reading.Cancel();

        // Closing the master first is what a terminal emulator does when its
        // window goes away: the child sees end-of-input and exits on its own.
        // It also unblocks the reader, which is otherwise parked inside read().
        UnixPty.close(_master);

        try
        {
            if (!_process.HasExited && !_process.WaitForExit(300))
            {
                // Signalling the group, not the pid: /bin/sh launched the shell
                // through exec, so the shell may sit in a different process than
                // the one .NET is holding. A negative pid means "the group".
                var group = UnixPty.getpgid(_process.Id);
                var target = group > 0 ? -group : _process.Id;

                UnixPty.kill(target, UnixPty.SIGHUP);
                if (!_process.WaitForExit(300)) UnixPty.kill(target, UnixPty.SIGKILL);

                // Last resort, so a stuck child never outlives the IDE.
                if (!_process.WaitForExit(300)) _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or SystemException)
        {
            // Already gone.
        }

        _process.Dispose();
        _reading.Dispose();
    }
}
