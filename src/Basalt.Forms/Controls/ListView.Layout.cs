using System.Drawing;

namespace System.Windows.Forms;

public partial class ListView
{
    /// <summary>Column headers and item rows laid out for <see cref="View.Details"/>, with the group bands between them.</summary>
    private readonly record struct DetailsLayout(Rectangle[] ItemRects, (ListViewGroup Group, Rectangle Rect)[] GroupBands, Size ContentSize);

    private Size SmallIconSize => _smallImageList?.ImageSize ?? new Size(16, 16);

    private Size LargeIconSize => _largeImageList?.ImageSize ?? new Size(32, 32);

    /// <summary>The height of one Details/List row: tall enough for the small icon and the text, never under 16 pixels.</summary>
    private int ItemRowHeight => Math.Max(Math.Max(Math.Max(SmallIconSize.Height, _stateImageList?.ImageSize.Height ?? 0), FontHeight) + 4, 16);

    /// <summary>The height of the column-header strip; zero outside Details, or when HeaderStyle hides it.</summary>
    private int HeaderHeight => _view == View.Details && _headerStyle != ColumnHeaderStyle.None ? Math.Max(FontHeight + 8, 18) : 0;

    /// <summary>The area items are drawn in: the client area under the column headers.</summary>
    private Rectangle ContentRectangle
    {
        get
        {
            var client = ClientRectangle;
            return new Rectangle(0, HeaderHeight, Math.Max(client.Width, 0), Math.Max(client.Height - HeaderHeight, 0));
        }
    }

    private bool ScrollsVertically => _view is View.Details or View.SmallIcon or View.LargeIcon;

    private bool ScrollsHorizontally => _view is View.Details or View.List;

    private int TotalColumnsWidth()
    {
        var width = 0;
        foreach (var column in _columns) width += Math.Max(column.Width, 0);
        return width;
    }

