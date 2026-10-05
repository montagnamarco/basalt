using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace System.Media;

[Serializable]
public class SoundPlayer : Component, ISerializable
{
    private string _soundLocation = string.Empty;
    private Stream? _stream;
    private byte[]? _wave;
    private int _loadTimeout = 10000;
    private int _sourceVersion;
    private int _playVersion;
    private readonly Lock _gate = new();
    private Task<byte[]>? _loading;

    public SoundPlayer() { }

    public SoundPlayer(string? soundLocation) => SoundLocation = soundLocation ?? string.Empty;

    public SoundPlayer(Stream? stream) => Stream = stream;

    protected SoundPlayer(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);
        _soundLocation = info.GetString("SoundLocation") ?? string.Empty;
        _stream = (Stream?)info.GetValue("Stream", typeof(Stream));
        LoadTimeout = info.GetInt32("LoadTimeout");
        Tag = info.GetValue("Tag", typeof(object));
    }

    [Browsable(false)]
    public bool IsLoadCompleted { get; private set; }

    [DefaultValue(10000)]
    public int LoadTimeout
    {
        get => _loadTimeout;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            _loadTimeout = value;
        }
    }

    [DefaultValue("")]
    public string SoundLocation
    {
        get => _soundLocation;
        set
        {
            value ??= string.Empty;
            if (_soundLocation == value) return;
            _soundLocation = value;
            _stream = null;
            InvalidateLoadedSound();
            OnSoundLocationChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(null)]
    public Stream? Stream
    {
        get => _stream;
        set
        {
            if (ReferenceEquals(_stream, value)) return;
            _stream = value;
            _soundLocation = string.Empty;
            InvalidateLoadedSound();
            OnStreamChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(null)]
    public object? Tag { get; set; }

    public event AsyncCompletedEventHandler? LoadCompleted;
    public event EventHandler? SoundLocationChanged;
    public event EventHandler? StreamChanged;

    protected virtual void OnLoadCompleted(AsyncCompletedEventArgs e) => LoadCompleted?.Invoke(this, e);
    protected virtual void OnSoundLocationChanged(EventArgs e) => SoundLocationChanged?.Invoke(this, e);
    protected virtual void OnStreamChanged(EventArgs e) => StreamChanged?.Invoke(this, e);

    public void Load()
    {
        var (loading, version, _) = GetLoad(background: false);
        if (loading is null) return;
        try
        {
            CompleteLoad(loading, version, loading.GetAwaiter().GetResult(), null, null);
        }
        catch (Exception error)
        {
            CompleteLoad(loading, version, null, error, null);
            throw;
        }
    }

    public void LoadAsync()
    {
        var (loading, version, created) = GetLoad(background: true);
        if (loading is not null && created)
            _ = ObserveLoadAsync(loading, version, SynchronizationContext.Current);
    }

    public void Play() => PlayCore(wait: false, loop: false);
    public void PlayLooping() => PlayCore(wait: false, loop: true);
    public void PlaySync() => PlayCore(wait: true, loop: false);
    public void Stop()
    {
        lock (_gate)
        {
            Interlocked.Increment(ref _playVersion);
            AudioPlayback.Stop();
        }
    }

    private void PlayCore(bool wait, bool loop)
    {
        if (_stream is null && _soundLocation.Length == 0)
        {
            SystemSounds.Beep.Play();
            return;
        }

        var playVersion = Interlocked.Increment(ref _playVersion);
        if (wait)
        {
            Load();
            if (playVersion == Volatile.Read(ref _playVersion))
                AudioPlayback.Play(_wave ?? throw new InvalidOperationException("The sound source changed during loading."), true, loop);
            return;
        }

        var (loading, sourceVersion, created) = GetLoad(background: true);
        if (loading is null)
        {
            StartPlaybackIfCurrent(_wave!, sourceVersion, playVersion, loop);
            return;
        }

        _ = PlayWhenLoadedAsync(loading, sourceVersion, playVersion, loop, created, SynchronizationContext.Current);
    }

    private void InvalidateLoadedSound()
    {
        lock (_gate)
        {
            Interlocked.Increment(ref _sourceVersion);
            Interlocked.Increment(ref _playVersion);
            _loading = null;
            _wave = null;
            IsLoadCompleted = false;
        }
    }

    private (Task<byte[]>? Loading, int Version, bool Created) GetLoad(bool background)
    {
        lock (_gate)
        {
            var version = _sourceVersion;
            if (IsLoadCompleted) return (null, version, false);
            if (_loading is not null) return (_loading, version, false);
            var source = _stream;
            var location = _soundLocation;
            var timeout = _loadTimeout;
            _loading = background
                ? Task.Run(() => ReadSourceAsync(source, location, timeout))
                : ReadSourceAsync(source, location, timeout);
            return (_loading, version, true);
        }
    }

    private void CompleteLoad(Task<byte[]> loading, int version, byte[]? wave, Exception? error, SynchronizationContext? context)
    {
        lock (_gate)
        {
            if (version != _sourceVersion || !ReferenceEquals(_loading, loading)) return;
            _loading = null;
            _wave = wave;
            IsLoadCompleted = error is null;
        }

        void Raise() => OnLoadCompleted(new AsyncCompletedEventArgs(error, false, null));
        if (context is not null) context.Post(_ => Raise(), null);
        else Raise();
    }

    private async Task ObserveLoadAsync(Task<byte[]> loading, int version, SynchronizationContext? context)
    {
        try { CompleteLoad(loading, version, await loading.ConfigureAwait(false), null, context); }
        catch (Exception error) { CompleteLoad(loading, version, null, error, context); }
    }

    private async Task PlayWhenLoadedAsync(Task<byte[]> loading, int sourceVersion, int playVersion, bool loop,
        bool completeLoad, SynchronizationContext? context)
    {
        try
        {
            var wave = await loading.ConfigureAwait(false);
            if (completeLoad) CompleteLoad(loading, sourceVersion, wave, null, context);
            StartPlaybackIfCurrent(wave, sourceVersion, playVersion, loop);
        }
        catch (Exception error)
        {
            if (completeLoad) CompleteLoad(loading, sourceVersion, null, error, context);
            Trace.TraceError("Audio loading failed: {0}", error);
        }
    }

    private void StartPlaybackIfCurrent(byte[] wave, int sourceVersion, int playVersion, bool loop)
    {
        lock (_gate)
        {
            if (sourceVersion == _sourceVersion && playVersion == _playVersion)
                AudioPlayback.Play(wave, false, loop);
        }
    }

    private static async Task<byte[]> ReadSourceAsync(Stream? source, string location, int timeout)
    {
        using var cancellation = new CancellationTokenSource();
        if (timeout > 0) cancellation.CancelAfter(timeout);
        try
        {
            byte[] wave;
            if (source is not null)
            {
                using var memory = new MemoryStream();
                await source.CopyToAsync(memory, cancellation.Token).ConfigureAwait(false);
                wave = memory.ToArray();
            }
            else if (location.Length == 0)
            {
                return [];
            }
            else if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                using var client = new HttpClient();
                wave = await client.GetByteArrayAsync(uri, cancellation.Token).ConfigureAwait(false);
            }
            else
            {
                var path = Uri.TryCreate(location, UriKind.Absolute, out uri) && uri.IsFile ? uri.LocalPath : location;
                wave = await File.ReadAllBytesAsync(path, cancellation.Token).ConfigureAwait(false);
            }

            ValidateWave(wave);
            return wave;
        }
        catch (OperationCanceledException error) when (cancellation.IsCancellationRequested)
        {
            throw new TimeoutException("The sound did not load within LoadTimeout.", error);
        }
    }

    private static void ValidateWave(byte[] wave)
    {
        static InvalidOperationException Invalid() => new("The sound source must be a complete RIFF WAVE file.");
        if (wave.Length < 12 || !wave.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wave.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw Invalid();
        var length = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(wave.AsSpan(4)) + 8;
        if (length > wave.Length || length < 12) throw Invalid();
        var format = false;
        var data = false;
        for (long offset = 12; offset < length;)
        {
            if (offset + 8 > length) throw Invalid();
            var header = wave.AsSpan((int)offset, 8);
            var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            var next = offset + 8 + size;
            if (next > length) throw Invalid();
            if (header[..4].SequenceEqual("fmt "u8))
            {
                if (size < 16) throw Invalid();
                format = true;
            }
            if (header[..4].SequenceEqual("data"u8)) data = true;
            offset = next + (size & 1);
            if (offset > length) throw Invalid();
        }
        if (!format || !data) throw Invalid();
    }

    void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);
        info.AddValue("SoundLocation", SoundLocation);
        info.AddValue("Stream", Stream, typeof(Stream));
        info.AddValue("LoadTimeout", LoadTimeout);
        info.AddValue("Tag", Tag);
    }
}
