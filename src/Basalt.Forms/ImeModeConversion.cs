namespace System.Windows.Forms;

/// <summary>Win32 IME conversion modes are unavailable without an HWND and HIMC.</summary>
public struct ImeModeConversion
{
    public static Dictionary<ImeMode, ImeModeConversion> ImeModeConversionBits => [];

    public static bool IsCurrentConversionTableSupported => false;
}