    /// <summary>Lays out every item's row and every group's header band for Details, stacked top to bottom.</summary>
    private DetailsLayout ComputeDetailsLayout()
    {
        var rects = new Rectangle[_items.Count];
        var bands = new List<(ListViewGroup Group, Rectangle Rect)>();
        var width = Math.Max(TotalColumnsWidth(), ContentRectangle.Width);
        var y = 0;
        var rowHeight = ItemRowHeight;

        if (_showGroups && _groups.Count > 0)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].Group is null)
                {
                    rects[i] = new Rectangle(0, y, width, rowHeight);
                    y += rowHeight;
                }
            }

            foreach (var group in _groups)
            {
                bands.Add((group, new Rectangle(0, y, width, rowHeight)));
                y += rowHeight;

                if (group.CollapsedState == ListViewGroupCollapsedState.Collapsed) continue;

                for (var i = 0; i < _items.Count; i++)
                {
                    if (ReferenceEquals(_items[i].Group, group))
                    {
                        rects[i] = new Rectangle(0, y, width, rowHeight);
                        y += rowHeight;
                    }
                }
            }
        }
        else
        {
            for (var i = 0; i < _items.Count; i++)
            {
                rects[i] = new Rectangle(0, y, width, rowHeight);
                y += rowHeight;
            }
        }

        return new DetailsLayout(rects, bands.ToArray(), new Size(width, y));
    }

    /// <summary>Lays out items for List: top to bottom within a column, wrapping into the next column to the right.</summary>
    private (Rectangle[] Rects, Size ContentSize) ComputeListLayout()
    {
        var rects = new Rectangle[_items.Count];
        var rowHeight = ItemRowHeight;
        var rowsPerColumn = Math.Max(ContentRectangle.Height / Math.Max(rowHeight, 1), 1);
        var columnWidth = ListColumnWidth();

        for (var i = 0; i < _items.Count; i++)
        {
            var column = i / rowsPerColumn;
            var row = i % rowsPerColumn;
            rects[i] = new Rectangle(column * columnWidth, row * rowHeight, columnWidth, rowHeight);
        }

        var columns = _items.Count == 0 ? 0 : (_items.Count + rowsPerColumn - 1) / rowsPerColumn;
        return (rects, new Size(columns * columnWidth, rowsPerColumn * rowHeight));
    }

    /// <summary>The width List gives every column: the widest icon-plus-label among the items, with a sensible minimum.</summary>
    private int ListColumnWidth()
    {
        var widest = 0;
        foreach (var item in _items)
        {
            var textWidth = TextRenderer.MeasureText(item.Text, item.Font).Width;
            widest = Math.Max(widest, SmallIconSize.Width + 6 + textWidth);
        }

        return Math.Max(widest, 90);
    }

    /// <summary>Lays out items for SmallIcon/LargeIcon: left to right, wrapping into the next row.</summary>
    private (Rectangle[] Rects, Size ContentSize) ComputeIconLayout(bool large)
    {
        var rects = new Rectangle[_items.Count];
        var cellSize = IconCellSize(large);
        var columnsPerRow = Math.Max(ContentRectangle.Width / Math.Max(cellSize.Width, 1), 1);

        if (!_autoArrange)
        {
            var width = 0;
            var height = 0;
            for (var i = 0; i < _items.Count; i++)
            {
                rects[i] = new Rectangle(_items[i].Position, cellSize);
                width = Math.Max(width, rects[i].Right);
                height = Math.Max(height, rects[i].Bottom);
            }
            return (rects, new Size(width, height));
        }

        for (var i = 0; i < _items.Count; i++)
        {
            var rowsPerColumn = Math.Max(ContentRectangle.Height / Math.Max(cellSize.Height, 1), 1);
            var row = _alignment == ListViewAlignment.Left ? i % rowsPerColumn : i / columnsPerRow;
            var column = _alignment == ListViewAlignment.Left ? i / rowsPerColumn : i % columnsPerRow;
            rects[i] = new Rectangle(column * cellSize.Width, row * cellSize.Height, cellSize.Width, cellSize.Height);
        }

        return (rects, new Size(rects.Length == 0 ? 0 : rects.Max(rect => rect.Right),
            rects.Length == 0 ? 0 : rects.Max(rect => rect.Bottom)));
    }

    private Size IconCellSize(bool large)
    {
        var icon = large ? LargeIconSize : SmallIconSize;
        var widestLabel = 0;
        foreach (var item in _items) widestLabel = Math.Max(widestLabel, TextRenderer.MeasureText(item.Text, item.Font).Width);

        if (_view == View.Tile && _tileSize != Size.Empty) return _tileSize;
        return large
            ? new Size(Math.Clamp(Math.Max(icon.Width + 16, widestLabel + 8), 60, 160), icon.Height + FontHeight + 10)
            : new Size(Math.Clamp(icon.Width + 8 + widestLabel, 90, 260), Math.Max(icon.Height, FontHeight) + 6);
    }

    /// <summary>Every item's row or cell in content coordinates (unscrolled), whatever the current View is.</summary>
    private Rectangle[] ComputeItemLayout(out Size contentSize)
    {
        switch (_view)
        {
            case View.Details:
                var details = ComputeDetailsLayout();
                contentSize = details.ContentSize;
                return details.ItemRects;

            case View.List:
                var list = ComputeListLayout();
                contentSize = list.ContentSize;
                return list.Rects;

            case View.SmallIcon:
                var small = ComputeIconLayout(large: false);
                contentSize = small.ContentSize;
                return small.Rects;

            default:
                var large = ComputeIconLayout(large: true);
                contentSize = large.ContentSize;
                return large.Rects;
        }
    }

    /// <summary>Translates a content-space rectangle (unscrolled) to client coordinates, applying the current scroll offset.</summary>
    private Rectangle ToClient(Rectangle content)
    {
        var origin = ContentRectangle.Location;
        var dx = ScrollsHorizontally ? _scrollX : 0;
        var dy = ScrollsVertically ? _scrollY : 0;

        return new Rectangle(origin.X + content.X - dx, origin.Y + content.Y - dy, content.Width, content.Height);
    }

    /// <summary>The icon and label sub-rectangles within one item's content-space rectangle, for the current View.</summary>
    private (Rectangle Icon, Rectangle Label) SplitItemRect(Rectangle item)
    {
        if (_view == View.LargeIcon)
        {
            var icon = LargeIconSize;
            var iconRect = new Rectangle(item.X + (item.Width - icon.Width) / 2, item.Y + 2, icon.Width, icon.Height);
            var labelRect = new Rectangle(item.X, iconRect.Bottom + 2, item.Width, Math.Max(item.Bottom - iconRect.Bottom - 2, 0));
            return (iconRect, labelRect);
        }

        var small = SmallIconSize;
        var smallIconRect = new Rectangle(item.X + 2, item.Y + (item.Height - small.Height) / 2, small.Width, small.Height);
        var smallLabelRect = new Rectangle(smallIconRect.Right + 4, item.Y, Math.Max(item.Width - smallIconRect.Width - 6, 0), item.Height);
        return (smallIconRect, smallLabelRect);
    }

    /// <summary>The unscrolled content-space rectangle for one item's whole row or cell.</summary>
    private Rectangle ItemContentBounds(int index)
    {
        if (IsVirtualLayoutView) return index >= 0 && index < _virtualListSize ? VirtualItemContentBounds(index) : Rectangle.Empty;

        var rects = ComputeItemLayout(out _);
        return index >= 0 && index < rects.Length ? rects[index] : Rectangle.Empty;
    }

    /// <summary>The content size scroll bars and clamping measure against: VirtualListSize x row height in VirtualMode (Details/List), never one Rectangle per item.</summary>
    private Size CurrentContentSize()
    {
        if (IsVirtualLayoutView) return VirtualContentSize();

        ComputeItemLayout(out var contentSize);
        return contentSize;
    }

    private Rectangle ItemPortionBounds(int index, ItemBoundsPortion portion)
    {
        var content = ItemContentBounds(index);
        if (content.IsEmpty) return Rectangle.Empty;

        if (_view == View.Details)
        {
            var columnZeroWidth = _columns.Count > 0 ? _columns[0].Width : content.Width;
            var row = ToClient(content);

            return portion switch
            {
                ItemBoundsPortion.Icon => new Rectangle(row.X + 2, row.Y, SmallIconSize.Width, row.Height),
                ItemBoundsPortion.Label => new Rectangle(row.X, row.Y, columnZeroWidth, row.Height),
                ItemBoundsPortion.ItemOnly => new Rectangle(row.X, row.Y, columnZeroWidth, row.Height),
                _ => row,
            };
        }

        var (icon, label) = SplitItemRect(content);

        return portion switch
        {
            ItemBoundsPortion.Icon => ToClient(icon),
            ItemBoundsPortion.Label => ToClient(label),
            ItemBoundsPortion.ItemOnly => ToClient(Rectangle.Union(icon, label)),
            _ => ToClient(content),
        };
    }

    /// <summary>A sub-item's bounds in Details view: the column's slice of its item's row; empty in every other View.</summary>
    internal Rectangle GetSubItemBounds(ListViewItem item, int subItemIndex)
    {
        if (_view != View.Details) return Rectangle.Empty;

        var index = IndexOfItem(item);
        if (index < 0) return Rectangle.Empty;

        var row = ToClient(ItemContentBounds(index));
        var x = row.X;
        for (var c = 0; c < subItemIndex && c < _columns.Count; c++) x += _columns[c].Width;

        var width = subItemIndex < _columns.Count ? _columns[subItemIndex].Width : 0;
        return new Rectangle(x, row.Y, width, row.Height);
    }

    /// <summary>The rectangle, in client coordinates, of one column's header cell.</summary>
    private Rectangle ColumnHeaderRect(int columnIndex)
    {
        var x = -_scrollX;
        for (var i = 0; i < columnIndex; i++) x += _columns[i].Width;

        return new Rectangle(x, 0, _columns[columnIndex].Width, HeaderHeight);
    }

    private bool NeedsVerticalScrollBar => _scrollable && ScrollsVertically && CurrentContentSize().Height > ContentRectangle.Height;

    private bool NeedsHorizontalScrollBar => _scrollable && ScrollsHorizontally && CurrentContentSize().Width > ContentRectangle.Width;

    private int ClampScrollY(int value)
    {
        var max = Math.Max(CurrentContentSize().Height - ContentRectangle.Height, 0);
        return Math.Clamp(value, 0, max);
    }

    private int ClampScrollX(int value)
    {
        var max = Math.Max(CurrentContentSize().Width - ContentRectangle.Width, 0);
        return Math.Clamp(value, 0, max);
    }

    /// <summary>Scrolls the minimum amount so the content-space rectangle is inside the visible viewport.</summary>
    private void ScrollIntoView(Rectangle content)
    {
        if (!_scrollable || content.IsEmpty) return;
        var viewport = ContentRectangle;

        if (ScrollsVertically)
        {
            if (content.Top < _scrollY) _scrollY = content.Top;
            else if (content.Bottom > _scrollY + viewport.Height) _scrollY = content.Bottom - viewport.Height;
            _scrollY = ClampScrollY(_scrollY);
        }

        if (ScrollsHorizontally)
        {
            if (content.Left < _scrollX) _scrollX = content.Left;
            else if (content.Right > _scrollX + viewport.Width) _scrollX = content.Right - viewport.Width;
            _scrollX = ClampScrollX(_scrollX);
        }

        Invalidate();
    }

    /// <summary>The first item whose row or cell is at least partially inside the visible viewport.</summary>
    private ListViewItem? TopItemCore()
    {
        if (IsVirtualLayoutView)
        {
            var (first, last) = VisibleVirtualRange();
            return first <= last ? GetVirtualItem(first) : null;
        }

        var rects = ComputeItemLayout(out _);
        var viewport = ContentRectangle;

        for (var i = 0; i < rects.Length; i++)
        {
            var client = ToClient(rects[i]);
            if (client.Bottom > viewport.Top && client.Right > viewport.Left) return _items[i];
        }

        return null;
    }

    private ListViewHitTestInfo HitTestCore(int x, int y)
    {
        var point = new Point(x, y);
        var viewport = ContentRectangle;

        if (y < viewport.Top) return new ListViewHitTestInfo(null, null, ListViewHitTestLocations.AboveClientArea);
        if (y > viewport.Bottom) return new ListViewHitTestInfo(null, null, ListViewHitTestLocations.BelowClientArea);
        if (x < viewport.Left) return new ListViewHitTestInfo(null, null, ListViewHitTestLocations.LeftOfClientArea);
        if (x > viewport.Right) return new ListViewHitTestInfo(null, null, ListViewHitTestLocations.RightOfClientArea);

        if (IsVirtualLayoutView) return HitTestVirtualCore(x, y, point);

        var rects = ComputeItemLayout(out _);
        for (var i = 0; i < rects.Length; i++)
        {
            var client = ToClient(rects[i]);
            if (!client.Contains(point)) continue;

            var (icon, label) = SplitItemRect(rects[i]);
            var location = ToClient(icon).Contains(point) ? ListViewHitTestLocations.Image : ListViewHitTestLocations.Label;

            ListViewItem.ListViewSubItem? subItem = null;
            if (_view == View.Details)
            {
                var columnX = client.X;
                for (var c = 0; c < _items[i].SubItems.Count && c < _columns.Count; c++)
                {
                    var columnWidth = _columns[c].Width;
                    if (x >= columnX && x < columnX + columnWidth)
                    {
                        subItem = _items[i].SubItems[c];
                        break;
                    }

                    columnX += columnWidth;
                }
            }

            return new ListViewHitTestInfo(_items[i], subItem, location);
        }

        return new ListViewHitTestInfo(null, null, ListViewHitTestLocations.None);
    }

    /// <summary>HitTestCore's VirtualMode counterpart: the row is found by arithmetic (VirtualIndexAt), and RetrieveVirtualItem is asked for only that one index, never for every row.</summary>
    private ListViewHitTestInfo HitTestVirtualCore(int x, int y, Point point)
    {
        var index = VirtualIndexAt(y);
        if (index < 0) return new ListViewHitTestInfo(null, null, ListViewHitTestLocations.None);

        var content = VirtualItemContentBounds(index);
        var client = ToClient(content);
        if (!client.Contains(point)) return new ListViewHitTestInfo(null, null, ListViewHitTestLocations.None);

        var item = GetVirtualItem(index);

        if (_view == View.Details)
        {
            var location = x < client.X + SmallIconSize.Width + 2 ? ListViewHitTestLocations.Image : ListViewHitTestLocations.Label;
            return new ListViewHitTestInfo(item, null, location);
        }

        var (icon, _) = SplitItemRect(content);
        var iconLocation = ToClient(icon).Contains(point) ? ListViewHitTestLocations.Image : ListViewHitTestLocations.Label;
        return new ListViewHitTestInfo(item, null, iconLocation);
    }
}
