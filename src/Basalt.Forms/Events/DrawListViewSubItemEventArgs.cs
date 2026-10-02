using System.Drawing;

namespace System.Windows.Forms;

/// <summary>What an owner-drawn ListView sub-item (Details view, <see cref="ListView.OwnerDraw"/>) draws with.</summary>
public class DrawListViewSubItemEventArgs : EventArgs
{
    public DrawListViewSubItemEventArgs(
        Graphics graphics,
        Rectangle bounds,
        ListViewItem? item,
        ListViewItem.ListViewSubItem? subItem,
        int itemIndex,
        int columnIndex,
        ColumnHeader? header,
        ListViewItemStates itemState)
    {
        Graphics = graphics;
        Bounds = bounds;
        Item = item;
        SubItem = subItem;
        ItemIndex = itemIndex;
        ColumnIndex = columnIndex;
        Header = header;
        ItemState = itemState;
    }

    public Rectangle Bounds { get; }

    public int ColumnIndex { get; }

    public bool DrawDefault { get; set; }

    public Graphics Graphics { get; }

    public ColumnHeader? Header { get; }

    public ListViewItem? Item { get; }

    public int ItemIndex { get; }

    public ListViewItemStates ItemState { get; }

    public ListViewItem.ListViewSubItem? SubItem { get; }

    public void DrawBackground()
    {
        var backColor = (ItemState & ListViewItemStates.Selected) != 0 ? SystemColors.Highlight : SubItem?.BackColor ?? SystemColors.Window;
        using var brush = new SolidBrush(backColor);
        Graphics.FillRectangle(brush, Bounds);
    }

    public void DrawFocusRectangle(Rectangle bounds)
    {
        if ((ItemState & ListViewItemStates.Focused) == 0) return;

        ControlPaint.DrawFocusRectangle(Graphics, bounds);
    }

    public void DrawText() => DrawText(TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

    public void DrawText(TextFormatFlags flags)
    {
        var foreColor = (ItemState & ListViewItemStates.Selected) != 0 ? SystemColors.HighlightText : SubItem?.ForeColor ?? SystemColors.WindowText;
        var font = SubItem?.Font ?? Item?.Font ?? Control.DefaultFont;
        TextRenderer.DrawText(Graphics, SubItem?.Text ?? string.Empty, font, Bounds, foreColor, flags);
    }
}
