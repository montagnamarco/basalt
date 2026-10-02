using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// What an owner-drawn ListView item (<see cref="ListView.OwnerDraw"/>) draws
/// with: its bounds and state, offered alongside DrawBackground/DrawFocusRectangle/
/// DrawText so a handler can start from the normal look and only change part of it.
/// Setting <see cref="DrawDefault"/> tells ListView to draw the item itself instead.
/// </summary>
public class DrawListViewItemEventArgs : EventArgs
{
    public DrawListViewItemEventArgs(Graphics graphics, ListViewItem item, Rectangle bounds, int itemIndex, ListViewItemStates state)
    {
        Graphics = graphics;
        Item = item;
        Bounds = bounds;
        ItemIndex = itemIndex;
        State = state;
    }

    public Rectangle Bounds { get; }

    public bool DrawDefault { get; set; }

    public Graphics Graphics { get; }

    public ListViewItem Item { get; }

    public int ItemIndex { get; }

    public ListViewItemStates State { get; }

    /// <summary>Fills the item's bounds with its background colour, as the default drawing would.</summary>
    public void DrawBackground()
    {
        var backColor = (State & ListViewItemStates.Selected) != 0 ? SystemColors.Highlight : Item.BackColor;
        using var brush = new SolidBrush(backColor);
        Graphics.FillRectangle(brush, Bounds);
    }

    /// <summary>Outlines the item with the dotted focus rectangle, when the state asks for one.</summary>
    public void DrawFocusRectangle()
    {
        if ((State & ListViewItemStates.Focused) == 0) return;

        ControlPaint.DrawFocusRectangle(Graphics, Bounds);
    }

    public void DrawText() => DrawText(TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

    public void DrawText(TextFormatFlags flags)
    {
        var foreColor = (State & ListViewItemStates.Selected) != 0 ? SystemColors.HighlightText : Item.ForeColor;
        TextRenderer.DrawText(Graphics, Item.Text, Item.Font, Bounds, foreColor, flags);
    }
}
