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

    /// <summary>
    /// The operating system's id for the child, or -1 when no child was started.
    /// Asked for so a caller can end the program it started, which the
    /// connection itself does not do until it is disposed.
    /// </summary>
    int ProcessId { get; }

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
/// Uses openpty plus posix_spawn rather than forkpty: forking a multi-threaded
/// .NET process and not immediately calling exec can deadlock, because only the
/// forking thread survives in the child while locks held by other threads stay
/// locked forever. The child gets a session of its own; see <see cref="UnixSpawn"/>.
/// </summary>
public sealed class UnixPtyConnection : IPtyConnection
{
    /// <summary>ETX, the byte a terminal sends when Ctrl+C is pressed.</summary>
    private const string InterruptCharacter = "\u0003";

    private readonly int _master;
    private readonly int _worker;
    private readonly ManualResetEventSlim _childEnded = new();
    private readonly CancellationTokenSource _reading = new();
    private readonly Lock _writeGate = new();
    private readonly bool _runsShell;
    private bool _disposed;

    public UnixPtyConnection(
        string shellPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int columns = 80,
        int rows = 24,
        IReadOnlyDictionary<string, string>? environment = null,
        bool runsShell = false)
    {
        _runsShell = runsShell;
        var size = new UnixPty.WinSize { Cols = (ushort)columns, Rows = (ushort)rows };

        if (UnixPty.openpty(out _master, out _worker, IntPtr.Zero, IntPtr.Zero, ref size) != 0)
            throw new IOException(
                $"Could not allocate a pseudo-terminal: {Marshal.GetLastWin32Error()}");

        var childEnvironment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            childEnvironment[(string)entry.Key] = (string?)entry.Value ?? "";

        // TERM tells programs which escape sequences they may emit; xterm-256color
        // is what terminal emulators advertise and what our parser understands.
        childEnvironment["TERM"] = "xterm-256color";
        childEnvironment["COLORTERM"] = "truecolor";
        childEnvironment["LINES"] = rows.ToString();
        childEnvironment["COLUMNS"] = columns.ToString();

        if (environment is not null)
            foreach (var (key, value) in environment) childEnvironment[key] = value;

        try
        {
            ProcessId = UnixSpawn.Start(shellPath, arguments, workingDirectory, childEnvironment, _worker, _master);
        }
        catch
        {
            UnixPty.close(_worker);
            UnixPty.close(_master);
            throw;
        }

        // Waited for on a thread of its own: the child is not a .NET Process, so nothing else collects its status.
        new Thread(WaitForChild) { IsBackground = true, Name = "Terminal child " + ProcessId }.Start();

        // The worker end belongs to the child now: holding it open here would
        // stop us from ever seeing end-of-file when the child exits.
        UnixPty.close(_worker);

        _ = Task.Run(PumpOutputAsync);
    }

    // A disposed connection has released its process: asking it for HasExited
    // throws, so a closed terminal reports itself as not running instead.
    public bool IsRunning => !_disposed && !_childEnded.IsSet;

    /// <summary>The child's id: the program's own, and the id of its session and process group.</summary>
    public int ProcessId { get; }

    /// <summary>Waits for the child to end and reports its exit code.</summary>
    private void WaitForChild()
    {
        int code;

        while (true)
        {
            var result = UnixSpawn.waitpid(ProcessId, out var status, 0);

            if (result == ProcessId)
            {
                code = UnixSpawn.ExitCodeOf(status);
                break;
            }

            // Interrupted by a signal: wait again. Anything else (the status was collected elsewhere) ends it.
            if (result < 0 && Marshal.GetLastPInvokeError() == 4) continue;

            code = -1;
            break;
        }

        EventHandler<int>? handlers;
        lock (_exitGate)
        {
            _exitCode = code;
            handlers = _exited;
        }

        _childEnded.Set();
        handlers?.Invoke(this, code);
    }

    public event EventHandler<string>? OutputReceived;

    private readonly Lock _exitGate = new();
    private EventHandler<int>? _exited;
    private int? _exitCode;

    /// <summary>
    /// Raised once when the child ends. A handler added after that is called at once: a program that ends
    /// straight away can be gone before its tab has subscribed.
    /// </summary>
    public event EventHandler<int>? Exited
    {
        add
        {
            int? already;
            lock (_exitGate)
            {
                _exited += value;
                already = _exitCode;
            }

            if (already is { } code) value?.Invoke(this, code);
        }
        remove
        {
            lock (_exitGate) _exited -= value;
        }
    }

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
    /// Writes the interrupt character into the terminal, which turns it into SIGINT for the foreground job.
    /// </summary>
    /// <remarks>
    /// The child opens the terminal as the leader of its own session, so it is the session's controlling
    /// terminal and the kernel does the rest: SIGINT only when the program has not asked for raw input, as ssh
    /// or a program reading Ctrl+C as a key does. Should a system not hand over the terminal that way (no
    /// foreground group), the signal is sent from here, off the interface thread, to what the foreground job
    /// would be: the program and what it started, or for a shell what the shell started, never the shell.
    /// </remarks>
    public void Interrupt()
    {
        Write(InterruptCharacter);

        if (!IsRunning || UnixPty.tcgetpgrp(_master) > 0) return;

        var root = ProcessId;
        var includeRoot = !_runsShell;
        _ = Task.Run(() =>
        {
            foreach (var target in ForegroundOf(root, includeRoot)) UnixPty.kill(target, UnixPty.SIGINT);
        });
    }

    /// <summary>The process and everything it started, read from ps; the root only when asked for.</summary>
    internal static IReadOnlyList<int> ForegroundOf(int root, bool includeRoot)
    {
        var children = new Dictionary<int, List<int>>();

        try
        {
            using var ps = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ps", ["-A", "-o", "pid=,ppid="])
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            });

            if (ps is null) return includeRoot ? [root] : [];

            var output = ps.StandardOutput.ReadToEnd();
            ps.WaitForExit(2000);

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2 || !int.TryParse(fields[0], out var pid) || !int.TryParse(fields[1], out var parent)) continue;

                if (!children.TryGetValue(parent, out var list)) children[parent] = list = [];
                list.Add(pid);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // Without ps only the program itself can be reached.
        }

        var found = new List<int>();
        if (includeRoot) found.Add(root);

        var pending = new Queue<int>([root]);
        while (pending.TryDequeue(out var next))
        {
            if (!children.TryGetValue(next, out var list)) continue;

            foreach (var child in list.Where(child => !found.Contains(child) && child != root))
            {
                found.Add(child);
                pending.Enqueue(child);
            }
        }

        return found;
    }

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _reading.Cancel();

        // The shell is stopped before the master is closed, not after. The
        // reader is parked inside read() on the master, and on macOS close()
        // of a descriptor another thread is reading waits for that read to
        // return — which, with the shell still alive and silent, is never:
        // closing the IDE with a terminal open hung. Once the shell's group
        // is gone the slave side closes, read() returns, and close() is
        // immediate.
        // The child leads its own group: the shell and the commands in front of it. Jobs in the background keep
        // running, as they do when a real terminal closes. Once the child has ended its id is free again and may
        // name an unrelated group, so nothing is signalled then.
        if (!_childEnded.IsSet && ProcessId != UnixPty.getpgid(Environment.ProcessId))
        {
            UnixPty.kill(-ProcessId, UnixPty.SIGHUP);
            if (!_childEnded.Wait(300)) UnixPty.kill(-ProcessId, UnixPty.SIGKILL);
            _childEnded.Wait(300);
        }

        UnixPty.close(_master);

        _reading.Dispose();
    }
}
