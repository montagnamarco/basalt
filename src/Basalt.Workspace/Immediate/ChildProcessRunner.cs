using System.Diagnostics;
using System.Text;

namespace Basalt.Workspace.Immediate;

/// <summary>What a child process did within the time and output it was given.</summary>
public sealed record ChildProcessResult(
    int ProcessId,
    int? ExitCode,
    bool TimedOut,
    string Output,
    string Error,
    bool Truncated = false,
    bool OutputIncomplete = false);

/// <summary>
/// Runs a process to completion with its output captured and a deadline.
///
/// The Immediate window runs code the user typed, which can loop forever or
/// print without end. Past the deadline, or when the output reaches its cap,
/// the whole tree is killed (a child of the child would otherwise outlive it
/// and keep the pipes open).
///
/// Output is read in raw chunks and never held beyond the cap: reading a line
/// at a time would buffer a line that never ends, which is exactly what
/// <c>Console.Write</c> in a loop produces.
/// </summary>
public static class ChildProcessRunner
{
    /// <summary>The most characters kept of each of output and error.</summary>
    public const int CaptureLimit = 64 * 1024;

    public static async Task<ChildProcessResult> RunAsync(
        ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken ct = default)
    {
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardInput = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;

        using var process = new Process { StartInfo = startInfo };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);

        process.Start();
        ChildProcessRegistry.Track(process);

        try
        {
            var processId = process.Id;
            var truncated = false;

            // Nothing will be typed: closed at once, a ReadLine in the line
            // being evaluated returns instead of waiting out the deadline.
            process.StandardInput.Close();

            void CapReached()
            {
                truncated = true;
                stop.Cancel();
            }

            var output = new Capture();
            var error = new Capture();
            var pumps = Task.WhenAll(
                output.PumpAsync(process.StandardOutput, CapReached),
                error.PumpAsync(process.StandardError, CapReached));

            stop.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(stop.Token).ConfigureAwait(false);

                // The process is done, but a child of it may still hold the
                // pipes open (it started something and left). That is not a
                // timeout: the exit code stands, and the output is what had
                // arrived by then.
                var incomplete = false;

                try
                {
                    await pumps.WaitAsync(TimeSpan.FromSeconds(5), stop.Token).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    incomplete = true;
                }

                return new ChildProcessResult(
                    processId, process.ExitCode, false, output.Text, error.Text, truncated, incomplete);
            }
            catch (OperationCanceledException)
            {
                Kill(process);

                // Cancelled by the user: say so by cancelling too. A deadline
                // or a cap is an answer, not an error.
                ct.ThrowIfCancellationRequested();

                await WaitAfterKillAsync(process, pumps).ConfigureAwait(false);

                return new ChildProcessResult(
                    processId, null, !truncated, output.Text, error.Text, truncated);
            }
        }
        catch (OperationCanceledException)
        {
            // Also covers the user cancelling while the process was starting up.
            Kill(process);
            throw;
        }
        finally
        {
            ChildProcessRegistry.Untrack(process);
        }
    }

    internal static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Exited between the check and the kill.
        }
    }

    private static async Task WaitAfterKillAsync(Process process, Task pumps)
    {
        // Bounded: a grandchild that escaped the tree kill must not hold the
        // window up through the pipes it inherited.
        using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            await pumps.WaitAsync(grace.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class Capture
    {
        private readonly StringBuilder _text = new();
        private bool _truncated;

        public async Task PumpAsync(StreamReader reader, Action onLimit)
        {
            var buffer = new char[4096];

            try
            {
                while (true)
                {
                    var read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);

                    if (read == 0) return;

                    lock (_text)
                    {
                        var room = CaptureLimit - _text.Length;
                        _text.Append(buffer, 0, Math.Min(read, room));

                        if (read <= room) continue;

                        _truncated = true;
                    }

                    onLimit();
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The process was killed under the read.
            }
        }

        public string Text
        {
            get
            {
                lock (_text) return _truncated ? _text + "\n…" : _text.ToString().TrimEnd('\r', '\n');
            }
        }
    }
}

/// <summary>
/// The child processes that are running now, so that none outlives the IDE.
///
/// A line that never ends would otherwise keep running after the window that
/// asked for it was closed: the deadline lives in the IDE, and the IDE is gone.
/// </summary>
public static class ChildProcessRegistry
{
    private static readonly HashSet<Process> Running = [];

    static ChildProcessRegistry() => AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAll();

    internal static void Track(Process process)
    {
        lock (Running) Running.Add(process);
    }

    internal static void Untrack(Process process)
    {
        lock (Running) Running.Remove(process);
    }

    /// <summary>The ids of the children alive, for the tests.</summary>
    internal static IReadOnlyList<int> ProcessIds
    {
        get { lock (Running) return [.. Running.Select(p => p.Id)]; }
    }

    /// <summary>How many children are alive, for the tests.</summary>
    public static int Count
    {
        get { lock (Running) return Running.Count; }
    }

    /// <summary>Stops every child still running. Called when the IDE exits.</summary>
    public static void KillAll()
    {
        Process[] all;
        lock (Running) all = [.. Running];

        foreach (var process in all) ChildProcessRunner.Kill(process);
    }
}
