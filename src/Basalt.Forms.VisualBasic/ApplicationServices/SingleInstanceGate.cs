using System.Collections.ObjectModel;
using System.IO.Pipes;

namespace Microsoft.VisualBasic.ApplicationServices;

/// <summary>
/// Lets one WindowsFormsApplicationBase per application id become "the" running
/// instance and forwards later launches' command lines to it, using only BCL
/// primitives: an exclusive lock file names the primary instance (self-healing,
/// since the OS drops the lock when that process dies, crash or not) and a
/// named pipe carries a later launch's arguments to it.
/// </summary>
/// <remarks>
/// Internal, not part of My.Application's public contract: WindowsFormsApplicationBase
/// exposes only IsSingleInstance. Tests reach this through an internal id override
/// so two instances can run single-instance checks against each other inside one
/// test process without colliding with other tests or a real installation.
/// </remarks>
internal sealed class SingleInstanceGate : IDisposable
{
    private FileStream? _lockStream;
    private NamedPipeServerStream? _activeServer;
    private CancellationTokenSource? _listening;

    /// <summary>
    /// Tries to become the primary instance for <paramref name="id"/>. Returns true
    /// if this call made the caller primary; false if another instance already
    /// owns the id, in which case the own command line was forwarded to it.
    /// </summary>
    public bool TryBecomePrimary(string id, ReadOnlyCollection<string> ownCommandLine, Action<ReadOnlyCollection<string>> onForwardedCommandLine)
    {
        var lockPath = LockFilePath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);

        try
        {
            // FileShare.None: exactly one process can hold this handle at a time;
            // closing it (normally or because the process died) releases it.
            _lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            ForwardToPrimary(id, ownCommandLine);
            return false;
        }

        StartListening(id, onForwardedCommandLine);
        return true;
    }

    private static void ForwardToPrimary(string id, ReadOnlyCollection<string> ownCommandLine)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName(id), PipeDirection.Out);
            client.Connect(5000);

            using var writer = new StreamWriter(client);
            foreach (var argument in ownCommandLine) writer.WriteLine(argument);
            writer.Flush();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            throw new CantStartSingleInstanceException("Could not forward the command line to the running instance.", ex);
        }
    }

    /// <summary>
    /// Creates the next listening pipe instance synchronously (so a client that
    /// connects right after TryBecomePrimary returns always finds it there),
    /// then waits for a connection on the thread pool.
    /// </summary>
    private void StartListening(string id, Action<ReadOnlyCollection<string>> onForwardedCommandLine)
    {
        _listening = new CancellationTokenSource();
        AcceptNext(id, onForwardedCommandLine, _listening.Token);
    }

    private void AcceptNext(string id, Action<ReadOnlyCollection<string>> onForwardedCommandLine, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;

        var server = new NamedPipeServerStream(PipeName(id), PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        _activeServer = server;

        server.BeginWaitForConnection(result => OnConnected(server, result, id, onForwardedCommandLine, token), null);
    }

    private void OnConnected(NamedPipeServerStream server, IAsyncResult result, string id, Action<ReadOnlyCollection<string>> onForwardedCommandLine, CancellationToken token)
    {
        try
        {
            server.EndWaitForConnection(result);
        }
        catch (Exception)
        {
            server.Dispose();
            return;
        }

        // Listen for the next client right away, before reading this one, so the
        // pipe never looks closed to a forwarder that calls in while we are busy.
        if (!token.IsCancellationRequested) AcceptNext(id, onForwardedCommandLine, token);

        try
        {
            using var reader = new StreamReader(server);
            var lines = new List<string>();
            string? line;
            while ((line = reader.ReadLine()) != null) lines.Add(line);

            onForwardedCommandLine(new ReadOnlyCollection<string>(lines));
        }
        catch (IOException)
        {
            // The forwarder disconnected mid-write: nothing more to deliver.
        }
        finally
        {
            server.Dispose();
        }
    }

    public void Dispose()
    {
        _listening?.Cancel();
        _activeServer?.Dispose();
        _lockStream?.Dispose();
        _listening?.Dispose();
    }

    // On macOS and Linux a pipe is a Unix domain socket under the temp directory, whose full
    // path may not exceed 104 characters (108 on Linux): an application id derived from a long
    // install path would not fit, so the name is a short, fixed-length hash of the id.
    private static string PipeName(string id) =>
        "basalt-si-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))[..20].ToLowerInvariant();

    private static string LockFilePath(string id) =>
        Path.Combine(Path.GetTempPath(), "basalt-vb-single-instance", Sanitize(id) + ".lock");

    private static string Sanitize(string id)
    {
        var hash = id.GetHashCode().ToString("X8");
        var cleaned = new string(id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        return cleaned.Length > 64 ? cleaned[..64] + hash : cleaned + hash;
    }
}
