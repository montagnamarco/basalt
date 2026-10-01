using System.Drawing;
using System.Windows.Forms.VisualStyles;

namespace System.Windows.Forms;

/// <summary>Draws a radio button glyph and its text, the same classic look <see cref="RadioButton"/> paints itself.</summary>
public static class RadioButtonRenderer
{
    private const int GlyphSize = 12;

    public static bool RenderMatchingApplicationState { get; set; } = true;

    public static void DrawParentBackground(Graphics g, Rectangle bounds, Control childControl)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(childControl);

        using var brush = new SolidBrush(childControl.Parent?.BackColor ?? SystemColors.Control);
        g.FillRectangle(brush, bounds);
    }

    public static void DrawRadioButton(Graphics g, Point glyphLocation, RadioButtonState state) =>
        DrawRadioButton(g, glyphLocation, Rectangle.Empty, radioButtonText: null, font: null, focused: false, state);

    public static void DrawRadioButton(Graphics g, Point glyphLocation, Rectangle textBounds, string? radioButtonText, Font? font, bool focused, RadioButtonState state) =>
        DrawRadioButton(g, glyphLocation, textBounds, radioButtonText, font, TextFormatFlags.Default, focused, state);

    public static void DrawRadioButton(
        Graphics g,
        Point glyphLocation,
        Rectangle textBounds,
        string? radioButtonText,
        Font? font,
        TextFormatFlags flags,
        bool focused,
        RadioButtonState state) =>
        DrawRadioButton(g, glyphLocation, textBounds, radioButtonText, font, flags, image: null, Rectangle.Empty, focused, state);

    public static void DrawRadioButton(
        Graphics g,
        Point glyphLocation,
        Rectangle textBounds,
        string? radioButtonText,
        Font? font,
        Image image,
        Rectangle imageBounds,
        bool focused,
        RadioButtonState state) =>
        DrawRadioButton(g, glyphLocation, textBounds, radioButtonText, font, TextFormatFlags.Default, image, imageBounds, focused, state);

    public static void DrawRadioButton(
        Graphics g,
        Point glyphLocation,
        Rectangle textBounds,
        string? radioButtonText,
        Font? font,
        TextFormatFlags flags,
        Image? image,
        Rectangle imageBounds,
        bool focused,
        RadioButtonState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        var glyph = new Rectangle(glyphLocation.X, glyphLocation.Y, GlyphSize, GlyphSize);
        ControlPaint.DrawRadioButton(g, glyph, ToButtonState(state));

        if (image is not null) g.DrawImage(image, imageBounds);

        if (!string.IsNullOrEmpty(radioButtonText))
        {
            var color = IsDisabled(state) ? SystemColors.GrayText : SystemColors.ControlText;
            TextRenderer.DrawText(g, radioButtonText, font, textBounds, color, flags);
        }

        if (focused) ControlPaint.DrawFocusRectangle(g, textBounds);
    }

    public static Size GetGlyphSize(Graphics g, RadioButtonState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        return new Size(GlyphSize, GlyphSize);
    }

    public static bool IsBackgroundPartiallyTransparent(RadioButtonState state) => false;

    private static bool IsDisabled(RadioButtonState state) =>
        state is RadioButtonState.UncheckedDisabled or RadioButtonState.CheckedDisabled;

    private static bool IsChecked(RadioButtonState state) =>
        state is RadioButtonState.CheckedNormal or RadioButtonState.CheckedHot or RadioButtonState.CheckedPressed or RadioButtonState.CheckedDisabled;

    private static bool IsPressed(RadioButtonState state) =>
        state is RadioButtonState.UncheckedPressed or RadioButtonState.CheckedPressed;

    private static ButtonState ToButtonState(RadioButtonState state)
    {
        var result = ButtonState.Normal;
        if (IsChecked(state)) result |= ButtonState.Checked;
        if (IsPressed(state)) result |= ButtonState.Pushed;
        if (IsDisabled(state)) result |= ButtonState.Inactive;
        return result;
    }
}
