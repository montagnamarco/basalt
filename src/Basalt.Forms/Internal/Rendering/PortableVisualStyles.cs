using System.Drawing;
using System.Drawing.Drawing2D;

namespace System.Windows.Forms.Internal.Rendering;

internal enum PortableVisualStylePlatform
{
    Windows,
    MacOS,
    Linux
}

internal sealed record PortableVisualStylePalette(
    Color Face,
    Color HotFace,
    Color PressedFace,
    Color Border,
    Color Accent,
    Color DisabledText,
    string FontFamily,
    float FontSize)
{
    internal Color Field => Color.White;
    internal PortableVisualStylePlatform Platform { get; init; }
    internal Color Text => Platform switch
    {
        PortableVisualStylePlatform.MacOS => Color.FromArgb(29, 29, 31),
        PortableVisualStylePlatform.Linux => Color.FromArgb(46, 52, 54),
        _ => Color.Black
    };
    internal Color Selection => Accent;
    internal Color SelectedText => Color.White;
    internal Color Surface => Platform == PortableVisualStylePlatform.MacOS ? Color.FromArgb(237, 237, 237) : Face;
}

/// <summary>Portable managed painting; this does not expose native theme handles.</summary>
internal static class PortableVisualStyles
{
    private static readonly Lazy<Font> WindowsFont = new(() => CreateDefaultFont(PortableVisualStylePlatform.Windows));
    private static readonly Lazy<Font> MacOSFont = new(() => CreateDefaultFont(PortableVisualStylePlatform.MacOS));
    private static readonly Lazy<Font> LinuxFont = new(() => CreateDefaultFont(PortableVisualStylePlatform.Linux));

    internal static PortableVisualStylePlatform CurrentPlatform => OperatingSystem.IsWindows()
        ? PortableVisualStylePlatform.Windows
        : OperatingSystem.IsMacOS() ? PortableVisualStylePlatform.MacOS : PortableVisualStylePlatform.Linux;

    internal static PortableVisualStylePalette Palette(PortableVisualStylePlatform platform) => platform switch
    {
        PortableVisualStylePlatform.Windows => new(
            Color.FromArgb(240, 240, 240), Color.FromArgb(229, 241, 251), Color.FromArgb(204, 228, 247),
            Color.FromArgb(112, 112, 112), Color.FromArgb(0, 120, 215), Color.FromArgb(109, 109, 109), "Segoe UI", 9),
        PortableVisualStylePlatform.MacOS => new(
            Color.FromArgb(250, 250, 250), Color.FromArgb(242, 247, 255), Color.FromArgb(218, 231, 250),
            Color.FromArgb(174, 174, 174), Color.FromArgb(0, 122, 255), Color.FromArgb(142, 142, 147), "Helvetica Neue", 10)
        { Platform = PortableVisualStylePlatform.MacOS },
        _ => new(
            Color.FromArgb(246, 245, 244), Color.FromArgb(255, 255, 255), Color.FromArgb(222, 221, 218),
            Color.FromArgb(154, 153, 150), Color.FromArgb(53, 132, 228), Color.FromArgb(146, 149, 149), "DejaVu Sans", 9)
        { Platform = PortableVisualStylePlatform.Linux }
    };

    internal static Font DefaultFont(PortableVisualStylePlatform platform) => platform switch
    {
        PortableVisualStylePlatform.Windows => WindowsFont.Value,
        PortableVisualStylePlatform.MacOS => MacOSFont.Value,
        _ => LinuxFont.Value
    };

    private static Font CreateDefaultFont(PortableVisualStylePlatform platform)
    {
        var palette = Palette(platform);
        return new Font(palette.FontFamily, palette.FontSize, FontStyle.Regular, GraphicsUnit.Point)
            .AsSystemFont("DefaultFont");
    }

    /// <param name="isDefault">The control is the form's default/accept button
    /// (<c>Button.IsDefault</c>); on macOS a default button gets the solid
    /// accent fill real buttons show. Keep this meaning distinct from
    /// <paramref name="emphasized"/>: a thumb or item that merely wants the
    /// same accent treatment while focused or active is not a default button.</param>
    /// <param name="emphasized">Anything other than "is the default button" that
    /// should still draw with the accent border/fill (for example, a focused
    /// TrackBar thumb). Named separately so callers never have to overload
    /// <paramref name="isDefault"/> to get the same pixels.</param>
    internal static void PaintButton(Graphics graphics, Rectangle bounds, PortableVisualStylePalette palette,
        PortableVisualStylePlatform platform, bool hot, bool pressed, bool enabled, bool isDefault, bool emphasized = false)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var prominent = platform == PortableVisualStylePlatform.MacOS && (isDefault || emphasized) && enabled;
        var face = palette.Face;
        if (prominent)
        {
            face = pressed
                ? Color.FromArgb(palette.Accent.R * 4 / 5, palette.Accent.G * 4 / 5, palette.Accent.B * 4 / 5)
                : palette.Accent;
        }
        else if (enabled)
        {
            face = pressed ? palette.PressedFace : hot ? palette.HotFace : palette.Face;
        }

