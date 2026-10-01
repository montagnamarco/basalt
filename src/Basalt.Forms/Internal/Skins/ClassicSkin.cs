using System.Drawing;

namespace System.Windows.Forms.Internal.Skins;

/// <summary>
/// The Windows 2000 look: flat colours from SystemColors and the two-line
/// three-dimensional edges of the classic theme.
/// </summary>
internal sealed class ClassicSkin : Skin
{
    public override void DrawNonClientFrame(Graphics graphics, Size outerSize, int style, int exStyle)
    {
        var bounds = new Rectangle(Point.Empty, outerSize);

        // Outside in, as Windows draws them: border, then the edges inside it.
        if ((style & WindowStyles.Border) != 0 && (style & WindowStyles.Caption) != WindowStyles.Caption)
        {
            DrawEdge(graphics, bounds, SystemColors.WindowFrame, SystemColors.WindowFrame);
            bounds.Inflate(-1, -1);
        }

        if ((exStyle & WindowStyles.ExClientEdge) != 0)
        {
            DrawEdge(graphics, bounds, SystemColors.ControlDark, SystemColors.ControlLightLight);
            bounds.Inflate(-1, -1);
            DrawEdge(graphics, bounds, SystemColors.ControlDarkDark, SystemColors.ControlLight);
            bounds.Inflate(-1, -1);
        }

        if ((exStyle & WindowStyles.ExStaticEdge) != 0)
        {
            DrawEdge(graphics, bounds, SystemColors.ControlDark, SystemColors.ControlLightLight);
            bounds.Inflate(-1, -1);
        }

        DrawScrollBarPlaceholders(graphics, bounds, style);
    }

    /// <summary>
    /// One line of a three-dimensional edge, as Win32's DrawEdge lays it: the
    /// top and left in one colour up to the last pixel, the bottom and right
    /// in the other over their whole length, so that colour owns the corners.
    /// Filled as one-pixel rectangles, which have no end-point ambiguity.
    /// </summary>
    private static void DrawEdge(Graphics graphics, Rectangle bounds, Color topLeft, Color bottomRight)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using var first = new SolidBrush(topLeft);
        using var second = new SolidBrush(bottomRight);

        graphics.FillRectangle(first, bounds.Left, bounds.Top, bounds.Width - 1, 1);
        graphics.FillRectangle(first, bounds.Left, bounds.Top, 1, bounds.Height - 1);
        graphics.FillRectangle(second, bounds.Left, bounds.Bottom - 1, bounds.Width, 1);
        graphics.FillRectangle(second, bounds.Right - 1, bounds.Top, 1, bounds.Height);
    }

    /// <summary>The area the window's scroll bars occupy, until scroll bars are drawn.</summary>
    private static void DrawScrollBarPlaceholders(Graphics graphics, Rectangle inside, int style)
    {
        using var brush = new SolidBrush(SystemColors.ScrollBar);

        if ((style & WindowStyles.VerticalScroll) != 0)
        {
            graphics.FillRectangle(brush, inside.Right - NonClientArea.ScrollBarSize, inside.Top, NonClientArea.ScrollBarSize, inside.Height);
        }

        if ((style & WindowStyles.HorizontalScroll) != 0)
        {
            graphics.FillRectangle(brush, inside.Left, inside.Bottom - NonClientArea.ScrollBarSize, inside.Width, NonClientArea.ScrollBarSize);
        }
    }
}
