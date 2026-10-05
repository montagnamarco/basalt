using System.Media;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.Devices;

namespace Basalt.Forms.Tests.VisualBasic;

[Collection("Audio playback")]
public class SoundPlayerTests
{
    [Fact]
    public void Load_preserves_the_callers_stream_and_invalidates_when_source_changes()
    {
        using var stream = new MemoryStream(Wave());
        using var player = new SoundPlayer(stream);
        var completed = 0;
        player.LoadCompleted += (_, e) => { Assert.Null(e.Error); completed++; };
        player.Load();
        player.Load();
        Assert.True(player.IsLoadCompleted);
        Assert.Equal(1, completed);
        player.SoundLocation = "replacement.wav";
        Assert.False(player.IsLoadCompleted);
        Assert.Null(player.Stream);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void PlaySync_waits_for_backend_and_removes_the_temporary_wave()
    {
        string? filename = null;
        AudioPlayback.RunProcessForTest = async (start, cancellation) =>
        {
            filename = Assert.Single(start.ArgumentList);
            Assert.Equal(Wave(), await File.ReadAllBytesAsync(filename, cancellation));
        };
        try
        {
            new Audio().Play(Wave(), AudioPlayMode.WaitToComplete);
            Assert.NotNull(filename);
            Assert.False(File.Exists(filename));
        }
        finally { AudioPlayback.RunProcessForTest = null; }
    }

    [Fact]
    public async Task Stop_cancels_loop_and_cleans_up_the_backend_file()
    {
        var started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AudioPlayback.RunProcessForTest = async (start, cancellation) =>
        {
            started.TrySetResult(Assert.Single(start.ArgumentList));
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            finally { cancelled.TrySetResult(); }
        };
        try
        {
            using var player = new SoundPlayer(new MemoryStream(Wave()));
            player.PlayLooping();
            var filename = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            player.Stop();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var retry = 0; retry < 100 && File.Exists(filename); retry++) await Task.Delay(10);
            Assert.False(File.Exists(filename));
        }
        finally { AudioPlayback.Stop(); AudioPlayback.RunProcessForTest = null; }
    }

    [Fact]
    public async Task LoadAsync_reports_invalid_wave_without_claiming_success()
    {
        using var player = new SoundPlayer(new MemoryStream([1, 2, 3]));
        var completion = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.LoadCompleted += (_, e) => completion.TrySetResult(e.Error);
        player.LoadAsync();
        Assert.IsType<InvalidOperationException>(await completion.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(player.IsLoadCompleted);
    }

    [Fact]
    public void Load_failure_raises_completion_with_the_error()
    {
        using var player = new SoundPlayer(new MemoryStream([1, 2, 3]));
        Exception? reported = null;
        player.LoadCompleted += (_, e) => reported = e.Error;

        var thrown = Assert.Throws<InvalidOperationException>(player.Load);

        Assert.Same(thrown, reported);
        Assert.False(player.IsLoadCompleted);
    }

    [Fact]
    public async Task Background_play_returns_before_loading_and_stop_prevents_late_playback()
    {
        using var stream = new GatedWaveStream(Wave());
        using var player = new SoundPlayer(stream);
        var loadCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var played = 0;
        player.LoadCompleted += (_, _) => loadCompleted.TrySetResult();
        AudioPlayback.RunProcessForTest = (_, _) =>
        {
            Interlocked.Increment(ref played);
            return Task.CompletedTask;
        };
        try
        {
            var call = Task.Run(player.Play);
            await call.WaitAsync(TimeSpan.FromSeconds(5));
            await stream.ReadStarted.WaitAsync(TimeSpan.FromSeconds(5));
            player.Stop();
            stream.Release();
            await loadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, Volatile.Read(ref played));
        }
        finally
        {
            stream.Release();
            AudioPlayback.Stop();
            AudioPlayback.RunProcessForTest = null;
        }
    }

    [Fact]
    public async Task Repeated_LoadAsync_coalesces_reads_and_completion()
    {
        using var stream = new GatedWaveStream(Wave());
        using var player = new SoundPlayer(stream);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        player.LoadCompleted += (_, e) =>
        {
            Assert.Null(e.Error);
            Interlocked.Increment(ref count);
            completed.TrySetResult();
        };

        player.LoadAsync();
        player.LoadAsync();
        await stream.ReadStarted.WaitAsync(TimeSpan.FromSeconds(5));
        stream.Release();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref count));
        Assert.True(player.IsLoadCompleted);
    }

    [Fact]
    public async Task VisualBasic_Audio_Stop_prevents_playback_after_background_load_finishes()
    {
        using var stream = new GatedWaveStream(Wave());
        var audio = new Audio();
        var played = 0;
        AudioPlayback.RunProcessForTest = (_, _) =>
        {
            Interlocked.Increment(ref played);
            return Task.CompletedTask;
        };
        try
        {
            audio.Play(stream, AudioPlayMode.Background);
            var loadCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            audio.ActivePlayerForTest!.LoadCompleted += (_, _) => loadCompleted.TrySetResult();
            await stream.ReadStarted.WaitAsync(TimeSpan.FromSeconds(5));
            audio.Stop();
            stream.Release();
            await loadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, Volatile.Read(ref played));
        }
        finally
        {
            stream.Release();
            audio.Stop();
            AudioPlayback.RunProcessForTest = null;
        }
    }

    [Fact]
    public void Load_rejects_truncated_chunk_data()
    {
        var wave = Wave();
        using var player = new SoundPlayer(new MemoryStream(wave[..^1]));
        Assert.Throws<InvalidOperationException>(player.Load);
        Assert.False(player.IsLoadCompleted);
    }

    private static byte[] Wave()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(38); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(2); writer.Write((short)0);
        return stream.ToArray();
    }

    private sealed class GatedWaveStream(byte[] wave) : MemoryStream(wave)
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _started.Task;

        public void Release() => _release.TrySetResult();

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            await base.CopyToAsync(destination, bufferSize, cancellationToken);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, offset, count, cancellationToken);
        }
    }
}

[CollectionDefinition("Audio playback", DisableParallelization = true)]
public class AudioPlaybackCollection;
