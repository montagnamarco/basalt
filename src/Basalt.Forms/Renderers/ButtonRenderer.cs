using System.Drawing;
using System.Windows.Forms.VisualStyles;

namespace System.Windows.Forms;

/// <summary>
/// Draws a push button. Basalt has no visual style to switch to, so every
/// overload draws the classic Windows 2000 look through
/// <see cref="ControlPaint"/>, the same way <see cref="ButtonBase"/> paints
/// itself.
/// </summary>
public static class ButtonRenderer
{
    public static bool RenderMatchingApplicationState { get; set; } = true;

    public static void DrawButton(Graphics g, Rectangle bounds, PushButtonState state) =>
        DrawButton(g, bounds, buttonText: null, font: null, focused: false, state);

    public static void DrawButton(Graphics g, Rectangle bounds, bool focused, PushButtonState state) =>
        DrawButton(g, bounds, buttonText: null, font: null, focused, state);

    public static void DrawButton(Graphics g, Rectangle bounds, string? buttonText, Font? font, bool focused, PushButtonState state) =>
        DrawButton(g, bounds, buttonText, font, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter, focused, state);

    public static void DrawButton(Graphics g, Rectangle bounds, string? buttonText, Font? font, TextFormatFlags flags, bool focused, PushButtonState state) =>
        DrawButton(g, bounds, buttonText, font, flags, image: null, Rectangle.Empty, focused, state);

    public static void DrawButton(Graphics g, Rectangle bounds, Image image, Rectangle imageBounds, bool focused, PushButtonState state) =>
        DrawButton(g, bounds, buttonText: null, font: null, image, imageBounds, focused, state);

    public static void DrawButton(Graphics g, Rectangle bounds, string? buttonText, Font? font, Image image, Rectangle imageBounds, bool focused, PushButtonState state) =>
        DrawButton(g, bounds, buttonText, font, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter, image, imageBounds, focused, state);

    public static void DrawButton(
        Graphics g,
        Rectangle bounds,
        string? buttonText,
        Font? font,
        TextFormatFlags flags,
        Image? image,
        Rectangle imageBounds,
        bool focused,
        PushButtonState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        ControlPaint.DrawButton(g, bounds, ToButtonState(state));

        var content = ShrinkForContent(bounds, state);

        if (image is not null) g.DrawImage(image, imageBounds);

        if (!string.IsNullOrEmpty(buttonText))
        {
            var color = state == PushButtonState.Disabled ? SystemColors.GrayText : SystemColors.ControlText;
            TextRenderer.DrawText(g, buttonText, font, content, color, flags);
        }

        if (focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(content, -3, -3));
    }

    public static void DrawParentBackground(Graphics g, Rectangle bounds, Control childControl)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(childControl);

        using var brush = new SolidBrush(childControl.Parent?.BackColor ?? SystemColors.Control);
        g.FillRectangle(brush, bounds);
    }

    public static bool IsBackgroundPartiallyTransparent(PushButtonState state) => false;

    /// <summary>The classic button face is pushed in by 2 pixels while pressed, matching <see cref="ControlPaint.DrawButton"/>.</summary>
    private static Rectangle ShrinkForContent(Rectangle bounds, PushButtonState state)
    {
        var content = Rectangle.Inflate(bounds, -2, -2);
        if (state == PushButtonState.Pressed) content.Offset(1, 1);
        return content;
    }

    private static ButtonState ToButtonState(PushButtonState state) => state switch
    {
        PushButtonState.Pressed => ButtonState.Pushed,
        PushButtonState.Disabled => ButtonState.Inactive,
        _ => ButtonState.Normal,
    };
}
