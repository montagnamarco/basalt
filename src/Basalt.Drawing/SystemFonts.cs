using System.Drawing.Text;

namespace System.Drawing;

/// <summary>The fonts of the system's own interface: captions, menus, message boxes.</summary>
/// <remarks>
/// Windows reads these from the user's settings. Here they are those of
/// Windows 2000, the look the controls are drawn in: Tahoma at 8 points, bold
/// for captions, and Microsoft Sans Serif at 8.25 points as the default font.
/// Each call gives a new font marked as a system font, as GDI+ does.
/// </remarks>
public static class SystemFonts
{
    public static Font CaptionFont => Make("CaptionFont", "Tahoma", 8f, FontStyle.Bold);

    public static Font SmallCaptionFont => Make("SmallCaptionFont", "Tahoma", 8f, FontStyle.Bold);

    public static Font MenuFont => Make("MenuFont", "Tahoma", 8f, FontStyle.Regular);

    public static Font StatusFont => Make("StatusFont", "Tahoma", 8f, FontStyle.Regular);

    public static Font MessageBoxFont => Make("MessageBoxFont", "Tahoma", 8f, FontStyle.Regular);

    public static Font IconTitleFont => Make("IconTitleFont", "Tahoma", 8f, FontStyle.Regular);

    public static Font DefaultFont => Make("DefaultFont", FontResolver.SansSerif, 8.25f, FontStyle.Regular);

    public static Font DialogFont => Make("DialogFont", "Tahoma", 8f, FontStyle.Regular);

    /// <summary>The system font of that name ("CaptionFont", "MenuFont"…), or null.</summary>
    public static Font? GetFontByName(string systemFontName) => systemFontName switch
    {
        "CaptionFont" => CaptionFont,
        "SmallCaptionFont" => SmallCaptionFont,
        "MenuFont" => MenuFont,
        "StatusFont" => StatusFont,
        "MessageBoxFont" => MessageBoxFont,
        "IconTitleFont" => IconTitleFont,
        "DefaultFont" => DefaultFont,
        "DialogFont" => DialogFont,
        _ => null,
    };

    private static Font Make(string systemName, string family, float size, FontStyle style) =>
        new Font(family, size, style, GraphicsUnit.Point).AsSystemFont(systemName);
}
