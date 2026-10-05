using System.Drawing;
using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms.Internal.Skins;

internal static partial class ScrollBarPainter
{
    internal static void DrawPortable(Graphics graphics, Orientation orientation, Rectangle bounds,
        Rectangle thumb, PressedPart pressed, bool enabled, PortableVisualStylePalette palette,
        PortableVisualStylePlatform platform)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var track = new SolidBrush(palette.Surface);
        graphics.FillRectangle(track, bounds);
        PaintArrow(graphics, LineUpButtonBounds(orientation, bounds), orientation, forward: false,
            pressed == PressedPart.LineUpButton, enabled, palette, platform);
        PaintArrow(graphics, LineDownButtonBounds(orientation, bounds), orientation, forward: true,
            pressed == PressedPart.LineDownButton, enabled, palette, platform);
        if (!thumb.IsEmpty)
        {
            PortableVisualStyles.PaintButton(graphics, thumb, palette, platform, hot: false,
                pressed: pressed == PressedPart.Thumb, enabled, isDefault: false);
        }
    }

    /// <summary>How far the arrow glyph's tip sits from the button's centre along the
    /// scroll direction: a 4px-wide triangle (2px either side of centre), the size a
    /// native scroll bar's arrow glyph renders at regardless of the button's own size.</summary>
    private const int ArrowGlyphTipOffset = 2;

    /// <summary>How far the arrow glyph's two base corners sit from centre across the
    /// scroll direction: a 6px-tall triangle, matching the 3:2 base-to-tip proportion
    /// of the native scroll bar arrow glyph.</summary>
    private const int ArrowGlyphBaseOffset = 3;

    private static void PaintArrow(Graphics graphics, Rectangle bounds, Orientation orientation,
        bool forward, bool pressed, bool enabled, PortableVisualStylePalette palette,
        PortableVisualStylePlatform platform)
    {
        PortableVisualStyles.PaintButton(graphics, bounds, palette, platform, hot: false,
            pressed, enabled, isDefault: false);
        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var direction = forward ? 1 : -1;
        Point[] arrow = orientation == Orientation.Horizontal
            ? [new(center.X + direction * ArrowGlyphTipOffset, center.Y),
                new(center.X - direction * ArrowGlyphTipOffset, center.Y - ArrowGlyphBaseOffset),
                new(center.X - direction * ArrowGlyphTipOffset, center.Y + ArrowGlyphBaseOffset)]
            : [new(center.X, center.Y + direction * ArrowGlyphTipOffset),
                new(center.X - ArrowGlyphBaseOffset, center.Y - direction * ArrowGlyphTipOffset),
                new(center.X + ArrowGlyphBaseOffset, center.Y - direction * ArrowGlyphTipOffset)];
        using var brush = new SolidBrush(enabled ? palette.Text : palette.DisabledText);
        graphics.FillPolygon(brush, arrow);
    }
}
