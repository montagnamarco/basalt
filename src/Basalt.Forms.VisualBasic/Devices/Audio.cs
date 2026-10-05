namespace Microsoft.VisualBasic.Devices;

/// <summary>
/// Sound playback, as My.Computer.Audio exposes it to a VB application.
/// </summary>
/// <remarks>
/// WAV playback uses the platform backend supplied by Basalt.Windows.Extensions.
/// Streams remain owned by the caller; SoundPlayer loads them before playback.
/// </remarks>
public class Audio
{
    private System.Media.SoundPlayer? _player;

    internal System.Media.SoundPlayer? ActivePlayerForTest => _player;

    public void Play(string location) => Play(location, AudioPlayMode.Background);

    public void Play(string location, AudioPlayMode playMode)
    {
        ArgumentNullException.ThrowIfNull(location);
        Start(new System.Media.SoundPlayer(location), playMode);
    }

    public void Play(byte[] data, AudioPlayMode playMode)
    {
        ArgumentNullException.ThrowIfNull(data);
        // Background playback may still be loading after this method returns.
        // Keep an independent copy so the caller may reuse or change its array.
        var ownedStream = new MemoryStream((byte[])data.Clone(), writable: false);
        Start(new System.Media.SoundPlayer(ownedStream), playMode, ownedStream);
    }

    public void Play(Stream stream, AudioPlayMode playMode)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Start(new System.Media.SoundPlayer(stream), playMode);
    }

    public void PlaySystemSound(System.Media.SystemSound systemSound)
    {
        ArgumentNullException.ThrowIfNull(systemSound);
        systemSound.Play();
    }

    public void Stop()
    {
        if (_player is not null) _player.Stop();
        else System.Media.AudioPlayback.Stop();
    }

    private void Start(System.Media.SoundPlayer player, AudioPlayMode mode, Stream? ownedStream = null)
    {
        if (!Enum.IsDefined(mode))
        {
            player.Dispose();
            ownedStream?.Dispose();
            throw new ArgumentException("The audio play mode is invalid.", nameof(mode));
        }
        if (ownedStream is not null)
            player.LoadCompleted += (_, _) => ownedStream.Dispose();
        _player?.Stop();
        _player?.Dispose();
        _player = player;
        switch (mode)
        {
            case AudioPlayMode.WaitToComplete: player.PlaySync(); break;
            case AudioPlayMode.Background: player.Play(); break;
            case AudioPlayMode.BackgroundLoop: player.PlayLooping(); break;
        }
    }
}
