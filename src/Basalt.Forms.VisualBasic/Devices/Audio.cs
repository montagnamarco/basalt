namespace Microsoft.VisualBasic.Devices;

/// <summary>
/// Sound playback, as My.Computer.Audio exposes it to a VB application.
/// </summary>
/// <remarks>
/// Basalt has no audio device API (no equivalent of winmm's PlaySound):
/// every Play overload throws PlatformNotSupportedException with a message
/// that says so, rather than silently doing nothing. Stop is the one member
/// that still makes sense when nothing can ever be playing: it does nothing,
/// exactly as Microsoft's Stop does once a Play call already returned.
/// PlaySystemSound(System.Media.SystemSound) is left out: that type ships in
/// the System.Windows.Extensions package, which this project does not reference.
/// </remarks>
public class Audio
{
    private const string NoAudioDevice = "Audio.Play is not supported: Basalt has no audio device API to play sound through.";

    public void Play(string location) => throw new PlatformNotSupportedException(NoAudioDevice);

    public void Play(string location, AudioPlayMode playMode) => throw new PlatformNotSupportedException(NoAudioDevice);

    public void Play(byte[] data, AudioPlayMode playMode) => throw new PlatformNotSupportedException(NoAudioDevice);

    public void Play(Stream stream, AudioPlayMode playMode) => throw new PlatformNotSupportedException(NoAudioDevice);

    /// <summary>Nothing is ever playing, so there is nothing to stop.</summary>
    public void Stop()
    {
    }
}
