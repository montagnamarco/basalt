using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// What an owner-drawn item (DrawMode.OwnerDrawFixed or OwnerDrawVariable)
/// draws with: the item's bounds and state, and the colours it would have
/// used by default.
/// </summary>
/// <remarks>
/// DrawBackground and DrawFocusRectangle are offered so a handler that only
/// wants different text can still start from the normal background and
/// focus cue, exactly as ListBox and ComboBox themselves do internally.
/// </remarks>
public class DrawItemEventArgs : EventArgs, IDeviceContext, IDisposable
{
    public DrawItemEventArgs(Graphics graphics, Font? font, Rectangle rect, int index, DrawItemState state)
        : this(graphics, font, rect, index, state, SystemColors.WindowText, SystemColors.Window)
    {
    }

    public DrawItemEventArgs(Graphics graphics, Font? font, Rectangle rect, int index, DrawItemState state, Color foreColor, Color backColor)
    {
        Graphics = graphics;
        Font = font;
        Bounds = rect;
        Index = index;
        State = state;
        ForeColor = foreColor;
        BackColor = backColor;
    }

    public Color BackColor { get; }

    public Rectangle Bounds { get; }

    public Font? Font { get; }

    public Color ForeColor { get; }

    public Graphics Graphics { get; }

    public int Index { get; }

    public DrawItemState State { get; }

    public void Dispose() => Dispose(true);

    protected virtual void Dispose(bool disposing)
    {
    }

    /// <summary>Fills the item's bounds with its background colour, as the default drawing would.</summary>
    public virtual void DrawBackground()
    {
        using var brush = new SolidBrush(BackColor);
        Graphics.FillRectangle(brush, Bounds);
    }

    /// <summary>Outlines the item with the dotted focus rectangle, when the state asks for one.</summary>
    public virtual void DrawFocusRectangle()
    {
        if ((State & DrawItemState.Focus) == 0 || (State & DrawItemState.NoFocusRect) != 0) return;

        ControlPaint.DrawFocusRectangle(Graphics, Bounds);
    }

    IntPtr IDeviceContext.GetHdc() => Graphics.GetHdc();

    void IDeviceContext.ReleaseHdc() => Graphics.ReleaseHdc();
}
