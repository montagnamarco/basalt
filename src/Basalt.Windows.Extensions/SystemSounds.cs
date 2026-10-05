namespace System.Media;

public sealed class SystemSounds
{
    private SystemSounds() { }

    public static SystemSound Asterisk { get; } = new(0x40);
    public static SystemSound Beep { get; } = new(0);
    public static SystemSound Exclamation { get; } = new(0x30);
    public static SystemSound Hand { get; } = new(0x10);
    public static SystemSound Question { get; } = new(0x20);
}
