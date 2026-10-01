using System.Drawing;

namespace System.Windows.Forms.VisualStyles;

/// <summary>
/// Describes the visual style currently applied by the operating system.
/// Basalt always draws the classic Windows 2000 theme, so no style is ever
/// loaded: every property reports the "nothing is themed" answer, matching
/// what WinForms reports when the OS has no active visual style.
/// </summary>
public static class VisualStyleInformation
{
    public static string Author => string.Empty;

    public static string ColorScheme => string.Empty;

    public static string Company => string.Empty;

    public static Color ControlHighlightHot => Color.Empty;

    public static string Copyright => string.Empty;

    public static string Description => string.Empty;

    public static string DisplayName => string.Empty;

    public static bool IsEnabledByUser => false;

    public static bool IsSupportedByOS => false;

    public static int MinimumColorDepth => 0;

    public static string Size => string.Empty;

    public static bool SupportsFlatMenus => false;

    public static Color TextControlBorder => Color.Empty;

    public static string Url => string.Empty;

    public static string Version => string.Empty;
}
