namespace System.Windows.Forms;

/// <summary>Win32 IME window functions have no native handle in Basalt's painted backend.</summary>
public static class ImeContext
{
    private static PlatformNotSupportedException NoNativeIme() =>
        new("Win32 IME context operations require a native HWND.");

    public static void Disable(IntPtr handle) => throw NoNativeIme();

    public static void Enable(IntPtr handle) => throw NoNativeIme();

    public static ImeMode GetImeMode(IntPtr handle) => throw NoNativeIme();

    public static bool IsOpen(IntPtr handle) => throw NoNativeIme();

    public static void SetImeStatus(ImeMode mode, IntPtr handle) => throw NoNativeIme();

    public static void SetOpenStatus(bool open, IntPtr handle) => throw NoNativeIme();
}
