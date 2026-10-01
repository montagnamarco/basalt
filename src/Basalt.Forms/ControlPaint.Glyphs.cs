using System.Drawing;

namespace System.Windows.Forms;

public static partial class ControlPaint
{
    public static void DrawMenuGlyph(Graphics graphics, Rectangle rectangle, MenuGlyph glyph) =>
        DrawMenuGlyph(graphics, rectangle, glyph, SystemColors.ControlText, SystemColors.Control);

    public static void DrawMenuGlyph(Graphics graphics, Rectangle rectangle, MenuGlyph glyph, Color foreColor, Color backColor)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;

        if (!backColor.IsEmpty)
        {
            using var back = new SolidBrush(backColor);
            graphics.FillRectangle(back, rectangle);
        }

        using var brush = new SolidBrush(foreColor);
        var area = Shrink(rectangle, Math.Min(rectangle.Width, rectangle.Height) / 4);

        switch (glyph)
        {
            case MenuGlyph.Arrow:
                graphics.FillPolygon(brush, ArrowPoints(area, ScrollButton.Right));
                break;

            case MenuGlyph.Checkmark:
                DrawCheckGlyph(graphics, area, foreColor);
                break;

            case MenuGlyph.Bullet:
                var side = Math.Min(area.Width, area.Height);
                var dot = new Rectangle(area.Left + (area.Width - side) / 2, area.Top + (area.Height - side) / 2, side, side);
                graphics.FillEllipse(brush, dot);
                break;
        }
    }

    public static void DrawMenuGlyph(Graphics graphics, int x, int y, int width, int height, MenuGlyph glyph) =>
        DrawMenuGlyph(graphics, new Rectangle(x, y, width, height), glyph);

    public static void DrawMenuGlyph(Graphics graphics, int x, int y, int width, int height, MenuGlyph glyph, Color foreColor, Color backColor) =>
        DrawMenuGlyph(graphics, new Rectangle(x, y, width, height), glyph, foreColor, backColor);

    public static void DrawFocusRectangle(Graphics graphics, Rectangle rectangle) =>
        DrawFocusRectangle(graphics, rectangle, SystemColors.ControlText, SystemColors.Control);

    /// <summary>
    /// Win32 draws a focus rectangle by XORing a 1-pixel dotted pen with the
    /// screen; without a screen to XOR against, the same look comes from
    /// alternating two brushes along the perimeter pixel by pixel.
    /// </summary>
    public static void DrawFocusRectangle(Graphics graphics, Rectangle rectangle, Color foreColor, Color backColor)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;

        using var fore = new SolidBrush(foreColor);
        using var back = new SolidBrush(backColor);

        DrawDottedSide(graphics, fore, back, rectangle.Left, rectangle.Top, rectangle.Width, horizontal: true);
        DrawDottedSide(graphics, fore, back, rectangle.Left, rectangle.Bottom - 1, rectangle.Width, horizontal: true);
        DrawDottedSide(graphics, fore, back, rectangle.Left, rectangle.Top, rectangle.Height, horizontal: false);
        DrawDottedSide(graphics, fore, back, rectangle.Right - 1, rectangle.Top, rectangle.Height, horizontal: false);
    }

    private static void DrawDottedSide(Graphics graphics, Brush fore, Brush back, int x, int y, int length, bool horizontal)
    {
        for (var i = 0; i < length; i++)
        {
            var brush = i % 2 == 0 ? fore : back;
            if (horizontal) graphics.FillRectangle(brush, x + i, y, 1, 1);
            else graphics.FillRectangle(brush, x, y + i, 1, 1);
        }
    }

    public static void DrawSizeGrip(Graphics graphics, Color backColor, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using var light = new SolidBrush(SystemColors.ControlLightLight);
        using var dark = new SolidBrush(SystemColors.ControlDarkDark);

        // Three diagonal highlight/shadow dot pairs climbing away from the
        // bottom-right corner: the classic size grip, drawn on whatever
        // background is already there (backColor only identifies it, GDI+
        // never needed to erase first).
        for (var i = 0; i < 3; i++)
        {
            var offset = 4 + i * 4;
            if (offset + 1 > bounds.Width || offset + 1 > bounds.Height) break;

            graphics.FillRectangle(light, bounds.Right - offset, bounds.Bottom - offset, 2, 2);
            graphics.FillRectangle(dark, bounds.Right - offset - 2, bounds.Bottom - offset - 2, 2, 2);
        }
    }

    public static void DrawSizeGrip(Graphics graphics, Color backColor, int x, int y, int width, int height) =>
        DrawSizeGrip(graphics, backColor, new Rectangle(x, y, width, height));
}
