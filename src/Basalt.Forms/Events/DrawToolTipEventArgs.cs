using System.Drawing;

namespace System.Windows.Forms;

/// <summary>An owner-drawn tool tip to paint, with helpers that paint its standard parts.</summary>
public class DrawToolTipEventArgs : EventArgs
{
    private readonly Color _backColor;
    private readonly Color _foreColor;

    public DrawToolTipEventArgs(Graphics graphics, IWin32Window? associatedWindow, Control? associatedControl, Rectangle bounds, string? toolTipText, Color backColor, Color foreColor, Font? font)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        Graphics = graphics;
        AssociatedWindow = associatedWindow;
        AssociatedControl = associatedControl;
        Bounds = bounds;
        ToolTipText = toolTipText;
        _backColor = backColor;
        _foreColor = foreColor;
        Font = font;
    }

    public Graphics Graphics { get; }

    public IWin32Window? AssociatedWindow { get; }

    public Control? AssociatedControl { get; }

    public Rectangle Bounds { get; }

    public string? ToolTipText { get; }

    public Font? Font { get; }

    public void DrawBackground()
    {
        using var brush = new SolidBrush(_backColor);
        Graphics.FillRectangle(brush, Bounds);
    }

    /// <summary>The tool tip's one-pixel frame.</summary>
    public void DrawBorder() => ControlPaint.DrawBorder(Graphics, Bounds, SystemColors.WindowFrame, ButtonBorderStyle.Solid);

    public void DrawText() => DrawText(TextFormatFlags.HidePrefix | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

    public void DrawText(TextFormatFlags flags) => TextRenderer.DrawText(Graphics, ToolTipText, Font, Bounds, _foreColor, flags);
}