        using Brush background = platform == PortableVisualStylePlatform.Windows
            ? new LinearGradientBrush(bounds, BlendWithWhite(face), face, LinearGradientMode.Vertical)
            : new SolidBrush(face);
        using var border = new Pen(isDefault || emphasized || hot ? palette.Accent : palette.Border);
        if (platform == PortableVisualStylePlatform.MacOS && bounds.Width > 10 && bounds.Height > 10)
        {
            using var shape = RoundedButton(bounds);
            graphics.FillPath(background, shape);
            graphics.DrawPath(border, shape);
        }
        else
        {
            graphics.FillRectangle(background, bounds);
            graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        }
    }

    private static Color BlendWithWhite(Color color) => Color.FromArgb(
        (color.R + 255) / 2, (color.G + 255) / 2, (color.B + 255) / 2);

    private static GraphicsPath RoundedButton(Rectangle bounds)
    {
        // Reproduces the ~4px corner radius of a macOS bordered button; GraphicsPath.AddArc
        // takes a bounding box for the corner, so the radius is doubled into a diameter.
        const int CornerDiameter = 8;
        var shape = new GraphicsPath();
        shape.AddArc(bounds.Left, bounds.Top, CornerDiameter, CornerDiameter, 180, 90);
        shape.AddArc(bounds.Right - CornerDiameter - 1, bounds.Top, CornerDiameter, CornerDiameter, 270, 90);
        shape.AddArc(bounds.Right - CornerDiameter - 1, bounds.Bottom - CornerDiameter - 1, CornerDiameter, CornerDiameter, 0, 90);
        shape.AddArc(bounds.Left, bounds.Bottom - CornerDiameter - 1, CornerDiameter, CornerDiameter, 90, 90);
        shape.CloseFigure();
        return shape;
    }

    /// <summary>How far the inner fill (the selected dot, the mixed square, or the tick's anchor points)
    /// sits inside the glyph's outer border, reproducing the narrow margin native checkboxes leave
    /// around their inner mark on both Windows and macOS.</summary>
    private const int GlyphMarkInset = 3;

    /// <summary>Where the checkmark's downward stroke bottoms out, one pixel lower than
    /// <see cref="GlyphMarkInset"/> so the tick's low point reads as a distinct vertex
    /// rather than touching the same inset line as its two end points.</summary>
    private const int CheckmarkLowPointInset = 4;

    internal static void PaintGlyph(Graphics graphics, Rectangle bounds, PortableVisualStylePalette palette,
        bool radio, bool selected, bool mixed, bool pressed, bool enabled)
    {
        var outline = new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        var filledSelection = (selected || mixed) && palette.Platform != PortableVisualStylePlatform.Windows;
        using var face = new SolidBrush(filledSelection ? enabled ? palette.Accent : palette.DisabledText
            : pressed ? palette.PressedFace : Color.White);
        using var border = new Pen(enabled ? palette.Border : palette.DisabledText);
        if (radio)
        {
            graphics.FillEllipse(face, outline);
            graphics.DrawEllipse(border, outline);
        }
        else
        {
            graphics.FillRectangle(face, outline);
            graphics.DrawRectangle(border, outline);
        }

        if (!selected && !mixed)
        {
            return;
        }

        using var mark = new SolidBrush(filledSelection ? palette.SelectedText : enabled ? palette.Accent : palette.DisabledText);
        var inner = Rectangle.Inflate(outline, -GlyphMarkInset, -GlyphMarkInset);
        if (radio)
        {
            graphics.FillEllipse(mark, inner);
        }
        else if (mixed)
        {
            graphics.FillRectangle(mark, inner);
        }
        else
        {
            using var tick = new Pen(filledSelection ? palette.SelectedText : enabled ? palette.Accent : palette.DisabledText, 2);
            graphics.DrawLine(tick, bounds.Left + GlyphMarkInset, bounds.Top + bounds.Height / 2,
                bounds.Left + bounds.Width / 2 - 1, bounds.Bottom - CheckmarkLowPointInset);
            graphics.DrawLine(tick, bounds.Left + bounds.Width / 2 - 1, bounds.Bottom - CheckmarkLowPointInset,
                bounds.Right - GlyphMarkInset, bounds.Top + GlyphMarkInset);
        }
    }
}
