using System.Drawing;
using System.Windows.Forms.VisualStyles;

namespace System.Windows.Forms;

/// <summary>Draws a tab item and the pane behind it, the same classic look <see cref="TabControl"/> paints itself.</summary>
public static class TabRenderer
{
    /// <summary>Always false: Basalt never loads a visual style, so TabRenderer always draws the classic look.</summary>
    public static bool IsSupported => false;

    public static void DrawTabItem(Graphics g, Rectangle bounds, TabItemState state) =>
        DrawTabItem(g, bounds, tabItemText: null, font: null, focused: false, state);

    public static void DrawTabItem(Graphics g, Rectangle bounds, bool focused, TabItemState state) =>
        DrawTabItem(g, bounds, tabItemText: null, font: null, focused, state);

    public static void DrawTabItem(Graphics g, Rectangle bounds, string? tabItemText, Font? font, TabItemState state) =>
        DrawTabItem(g, bounds, tabItemText, font, focused: false, state);

    public static void DrawTabItem(Graphics g, Rectangle bounds, string? tabItemText, Font? font, bool focused, TabItemState state) =>
        DrawTabItem(g, bounds, tabItemText, font, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter, focused, state);

    public static void DrawTabItem(Graphics g, Rectangle bounds, string? tabItemText, Font? font, TextFormatFlags flags, bool focused, TabItemState state) =>
        DrawTabItem(g, bounds, tabItemText, font, flags, image: null, Rectangle.Empty, focused, state);

    public static void DrawTabItem(Graphics g, Rectangle bounds, Image image, Rectangle imageRectangle, bool focused, TabItemState state) =>
        DrawTabItem(g, bounds, tabItemText: null, font: null, image, imageRectangle, focused, state);

    public static void DrawTabItem(Graphics g, Rectangle bounds, string? tabItemText, Font? font, Image image, Rectangle imageRectangle, bool focused, TabItemState state) =>
        DrawTabItem(g, bounds, tabItemText, font, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter, image, imageRectangle, focused, state);

    public static void DrawTabItem(
        Graphics g,
        Rectangle bounds,
        string? tabItemText,
        Font? font,
        TextFormatFlags flags,
        Image? image,
        Rectangle imageRectangle,
        bool focused,
        TabItemState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        // A selected tab merges into the pane below it: its bottom edge is
        // left open, the same way TabControl draws the active tab.
        var sides = state == TabItemState.Selected
            ? Border3DSide.Top | Border3DSide.Left | Border3DSide.Right | Border3DSide.Middle
            : Border3DSide.All;

        ControlPaint.DrawBorder3D(g, bounds, Border3DStyle.Raised, sides);

        if (image is not null) g.DrawImage(image, imageRectangle);

        if (!string.IsNullOrEmpty(tabItemText))
        {
            var color = state == TabItemState.Disabled ? SystemColors.GrayText : SystemColors.ControlText;
            TextRenderer.DrawText(g, tabItemText, font, bounds, color, flags);
        }

        if (focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(bounds, -3, -3));
    }

    public static void DrawTabPage(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        ControlPaint.DrawBorder3D(g, bounds, Border3DStyle.Raised);
    }
}
