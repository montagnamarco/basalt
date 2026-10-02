using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The thin line a ListView shows between two items while the user drags a reorder into place
/// (Explorer's own list views show the same line): which item it sits next to, on which side, and
/// in what colour. Owned by one ListView, created lazily the first time its InsertionMark is read.
/// </summary>
public sealed class ListViewInsertionMark
{
    private readonly ListView _listView;
    private bool _appearsAfterItem;
    private Color _color = SystemColors.HotTrack;
    private int _index = -1;

    internal ListViewInsertionMark(ListView listView)
    {
        _listView = listView;
    }

    /// <summary>Whether the mark sits after (to the right, or below in Details/LargeIcon view) Index's item, rather than before it.</summary>
    public bool AppearsAfterItem
    {
        get => _appearsAfterItem;
        set
        {
            if (value == _appearsAfterItem) return;

            _appearsAfterItem = value;
            _listView.InvalidateInsertionMarkCore();
        }
    }

    /// <summary>A thin strip along the edge of Index's item the mark sits next to; empty while Index is out of range.</summary>
    public Rectangle Bounds
    {
        get
        {
            if (_index < 0 || _index >= _listView.Items.Count) return Rectangle.Empty;

            var itemBounds = _listView.GetItemRect(_index);
            const int markThickness = 2;

            // List/SmallIcon/LargeIcon/Tile lay items out left to right: the mark is a vertical
            // line at the item's leading or trailing edge. Details is the one view that instead
            // stacks items top to bottom, so there the mark runs horizontally along the row's edge.
            if (_listView.View == View.Details)
            {
                var y = _appearsAfterItem ? itemBounds.Bottom : itemBounds.Top - markThickness;
                return new Rectangle(itemBounds.Left, y, itemBounds.Width, markThickness);
            }

            var x = _appearsAfterItem ? itemBounds.Right : itemBounds.Left - markThickness;
            return new Rectangle(x, itemBounds.Top, markThickness, itemBounds.Height);
        }
    }

    public Color Color
    {
        get => _color;
        set
        {
            if (value == _color) return;

            _color = value;
            _listView.InvalidateInsertionMarkCore();
        }
    }

    /// <summary>The item the mark sits next to; -1 (the default) shows no mark at all.</summary>
    public int Index
    {
        get => _index;
        set
        {
            if (value == _index) return;

            _index = value;
            _listView.InvalidateInsertionMarkCore();
        }
    }

    /// <summary>
    /// The item whose centre is closest to pt, by straight-line distance - what a drag-and-drop
    /// handler calls on every DragOver to decide which item Index should follow next, before it
    /// separately decides AppearsAfterItem from which half of that item's bounds pt fell in.
    /// </summary>
    public int NearestIndex(Point pt)
    {
        var items = _listView.Items;
        if (items.Count == 0) return -1;

        var nearest = 0;
        var nearestDistanceSquared = long.MaxValue;

        for (var index = 0; index < items.Count; index++)
        {
            var bounds = _listView.GetItemRect(index);
            var centerX = bounds.Left + bounds.Width / 2L;
            var centerY = bounds.Top + bounds.Height / 2L;
            var dx = centerX - pt.X;
            var dy = centerY - pt.Y;
            var distanceSquared = dx * dx + dy * dy;

            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearest = index;
            }
        }

        return nearest;
    }
}
