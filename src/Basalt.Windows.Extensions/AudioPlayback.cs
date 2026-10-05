using System.Diagnostics;
using System.Runtime.InteropServices;

namespace System.Media;

internal static class AudioPlayback
{
    private static readonly Lock Gate = new();
    private static CancellationTokenSource? _playing;

    internal static Func<ProcessStartInfo, CancellationToken, Task>? RunProcessForTest { get; set; }

    internal static void Play(byte[] wave, bool wait, bool loop)
    {
        var command = RunProcessForTest is null ? Command() : "basalt-test-audio";
        var cancellation = new CancellationTokenSource();
        lock (Gate)
        {
            _playing?.Cancel();
            _playing = cancellation;
        }

        async Task PlayCore()
        {
            var path = Path.Combine(Path.GetTempPath(), $"basalt-audio-{Guid.NewGuid():N}.wav");
            try
            {
                await File.WriteAllBytesAsync(path, wave, cancellation.Token).ConfigureAwait(false);
                do
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (OperatingSystem.IsWindows() && RunProcessForTest is null)
                    {
                        if (!PlaySound(path, IntPtr.Zero, 0x00020000 | 0x00000002))
                            throw new IOException("Windows could not play the WAV file.");
                    }
                    else
                    {
                        var start = new ProcessStartInfo(command!) { UseShellExecute = false, CreateNoWindow = true };
                        start.ArgumentList.Add(path);
                        if (RunProcessForTest is { } testRunner) await testRunner(start, cancellation.Token).ConfigureAwait(false);
                        else await RunProcess(start, cancellation.Token).ConfigureAwait(false);
                    }
                } while (loop && !cancellation.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                try { File.Delete(path); }
                finally
                {
                    lock (Gate)
                    {
                        if (ReferenceEquals(_playing, cancellation)) _playing = null;
                    }
                    cancellation.Dispose();
                }
            }
        }

        if (wait) PlayCore().GetAwaiter().GetResult();
        else
        {
            _ = Task.Run(PlayCore).ContinueWith(task =>
                Trace.TraceError("Audio playback failed: {0}", task.Exception?.GetBaseException().Message),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    internal static void Stop()
    {
        lock (Gate) _playing?.Cancel();
        if (OperatingSystem.IsWindows()) _ = PlaySound(null, IntPtr.Zero, 0);
    }

    private static string? Command()
    {
        if (OperatingSystem.IsWindows()) return null;
        if (OperatingSystem.IsMacOS()) return "/usr/bin/afplay";
        foreach (var command in new[] { "paplay", "aplay" })
        {
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                var path = Path.Combine(directory, command);
                if (File.Exists(path)) return path;
            }
        }

        throw new PlatformNotSupportedException("Audio playback requires paplay or aplay on Linux.");
    }

    private static async Task RunProcess(ProcessStartInfo start, CancellationToken cancellation)
    {
        using var process = Process.Start(start) ?? throw new IOException("The audio player could not be started.");
        try
        {
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException($"The audio player failed with exit code {process.ExitCode}.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
    }

    internal static void PlayNotification(int kind)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!MessageBeep((uint)kind)) throw new IOException("Windows could not play the notification sound.");
            return;
        }

        // A short PCM tone makes notifications available even when the desktop
        // has no Windows sound aliases. Each category has a distinct pitch.
        const int rate = 22050;
        const int samples = rate / 8;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        for (var index = 0; index < samples; index++)
            writer.Write((short)(Math.Sin(2 * Math.PI * (440 + kind * 5) * index / rate) * 5000));

        Play(stream.ToArray(), wait: false, loop: false);
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySound(string? name, IntPtr module, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MessageBeep(uint kind);
}
