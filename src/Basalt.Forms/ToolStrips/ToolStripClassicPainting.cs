using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The classic Windows 2000 shapes a toolbar is made of: a raised or sunken
/// button face, an etched line, a column of grip dots. Shared by the system
/// and the professional renderer, which draw the same shapes in different colours.
/// </summary>
internal static class ToolStripClassicPainting
{
    public static void DrawButtonFace(Graphics graphics, Rectangle bounds, bool pressed, bool selected, bool enabled)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        if (pressed)
        {
            ControlPaint.DrawBorder3D(graphics, bounds, Border3DStyle.Sunken);

            using var face = new SolidBrush(SystemColors.ControlLight);
            graphics.FillRectangle(face, Rectangle.Inflate(bounds, -1, -1));
        }
        else if (selected && enabled)
        {
            ControlPaint.DrawBorder3D(graphics, bounds, Border3DStyle.Raised);

            using var face = new SolidBrush(SystemColors.ControlLight);
            graphics.FillRectangle(face, Rectangle.Inflate(bounds, -1, -1));
        }

        // Neither hovered nor pressed: no edge at all, the flat look of an idle toolbar button.
    }

    public static void DrawEtchedSeparator(Graphics graphics, Rectangle bounds, bool vertical)
    {
        using var dark = new Pen(SystemColors.ControlDark);
        using var light = new Pen(SystemColors.ControlLightLight);

        if (vertical)
        {
            var x = bounds.Left + bounds.Width / 2;
            graphics.DrawLine(dark, x, bounds.Top, x, bounds.Bottom);
            graphics.DrawLine(light, x + 1, bounds.Top, x + 1, bounds.Bottom);
        }
        else
        {
            var y = bounds.Top + bounds.Height / 2;
            graphics.DrawLine(dark, bounds.Left, y, bounds.Right, y);
            graphics.DrawLine(light, bounds.Left, y + 1, bounds.Right, y + 1);
        }
    }

    /// <summary>The drag grip: two short columns (or rows) of raised dots, the classic "move handle".</summary>
    public static void DrawGripDots(Graphics graphics, Rectangle bounds, ToolStripGripDisplayStyle displayStyle)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using var light = new SolidBrush(SystemColors.ControlLightLight);
        using var dark = new SolidBrush(SystemColors.ControlDark);

        const int dotSpacing = 3;

        if (displayStyle == ToolStripGripDisplayStyle.Vertical)
        {
            var x = bounds.Left + Math.Max(bounds.Width / 2 - 1, 0);

            for (var y = bounds.Top + 2; y + 1 < bounds.Bottom - 1; y += dotSpacing)
            {
                graphics.FillRectangle(dark, x, y, 1, 1);
                graphics.FillRectangle(light, x + 1, y + 1, 1, 1);
            }
        }
        else
        {
            var y = bounds.Top + Math.Max(bounds.Height / 2 - 1, 0);

            for (var x = bounds.Left + 2; x + 1 < bounds.Right - 1; x += dotSpacing)
            {
                graphics.FillRectangle(dark, x, y, 1, 1);
                graphics.FillRectangle(light, x + 1, y + 1, 1, 1);
            }
        }
    }
}
