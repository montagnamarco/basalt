using System.Drawing;

namespace System.Windows.Forms.Internal;

/// <summary>ContentAlignment as DrawText flags, the way WinForms controls draw their text.</summary>
internal static class ContentAlignments
{
    public static TextFormatFlags ToTextFormat(ContentAlignment alignment)
    {
        var flags = alignment switch
        {
            ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter,
        };

        flags |= alignment switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight => TextFormatFlags.VerticalCenter,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => TextFormatFlags.Bottom,
            _ => TextFormatFlags.Top,
        };

        return flags;
    }
}
