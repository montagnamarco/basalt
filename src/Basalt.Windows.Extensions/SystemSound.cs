namespace System.Media;

public sealed class SystemSound
{
    private readonly int _kind;

    internal SystemSound(int kind) => _kind = kind;

    public void Play() => AudioPlayback.PlayNotification(_kind);
}
