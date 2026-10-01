using System.Drawing;
using System.Drawing.Text;

namespace System.Windows.Forms;

/// <summary>
/// Text drawn and measured the way GDI's DrawText does it, which is how
/// WinForms controls draw their captions: a line as tall as the font's
/// height, a margin for overhanging glyphs on both sides, the ampersand as
/// a mnemonic prefix, and the flags of DrawText for alignment and ellipses.
/// </summary>
/// <remarks>
/// The glyphs come from Basalt.Drawing's text engine; only the measuring
/// rules are GDI's, so a control sizes itself to its text as on Windows. The
/// side margin is the font height divided by six (integer division), what
/// GDI calls the glyph overhang padding: two pixels for an 8.25-point font.
/// </remarks>
public static class TextRenderer
{
    public static void DrawText(IDeviceContext dc, string? text, Font? font, Point pt, Color foreColor) =>
        DrawText(dc, text, font, pt, foreColor, Color.Empty, TextFormatFlags.Default);

    public static void DrawText(IDeviceContext dc, ReadOnlySpan<char> text, Font font, Point pt, Color foreColor) =>
        DrawText(dc, text.ToString(), font, pt, foreColor);

    public static void DrawText(IDeviceContext dc, string? text, Font? font, Point pt, Color foreColor, Color backColor) =>
        DrawText(dc, text, font, pt, foreColor, backColor, TextFormatFlags.Default);

    public static void DrawText(IDeviceContext dc, ReadOnlySpan<char> text, Font font, Point pt, Color foreColor, Color backColor) =>
        DrawText(dc, text.ToString(), font, pt, foreColor, backColor);

    public static void DrawText(IDeviceContext dc, string? text, Font? font, Point pt, Color foreColor, TextFormatFlags flags) =>
        DrawText(dc, text, font, pt, foreColor, Color.Empty, flags);

    public static void DrawText(IDeviceContext dc, ReadOnlySpan<char> text, Font? font, Point pt, Color foreColor, TextFormatFlags flags) =>
        DrawText(dc, text.ToString(), font, pt, foreColor, flags);

