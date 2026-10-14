using System.Text.RegularExpressions;
using Basalt.Workspace.Terminal;

namespace Basalt.Tests;

/// <summary>
/// The process id a terminal reports is the id of the program it started, which is what a caller needs to end
/// that program. Checked against the program's own report of its pid, not against the id alone.
/// </summary>
public sealed class PtyProcessIdTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-pty-pid-").FullName;

    private IPtyConnection? _pty;

    public void Dispose()
    {
        _pty?.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void AnUnstartedTerminalHasNoProcessId()
    {
        // The stand-in for a terminal that could not start has no child, so it reports -1 rather than a stale id.
        using var pty = new UnavailablePtyConnection("could not start");

        Assert.Equal(-1, pty.ProcessId);
    }

    [Fact]
    public async Task TheProcessIdIsTheIdOfTheProgramStarted()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows uses ConPTY, covered separately.");

        // The program prints its own pid. /bin/sh replaces itself with the program, so the two must agree.
        var transcript = new System.Text.StringBuilder();
        _pty = new UnixPtyConnection("/bin/sh", ["-c", "echo pid-$$; read line"], _root);
        _pty.OutputReceived += (_, text) => { lock (transcript) transcript.Append(text); };

        var reported = await WaitForAsync(() =>
        {
            lock (transcript)
            {
                var match = Regex.Match(transcript.ToString(), @"pid-(\d+)");
                return match.Success ? int.Parse(match.Groups[1].Value) : (int?)null;
            }
        });

        Assert.True(reported is not null, "the program did not report its pid.");
        Assert.Equal(reported, _pty.ProcessId);
        Assert.True(_pty.IsRunning);
    }

    /// <summary>Polls until the value appears or the time runs out.</summary>
    private static async Task<int?> WaitForAsync(Func<int?> read, int milliseconds = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);

        while (DateTime.UtcNow < deadline)
        {
            if (read() is { } value) return value;
            await Task.Delay(25);
        }

        return read();
    }
}
