using System.Drawing;
using System.Windows.Forms.VisualStyles;

namespace System.Windows.Forms;

/// <summary>Draws a combo box's drop-down button and edit area in the classic Windows 2000 look.</summary>
public static class ComboBoxRenderer
{
    /// <summary>Always false: Basalt never loads a visual style, so ComboBoxRenderer always draws the classic look.</summary>
    public static bool IsSupported => false;

    public static void DrawDropDownButton(Graphics g, Rectangle bounds, ComboBoxState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        ControlPaint.DrawComboButton(g, bounds, ToButtonState(state));
    }

    public static void DrawTextBox(Graphics g, Rectangle bounds, ComboBoxState state) =>
        DrawTextBox(g, bounds, comboBoxText: null, font: null, state);

    public static void DrawTextBox(Graphics g, Rectangle bounds, string? comboBoxText, Font? font, ComboBoxState state) =>
        DrawTextBox(g, bounds, comboBoxText, font, Rectangle.Inflate(bounds, -2, -2), TextFormatFlags.Default, state);

    public static void DrawTextBox(Graphics g, Rectangle bounds, string? comboBoxText, Font? font, Rectangle textBounds, ComboBoxState state) =>
        DrawTextBox(g, bounds, comboBoxText, font, textBounds, TextFormatFlags.Default, state);

    public static void DrawTextBox(Graphics g, Rectangle bounds, string? comboBoxText, Font? font, TextFormatFlags flags, ComboBoxState state) =>
        DrawTextBox(g, bounds, comboBoxText, font, Rectangle.Inflate(bounds, -2, -2), flags, state);

    public static void DrawTextBox(Graphics g, Rectangle bounds, string? comboBoxText, Font? font, Rectangle textBounds, TextFormatFlags flags, ComboBoxState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        ControlPaint.DrawBorder3D(g, bounds, Border3DStyle.Sunken);

        var fillColor = state == ComboBoxState.Disabled ? SystemColors.Control : SystemColors.Window;
        using var fill = new SolidBrush(fillColor);
        g.FillRectangle(fill, Rectangle.Inflate(bounds, -2, -2));

        if (!string.IsNullOrEmpty(comboBoxText))
        {
            var color = state == ComboBoxState.Disabled ? SystemColors.GrayText : SystemColors.WindowText;
            TextRenderer.DrawText(g, comboBoxText, font, textBounds, color, flags);
        }
    }

    private static ButtonState ToButtonState(ComboBoxState state) => state switch
    {
        ComboBoxState.Pressed => ButtonState.Pushed,
        ComboBoxState.Disabled => ButtonState.Inactive,
        _ => ButtonState.Normal,
    };
}
