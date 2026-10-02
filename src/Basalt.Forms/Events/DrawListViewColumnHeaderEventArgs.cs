using System.Drawing;

namespace System.Windows.Forms;

/// <summary>What an owner-drawn ListView column header (Details view, <see cref="ListView.OwnerDraw"/>) draws with.</summary>
public class DrawListViewColumnHeaderEventArgs : EventArgs
{
    public DrawListViewColumnHeaderEventArgs(
        Graphics graphics,
        Rectangle bounds,
        int columnIndex,
        ColumnHeader? header,
        ListViewItemStates state,
        Color foreColor,
        Color backColor,
        Font? font)
    {
        Graphics = graphics;
        Bounds = bounds;
        ColumnIndex = columnIndex;
        Header = header;
        State = state;
        ForeColor = foreColor;
        BackColor = backColor;
        Font = font;
    }

    public Color BackColor { get; }

    public Rectangle Bounds { get; }

    public int ColumnIndex { get; }

    public bool DrawDefault { get; set; }

    public Font? Font { get; }

    public Color ForeColor { get; }

    public Graphics Graphics { get; }

    public ColumnHeader? Header { get; }

    public ListViewItemStates State { get; }

    public void DrawBackground()
    {
        ControlPaint.DrawButton(Graphics, Bounds, ButtonState.Normal);
    }

    public void DrawText() => DrawText(TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

    public void DrawText(TextFormatFlags flags)
    {
        TextRenderer.DrawText(Graphics, Header?.Text ?? string.Empty, Font, Bounds, ForeColor, flags);
    }
}
