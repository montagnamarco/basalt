using System.Drawing;
using System.Windows.Forms.VisualStyles;

namespace System.Windows.Forms;

/// <summary>Draws a text box's classic sunken frame, the way <see cref="TextBoxBase"/> paints its native frame.</summary>
public static class TextBoxRenderer
{
    /// <summary>Always false: Basalt never loads a visual style, so TextBoxRenderer always draws the classic look.</summary>
    public static bool IsSupported => false;

    public static void DrawTextBox(Graphics g, Rectangle bounds, TextBoxState state) =>
        DrawTextBox(g, bounds, textBoxText: null, font: null, state);

    public static void DrawTextBox(Graphics g, Rectangle bounds, string? textBoxText, Font? font, TextBoxState state) =>
        DrawTextBox(g, bounds, textBoxText, font, Rectangle.Inflate(bounds, -2, -2), TextFormatFlags.Default, state);

    public static void DrawTextBox(Graphics g, Rectangle bounds, string? textBoxText, Font? font, Rectangle textBounds, TextBoxState state) =>
        DrawTextBox(g, bounds, textBoxText, font, textBounds, TextFormatFlags.Default, state);

    public static void DrawTextBox(Graphics g, Rectangle bounds, string? textBoxText, Font? font, TextFormatFlags flags, TextBoxState state) =>
        DrawTextBox(g, bounds, textBoxText, font, Rectangle.Inflate(bounds, -2, -2), flags, state);

    public static void DrawTextBox(Graphics g, Rectangle bounds, string? textBoxText, Font? font, Rectangle textBounds, TextFormatFlags flags, TextBoxState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        ControlPaint.DrawBorder3D(g, bounds, Border3DStyle.Sunken);

        var fillColor = state is TextBoxState.Disabled or TextBoxState.Readonly ? SystemColors.Control : SystemColors.Window;
        using var fill = new SolidBrush(fillColor);
        g.FillRectangle(fill, Rectangle.Inflate(bounds, -2, -2));

        if (!string.IsNullOrEmpty(textBoxText))
        {
            var color = state == TextBoxState.Disabled ? SystemColors.GrayText : SystemColors.WindowText;
            TextRenderer.DrawText(g, textBoxText, font, textBounds, color, flags);
        }
    }
}
