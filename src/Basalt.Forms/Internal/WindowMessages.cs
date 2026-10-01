namespace System.Windows.Forms.Internal;

/// <summary>
/// The few Win32 messages synthesized for the keyboard pipeline, so that
/// overrides of ProcessCmdKey, ProcessKeyMessage and the like receive the
/// messages they expect. Values from winuser.h.
/// </summary>
internal static class WindowMessages
{
    public const int KeyDown = 0x0100;
    public const int KeyUp = 0x0101;
    public const int Char = 0x0102;
    public const int SystemKeyDown = 0x0104;
    public const int SystemKeyUp = 0x0105;
    public const int SystemChar = 0x0106;

    public static bool IsKeyDown(int message) => message is KeyDown or SystemKeyDown;

    public static bool IsChar(int message) => message is Char or SystemChar;
}