    /// <summary>Draws from a point: the text is laid out on one line (or its own lines), without wrapping.</summary>
    public static void DrawText(IDeviceContext dc, string? text, Font? font, Point pt, Color foreColor, Color backColor, TextFormatFlags flags)
    {
        // From a point, nothing aligns: GDI ignores alignment without a rectangle to align in.
        var withoutAlignment = flags & ~(TextFormatFlags.HorizontalCenter | TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.Bottom);
        var size = MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), withoutAlignment);
        DrawText(dc, text, font, new Rectangle(pt, size), foreColor, backColor, withoutAlignment | TextFormatFlags.NoClipping);
    }

    public static void DrawText(IDeviceContext dc, ReadOnlySpan<char> text, Font? font, Point pt, Color foreColor, Color backColor, TextFormatFlags flags) =>
        DrawText(dc, text.ToString(), font, pt, foreColor, backColor, flags);

    public static void DrawText(IDeviceContext dc, string? text, Font? font, Rectangle bounds, Color foreColor) =>
        DrawText(dc, text, font, bounds, foreColor, Color.Empty, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

    public static void DrawText(IDeviceContext dc, ReadOnlySpan<char> text, Font? font, Rectangle bounds, Color foreColor) =>
        DrawText(dc, text.ToString(), font, bounds, foreColor);

    public static void DrawText(IDeviceContext dc, string? text, Font? font, Rectangle bounds, Color foreColor, Color backColor) =>
        DrawText(dc, text, font, bounds, foreColor, backColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

    public static void DrawText(IDeviceContext dc, ReadOnlySpan<char> text, Font? font, Rectangle bounds, Color foreColor, Color backColor) =>
        DrawText(dc, text.ToString(), font, bounds, foreColor, backColor);

    public static void DrawText(IDeviceContext dc, string? text, Font? font, Rectangle bounds, Color foreColor, TextFormatFlags flags) =>
        DrawText(dc, text, font, bounds, foreColor, Color.Empty, flags);

    public static void DrawText(IDeviceContext dc, ReadOnlySpan<char> text, Font? font, Rectangle bounds, Color foreColor, TextFormatFlags flags) =>
        DrawText(dc, text.ToString(), font, bounds, foreColor, flags);

    public static void DrawText(IDeviceContext dc, ReadOnlySpan<char> text, Font? font, Rectangle bounds, Color foreColor, Color backColor, TextFormatFlags flags) =>
        DrawText(dc, text.ToString(), font, bounds, foreColor, backColor, flags);

    /// <summary>Draws the text in the rectangle with DrawText's flags; the background colour fills behind the text.</summary>
    public static void DrawText(IDeviceContext dc, string? text, Font? font, Rectangle bounds, Color foreColor, Color backColor, TextFormatFlags flags)
    {
        ArgumentNullException.ThrowIfNull(dc);

        if (string.IsNullOrEmpty(text) || foreColor == Color.Transparent) return;

        if (dc is not Graphics graphics) throw new ArgumentException("The device context must be a Graphics.", nameof(dc));

        font ??= Control.DefaultFont;

        var state = graphics.Save();

        try
        {
            if ((flags & TextFormatFlags.NoClipping) == 0) graphics.IntersectClip(bounds);

            if (!backColor.IsEmpty && backColor != Color.Transparent)
            {
                using var background = new SolidBrush(backColor);
                graphics.FillRectangle(background, bounds);
            }

            var (left, right) = Margins(font, flags);
            var inner = Rectangle.FromLTRB(bounds.Left + left, bounds.Top, bounds.Right - right, bounds.Bottom);

            using var format = CreateFormat(flags);
            using var brush = new SolidBrush(foreColor);
            var layout = (flags & TextFormatFlags.SingleLine) != 0 || (flags & TextFormatFlags.WordBreak) == 0
                ? AlignSingleBlock(graphics, text, font, inner, format, flags)
                : inner;

            graphics.DrawString(text, font, brush, layout, format);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    public static Size MeasureText(string? text, Font? font) => MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.Default);

    public static Size MeasureText(ReadOnlySpan<char> text, Font? font) => MeasureText(text.ToString(), font);

    public static Size MeasureText(string? text, Font? font, Size proposedSize) => MeasureText(text, font, proposedSize, TextFormatFlags.Default);

    public static Size MeasureText(ReadOnlySpan<char> text, Font? font, Size proposedSize) => MeasureText(text.ToString(), font, proposedSize);

    public static Size MeasureText(ReadOnlySpan<char> text, Font? font, Size proposedSize, TextFormatFlags flags) => MeasureText(text.ToString(), font, proposedSize, flags);

    public static Size MeasureText(IDeviceContext dc, string? text, Font? font) => MeasureText(text, font);

    public static Size MeasureText(IDeviceContext dc, ReadOnlySpan<char> text, Font? font) => MeasureText(text.ToString(), font);

    public static Size MeasureText(IDeviceContext dc, string? text, Font? font, Size proposedSize) => MeasureText(text, font, proposedSize);

    public static Size MeasureText(IDeviceContext dc, ReadOnlySpan<char> text, Font? font, Size proposedSize) => MeasureText(text.ToString(), font, proposedSize);

    public static Size MeasureText(IDeviceContext dc, string? text, Font? font, Size proposedSize, TextFormatFlags flags) => MeasureText(text, font, proposedSize, flags);

    public static Size MeasureText(IDeviceContext dc, ReadOnlySpan<char> text, Font? font, Size proposedSize, TextFormatFlags flags) => MeasureText(text.ToString(), font, proposedSize, flags);

    /// <summary>
    /// The size DrawText would need: whole pixels, the lines one font height
    /// each, the overhang margins added. Empty text measures nothing wide but
    /// still one line high, as GDI answers.
    /// </summary>
    public static Size MeasureText(string? text, Font? font, Size proposedSize, TextFormatFlags flags)
    {
        font ??= Control.DefaultFont;

        var (left, right) = Margins(font, flags);
        if (string.IsNullOrEmpty(text)) return new Size(0, font.Height);

        var wraps = (flags & TextFormatFlags.WordBreak) != 0 && (flags & TextFormatFlags.SingleLine) == 0;
        var width = wraps && proposedSize.Width > 0 && proposedSize.Width < int.MaxValue ? Math.Max(proposedSize.Width - left - right, 1) : 0;

        using var format = CreateFormat(flags & ~(TextFormatFlags.EndEllipsis | TextFormatFlags.PathEllipsis | TextFormatFlags.WordEllipsis));
        var measured = Control.MeasurementGraphics.MeasureString(text, font, new SizeF(width, 0), format, out _, out var lines);

        var height = Math.Max(lines, 1) * font.Height;

        return new Size((int)Math.Ceiling(measured.Width) + left + right, height);
    }

    /// <summary>The overhang margins for the padding the flags choose.</summary>
    private static (int Left, int Right) Margins(Font font, TextFormatFlags flags)
    {
        if ((flags & TextFormatFlags.NoPadding) != 0) return (0, 0);

        var overhang = font.Height / 6;

        return (flags & TextFormatFlags.LeftAndRightPadding) != 0 ? (overhang * 2, overhang * 2) : (overhang, overhang);
    }

    /// <summary>A typographic format with DrawText's alignment, prefix, wrapping and ellipsis flags.</summary>
    private static StringFormat CreateFormat(TextFormatFlags flags)
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

        if ((flags & TextFormatFlags.SingleLine) != 0 || (flags & TextFormatFlags.WordBreak) == 0) format.FormatFlags |= StringFormatFlags.NoWrap;
        if ((flags & TextFormatFlags.NoClipping) != 0) format.FormatFlags |= StringFormatFlags.NoClip;
        if ((flags & TextFormatFlags.RightToLeft) != 0) format.FormatFlags |= StringFormatFlags.DirectionRightToLeft;

        format.Alignment = (flags & TextFormatFlags.HorizontalCenter) != 0 ? StringAlignment.Center
            : (flags & TextFormatFlags.Right) != 0 ? StringAlignment.Far
            : StringAlignment.Near;

        format.HotkeyPrefix = (flags & TextFormatFlags.NoPrefix) != 0 ? HotkeyPrefix.None
            : (flags & TextFormatFlags.HidePrefix) != 0 ? HotkeyPrefix.Hide
            : HotkeyPrefix.Show;

        format.Trimming = (flags & TextFormatFlags.EndEllipsis) != 0 ? StringTrimming.EllipsisCharacter
            : (flags & TextFormatFlags.WordEllipsis) != 0 ? StringTrimming.EllipsisWord
            : (flags & TextFormatFlags.PathEllipsis) != 0 ? StringTrimming.EllipsisPath
            : StringTrimming.None;

        if ((flags & TextFormatFlags.ExpandTabs) != 0) format.SetTabStops(0, [8f * 4]);

        return format;
    }

    /// <summary>
    /// Vertical placement of text that does not wrap: DrawText centres or
    /// bottoms a single line by its font height, not by the glyphs' extent.
    /// </summary>
    private static Rectangle AlignSingleBlock(Graphics graphics, string text, Font font, Rectangle inner, StringFormat format, TextFormatFlags flags)
    {
        var lineCount = (flags & TextFormatFlags.SingleLine) != 0 ? 1 : text.Split('\n').Length;
        var height = lineCount * font.Height;

        var top = (flags & TextFormatFlags.VerticalCenter) != 0 ? inner.Top + (inner.Height - height) / 2
            : (flags & TextFormatFlags.Bottom) != 0 ? inner.Bottom - height
            : inner.Top;

        return new Rectangle(inner.Left, top, inner.Width, Math.Max(height, inner.Bottom - top));
    }
}
