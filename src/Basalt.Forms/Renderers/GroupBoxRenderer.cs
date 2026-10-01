using System.Drawing;
using System.Windows.Forms.VisualStyles;

namespace System.Windows.Forms;

/// <summary>Draws a group box frame and caption, the same classic look <see cref="GroupBox"/> paints itself.</summary>
public static class GroupBoxRenderer
{
    public static bool RenderMatchingApplicationState { get; set; } = true;

    public static void DrawGroupBox(Graphics g, Rectangle bounds, GroupBoxState state) =>
        DrawGroupBox(g, bounds, groupBoxText: null, font: null, state);

    public static void DrawGroupBox(Graphics g, Rectangle bounds, string? groupBoxText, Font? font, GroupBoxState state) =>
        DrawGroupBox(g, bounds, groupBoxText, font, DefaultTextColor(state), TextFormatFlags.Default, state);

    public static void DrawGroupBox(Graphics g, Rectangle bounds, string? groupBoxText, Font? font, Color textColor, GroupBoxState state) =>
        DrawGroupBox(g, bounds, groupBoxText, font, textColor, TextFormatFlags.Default, state);

    public static void DrawGroupBox(Graphics g, Rectangle bounds, string? groupBoxText, Font? font, TextFormatFlags flags, GroupBoxState state) =>
        DrawGroupBox(g, bounds, groupBoxText, font, DefaultTextColor(state), flags, state);

    public static void DrawGroupBox(Graphics g, Rectangle bounds, string? groupBoxText, Font? font, Color textColor, TextFormatFlags flags, GroupBoxState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        // The caption's text leaves a gap in the top edge of the frame, the
        // classic look GroupBox.OnPaint already draws for itself.
        var textSize = string.IsNullOrEmpty(groupBoxText) ? Size.Empty : TextRenderer.MeasureText(groupBoxText, font);
        var frameTop = bounds.Top + textSize.Height / 2;
        var frame = Rectangle.FromLTRB(bounds.Left, frameTop, bounds.Right, bounds.Bottom);

        ControlPaint.DrawBorder3D(g, frame, Border3DStyle.Etched);

        if (!string.IsNullOrEmpty(groupBoxText))
        {
            var textBounds = new Rectangle(bounds.Left + 8, bounds.Top, bounds.Width - 16, textSize.Height);

            using var eraser = new SolidBrush(SystemColors.Control);
            g.FillRectangle(eraser, textBounds);

            TextRenderer.DrawText(g, groupBoxText, font, textBounds, textColor, flags);
        }
    }

    public static void DrawParentBackground(Graphics g, Rectangle bounds, Control childControl)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(childControl);

        using var brush = new SolidBrush(childControl.Parent?.BackColor ?? SystemColors.Control);
        g.FillRectangle(brush, bounds);
    }

    public static bool IsBackgroundPartiallyTransparent(GroupBoxState state) => false;

    private static Color DefaultTextColor(GroupBoxState state) =>
        state == GroupBoxState.Disabled ? SystemColors.GrayText : SystemColors.ControlText;
}
