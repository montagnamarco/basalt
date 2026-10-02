using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>OwnerDraw: lets a handler paint items, sub-items and column headers instead of ListView's own drawing.</summary>
public partial class ListView
{
    private bool _ownerDraw;

    [DefaultValue(false)]
    public bool OwnerDraw
    {
        get => _ownerDraw;
        set
        {
            if (value == _ownerDraw) return;

            _ownerDraw = value;
            Invalidate();
        }
    }

    public event DrawListViewColumnHeaderEventHandler? DrawColumnHeader
    {
        add => Events.AddHandler(s_drawColumnHeaderEvent, value);
        remove => Events.RemoveHandler(s_drawColumnHeaderEvent, value);
    }

    public event DrawListViewItemEventHandler? DrawItem
    {
        add => Events.AddHandler(s_drawItemEvent, value);
        remove => Events.RemoveHandler(s_drawItemEvent, value);
    }

    public event DrawListViewSubItemEventHandler? DrawSubItem
    {
        add => Events.AddHandler(s_drawSubItemEvent, value);
        remove => Events.RemoveHandler(s_drawSubItemEvent, value);
    }

    private static readonly object s_drawColumnHeaderEvent = new();
    private static readonly object s_drawItemEvent = new();
    private static readonly object s_drawSubItemEvent = new();

    protected virtual void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e) =>
        ((DrawListViewColumnHeaderEventHandler?)Events[s_drawColumnHeaderEvent])?.Invoke(this, e);

    protected virtual void OnDrawItem(DrawListViewItemEventArgs e) =>
        ((DrawListViewItemEventHandler?)Events[s_drawItemEvent])?.Invoke(this, e);

    protected virtual void OnDrawSubItem(DrawListViewSubItemEventArgs e) =>
        ((DrawListViewSubItemEventHandler?)Events[s_drawSubItemEvent])?.Invoke(this, e);

    /// <summary>The state flags WinForms passes to the drawing events for one item: which of Selected/Focused/Checked apply.</summary>
    private ListViewItemStates ItemDrawState(ListViewItem item)
    {
        var state = ListViewItemStates.Default;
        if (_selected.Contains(item)) state |= ListViewItemStates.Selected;
        if (Focused && ReferenceEquals(_focusedItem, item)) state |= ListViewItemStates.Focused;
        if (item.Checked) state |= ListViewItemStates.Checked;

        return state;
    }

    /// <summary>
    /// Raises DrawItem when OwnerDraw is on and a handler is attached. Returns true when the
    /// handler drew the item itself (DrawDefault left false); false means the caller must fall
    /// back to normal drawing, either because OwnerDraw is off, no handler is attached, or the
    /// handler asked for the default look by setting DrawDefault.
    /// </summary>
    private bool TryOwnerDrawItem(Graphics graphics, ListViewItem item, Rectangle bounds, out DrawListViewItemEventArgs? args)
    {
        args = null;
        if (!_ownerDraw || Events[s_drawItemEvent] is null) return false;

        args = new DrawListViewItemEventArgs(graphics, item, bounds, IndexOfItem(item), ItemDrawState(item));
        OnDrawItem(args);

        return !args.DrawDefault;
    }

    /// <summary>Raises DrawSubItem for one Details column of a row; true means the handler drew it.</summary>
    private bool TryOwnerDrawSubItem(Graphics graphics, ListViewItem item, int columnIndex, Rectangle bounds)
    {
        if (!_ownerDraw || Events[s_drawSubItemEvent] is null) return false;

        var subItem = columnIndex < item.SubItems.Count ? item.SubItems[columnIndex] : null;
        var header = columnIndex < _columns.Count ? _columns[columnIndex] : null;
        var args = new DrawListViewSubItemEventArgs(graphics, bounds, item, subItem, IndexOfItem(item), columnIndex, header, ItemDrawState(item));
        OnDrawSubItem(args);

        return !args.DrawDefault;
    }

    /// <summary>Raises DrawColumnHeader for one column's header cell; true means the handler drew it.</summary>
    private bool TryOwnerDrawColumnHeader(Graphics graphics, ColumnHeader column, Rectangle bounds)
    {
        if (!_ownerDraw || Events[s_drawColumnHeaderEvent] is null) return false;

        var args = new DrawListViewColumnHeaderEventArgs(
            graphics, bounds, _columns.IndexOf(column), column, ListViewItemStates.Default, EffectiveForeColor, EffectiveBackColor, Font);
        OnDrawColumnHeader(args);

        return !args.DrawDefault;
    }
}
