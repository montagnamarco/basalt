using System.Drawing;
using System.Windows.Forms.VisualStyles;

namespace System.Windows.Forms;

/// <summary>Draws a check box glyph and its text, the same classic look <see cref="CheckBox"/> paints itself.</summary>
public static class CheckBoxRenderer
{
    private const int GlyphSize = 13;

    public static bool RenderMatchingApplicationState { get; set; } = true;

    public static void DrawCheckBox(Graphics g, Point glyphLocation, CheckBoxState state) =>
        DrawCheckBox(g, glyphLocation, Rectangle.Empty, checkBoxText: null, font: null, focused: false, state);

    public static void DrawCheckBox(Graphics g, Point glyphLocation, Rectangle textBounds, string? checkBoxText, Font? font, bool focused, CheckBoxState state) =>
        DrawCheckBox(g, glyphLocation, textBounds, checkBoxText, font, TextFormatFlags.Default, focused, state);

    public static void DrawCheckBox(
        Graphics g,
        Point glyphLocation,
        Rectangle textBounds,
        string? checkBoxText,
        Font? font,
        TextFormatFlags flags,
        bool focused,
        CheckBoxState state) =>
        DrawCheckBox(g, glyphLocation, textBounds, checkBoxText, font, flags, image: null, Rectangle.Empty, focused, state);

    public static void DrawCheckBox(
        Graphics g,
        Point glyphLocation,
        Rectangle textBounds,
        string? checkBoxText,
        Font? font,
        Image image,
        Rectangle imageBounds,
        bool focused,
        CheckBoxState state) =>
        DrawCheckBox(g, glyphLocation, textBounds, checkBoxText, font, TextFormatFlags.Default, image, imageBounds, focused, state);

    public static void DrawCheckBox(
        Graphics g,
        Point glyphLocation,
        Rectangle textBounds,
        string? checkBoxText,
        Font? font,
        TextFormatFlags flags,
        Image? image,
        Rectangle imageBounds,
        bool focused,
        CheckBoxState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        var glyph = new Rectangle(glyphLocation.X, glyphLocation.Y, GlyphSize, GlyphSize);
        var buttonState = ToButtonState(state);

        if (IsMixed(state)) ControlPaint.DrawMixedCheckBox(g, glyph, buttonState);
        else ControlPaint.DrawCheckBox(g, glyph, buttonState);

        if (image is not null) g.DrawImage(image, imageBounds);

        if (!string.IsNullOrEmpty(checkBoxText))
        {
            var color = IsDisabled(state) ? SystemColors.GrayText : SystemColors.ControlText;
            TextRenderer.DrawText(g, checkBoxText, font, textBounds, color, flags);
        }

        if (focused) ControlPaint.DrawFocusRectangle(g, textBounds);
    }

    public static void DrawParentBackground(Graphics g, Rectangle bounds, Control childControl)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(childControl);

        using var brush = new SolidBrush(childControl.Parent?.BackColor ?? SystemColors.Control);
        g.FillRectangle(brush, bounds);
    }

    public static Size GetGlyphSize(Graphics g, CheckBoxState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        return new Size(GlyphSize, GlyphSize);
    }

    public static bool IsBackgroundPartiallyTransparent(CheckBoxState state) => false;

    private static bool IsMixed(CheckBoxState state) =>
        state is CheckBoxState.MixedNormal or CheckBoxState.MixedHot or CheckBoxState.MixedPressed or CheckBoxState.MixedDisabled;

    private static bool IsDisabled(CheckBoxState state) =>
        state is CheckBoxState.UncheckedDisabled or CheckBoxState.CheckedDisabled or CheckBoxState.MixedDisabled;

    private static bool IsChecked(CheckBoxState state) =>
        state is CheckBoxState.CheckedNormal or CheckBoxState.CheckedHot or CheckBoxState.CheckedPressed or CheckBoxState.CheckedDisabled
            or CheckBoxState.MixedNormal or CheckBoxState.MixedHot or CheckBoxState.MixedPressed or CheckBoxState.MixedDisabled;

    private static bool IsPressed(CheckBoxState state) =>
        state is CheckBoxState.UncheckedPressed or CheckBoxState.CheckedPressed or CheckBoxState.MixedPressed;

    private static ButtonState ToButtonState(CheckBoxState state)
    {
        var result = ButtonState.Normal;
        if (IsChecked(state)) result |= ButtonState.Checked;
        if (IsPressed(state)) result |= ButtonState.Pushed;
        if (IsDisabled(state)) result |= ButtonState.Inactive;
        return result;
    }
}
