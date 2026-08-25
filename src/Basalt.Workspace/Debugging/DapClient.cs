using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Basalt.Workspace.Debugging;

/// <summary>
/// Talks to a debug adapter over the Debug Adapter Protocol.
///
/// Messages are JSON with a Content-Length header, the same framing the
/// Language Server Protocol uses. This class knows the framing and the
/// request/response pairing; what the messages mean is the caller's business.
/// </summary>
public sealed class DapClient : IDisposable
{
    private readonly Process _process;
    private readonly Lock _writeGate = new();

    private readonly Dictionary<int, TaskCompletionSource<JsonObject>> _pending = [];
    private readonly Lock _pendingGate = new();

    private readonly CancellationTokenSource _reading = new();
    private int _sequence;
    private bool _disposed;

    public DapClient(string adapterPath, string arguments = "--interpreter=vscode")
    {
        var startInfo = new ProcessStartInfo(adapterPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            startInfo.ArgumentList.Add(argument);

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.Exited += (_, _) => Exited?.Invoke(this, _process.ExitCode);

        _process.Start();

        _ = Task.Run(() => ReadLoopAsync(_reading.Token));
    }

    public bool IsRunning => !_process.HasExited;

    /// <summary>Something the adapter reported without being asked.</summary>
    public event EventHandler<DapEvent>? EventReceived;

    /// <summary>The adapter process ended.</summary>
    public event EventHandler<int>? Exited;

    /// <summary>
    /// Sends a request and waits for its response.
    ///
    /// Responses are matched by sequence number rather than by order, because
    /// an adapter is free to answer out of order and to interleave events.
    /// </summary>
    public async Task<JsonObject> SendAsync(
        string command, JsonObject? arguments = null, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _sequence);

        var request = new JsonObject
        {
            ["seq"] = id,
            ["type"] = "request",
            ["command"] = command
        };

        if (arguments is not null) request["arguments"] = arguments;

        var waiter = new TaskCompletionSource<JsonObject>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_pendingGate) _pending[id] = waiter;

        Write(request);

        await using var registration = ct.Register(() => waiter.TrySetCanceled(ct));
        return await waiter.Task.ConfigureAwait(false);
    }

    /// <summary>Sends a request without waiting for its response.</summary>
    public void Send(string command, JsonObject? arguments = null)
    {
        var request = new JsonObject
        {
            ["seq"] = Interlocked.Increment(ref _sequence),
            ["type"] = "request",
            ["command"] = command
        };

        if (arguments is not null) request["arguments"] = arguments;

        Write(request);
    }

    private void Write(JsonObject message)
    {
        if (!IsRunning) return;

        var payload = Encoding.UTF8.GetBytes(message.ToJsonString());
        var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");

        lock (_writeGate)
        {
            try
            {
                var stream = _process.StandardInput.BaseStream;
                stream.Write(header);
                stream.Write(payload);
                stream.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The adapter went away between the check and the write.
            }
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var stream = _process.StandardOutput.BaseStream;

        while (!ct.IsCancellationRequested)
        {
            JsonObject? message;

            try
            {
                message = await ReadMessageAsync(stream, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException
                                          or OperationCanceledException)
            {
                break;
            }

            if (message is null) break;

            Dispatch(message);
        }

        // Anything still waiting will never be answered now.
        lock (_pendingGate)
        {
            foreach (var waiter in _pending.Values)
                waiter.TrySetException(new IOException("The debug adapter closed."));

            _pending.Clear();
        }
    }

    private void Dispatch(JsonObject message)
    {
        var type = message["type"]?.GetValue<string>();

        if (type == "event")
        {
            EventReceived?.Invoke(this, new DapEvent(
                message["event"]?.GetValue<string>() ?? "",
                message["body"] as JsonObject));
            return;
        }

        if (type != "response") return;

        var requestSeq = message["request_seq"]?.GetValue<int>() ?? -1;

        TaskCompletionSource<JsonObject>? waiter;
        lock (_pendingGate)
        {
            if (!_pending.Remove(requestSeq, out waiter)) return;
        }

        waiter.TrySetResult(message);
    }

    /// <summary>
    /// Reads one Content-Length framed message.
    ///
    /// The header is read a byte at a time because its length is not known in
    /// advance and over-reading would consume the body of the message.
    /// </summary>
    private static async Task<JsonObject?> ReadMessageAsync(Stream stream, CancellationToken ct)
    {
        var header = new StringBuilder();
        var single = new byte[1];

        while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(single.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (read == 0) return null;

            header.Append((char)single[0]);

            // A malformed stream must not grow the buffer without bound.
            if (header.Length > 8192) return null;
        }

        var length = ParseContentLength(header.ToString());
        if (length <= 0) return null;

        var payload = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var read = await stream
                .ReadAsync(payload.AsMemory(offset, length - offset), ct)
                .ConfigureAwait(false);

            if (read == 0) return null;
            offset += read;
        }

        try
        {
            return JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ParseContentLength(string header)
    {
        foreach (var line in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) continue;

            return int.TryParse(line["Content-Length:".Length..].Trim(), out var length)
                ? length
                : -1;
        }

        return -1;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _reading.Cancel();

        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(1000)) _process.Kill(entireProcessTree: true);
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

/// <summary>Something the adapter reported on its own.</summary>
public sealed record DapEvent(string Name, JsonObject? Body);
