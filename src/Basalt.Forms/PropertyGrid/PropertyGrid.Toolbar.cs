using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The toolbar's two buttons: Categorized (PropertySort becomes
/// CategorizedAlphabetical) and Alphabetical (PropertySort becomes
/// Alphabetical) - the pair every classic PropertyGrid shows at its top
/// left. PropertyTabs beyond the default one, and the hot-commands pane
/// Microsoft draws to their right, are left out (see the family's report).
/// </summary>
public partial class PropertyGrid
{
    private const int ToolbarButtonMargin = 3;

    private int ToolbarButtonSize => (_largeButtons ? ToolbarHeightLarge : ToolbarHeightSmall) - 2 * ToolbarButtonMargin;

    internal Rectangle CategorizedButtonBounds => new(ToolbarButtonMargin, ToolbarButtonMargin, ToolbarButtonSize, ToolbarButtonSize);

    internal Rectangle AlphabeticalButtonBounds => new(ToolbarButtonMargin * 2 + ToolbarButtonSize, ToolbarButtonMargin, ToolbarButtonSize, ToolbarButtonSize);

    private void PaintToolbar(Graphics graphics)
    {
        var bounds = ToolbarBounds;

        using (var face = new SolidBrush(BackColor))
        {
            graphics.FillRectangle(face, bounds);
        }

        using var bottomLine = new Pen(ViewBorderColor);
        graphics.DrawLine(bottomLine, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);

        var categorized = _propertySort is PropertySort.Categorized or PropertySort.CategorizedAlphabetical;
        PaintToolbarButton(graphics, CategorizedButtonBounds, categorized, DrawCategorizedGlyph);
        PaintToolbarButton(graphics, AlphabeticalButtonBounds, !categorized, DrawAlphabeticalGlyph);
    }

    private void PaintToolbarButton(Graphics graphics, Rectangle bounds, bool pressed, Action<Graphics, Rectangle> drawGlyph)
    {
        if (PortableVisualStylesEnabled)
        {
            Internal.Rendering.PortableVisualStyles.PaintButton(graphics, bounds,
                Internal.Rendering.PortableVisualStyles.Palette(PortableVisualStylePlatform), PortableVisualStylePlatform,
                hot: false, pressed, Enabled, isDefault: false);
        }
        else if (pressed)
        {
            ControlPaint.DrawBorder3D(graphics, bounds, Border3DStyle.SunkenOuter);
        }

        var inset = new Rectangle(bounds.X + 4, bounds.Y + 4, bounds.Width - 8, bounds.Height - 8);
        drawGlyph(graphics, inset);
    }

    /// <summary>Two short groups of lines, standing for properties gathered under separate categories.</summary>
    private void DrawCategorizedGlyph(Graphics graphics, Rectangle bounds)
    {
        using var pen = new Pen(ToolbarGlyphColor);

        var step = Math.Max(bounds.Height / 5, 1);
        var y = bounds.Top;

        for (var line = 0; line < 2; line++)
        {
            graphics.DrawLine(pen, bounds.Left, y, bounds.Right, y);
            y += step;
        }

        y += step;

        for (var line = 0; line < 2; line++)
        {
            graphics.DrawLine(pen, bounds.Left, y, bounds.Right, y);
            y += step;
        }
    }

    /// <summary>Evenly spaced lines, standing for one flat alphabetical list.</summary>
    private void DrawAlphabeticalGlyph(Graphics graphics, Rectangle bounds)
    {
        using var pen = new Pen(ToolbarGlyphColor);

        var step = Math.Max(bounds.Height / 4, 1);
        for (var y = bounds.Top; y < bounds.Bottom; y += step)
        {
            graphics.DrawLine(pen, bounds.Left, y, bounds.Right, y);
        }
    }

    private Color ToolbarGlyphColor => PortableVisualStylesEnabled
        ? Enabled ? ViewForeColor : DisabledItemForeColor
        : SystemColors.ControlText;

    private bool HandleToolbarClick(Point location)
    {
        if (!_toolbarVisible || !ToolbarBounds.Contains(location)) return false;

        if (CategorizedButtonBounds.Contains(location))
        {
            PropertySort = PropertySort.CategorizedAlphabetical;
            return true;
        }

        if (AlphabeticalButtonBounds.Contains(location))
        {
            PropertySort = PropertySort.Alphabetical;
            return true;
        }

        return true;
    }
}
