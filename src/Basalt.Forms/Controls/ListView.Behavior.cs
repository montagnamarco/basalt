using System.Drawing;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Skins;

namespace System.Windows.Forms;

/// <summary>The native parts of ListView: hit-testing, painting, the keyboard, the mouse, and the window's own scroll bars.</summary>
public partial class ListView
{
    private const int CheckBoxSize = 13;
    private const int DividerGrabWidth = 4;

    /// <summary>A press tracked on the window's frame: which bar, and where in the thumb the mouse grabbed it.</summary>
    private (bool Vertical, int GrabOffset)? _thumbDrag;

    /// <summary>The column whose right divider is being dragged, or -1 while none is.</summary>
    private int _resizingColumn = -1;
    private int _resizeStartX;
    private int _resizeStartWidth;

    /// <summary>The column a press landed on in the header, fired as ColumnClick only if released there without a resize or a reorder drag.</summary>
    private int _pendingColumnClick = -1;

    /// <summary>The column being dragged to a new position, once AllowColumnReorder and the press has moved far enough; -1 while none is.</summary>
    private int _reorderingColumn = -1;
    private int _columnPressX;

    private Color EffectiveBackColor => CollectionVisualAppearance.Field(this,
        ShouldSerializeBackColor() ? BackColor : SystemColors.Window);

    private Color EffectiveForeColor => CollectionVisualAppearance.Text(this, Enabled
        ? (ShouldSerializeForeColor() ? ForeColor : SystemColors.WindowText)
        : SystemColors.GrayText);

    // --- Painting -------------------------------------------------------------

    internal override void DefaultPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var client = ClientRectangle;

        using (var back = new SolidBrush(EffectiveBackColor)) graphics.FillRectangle(back, client);

        if (IsVirtualLayoutView)
        {
            PaintVirtualItems(graphics);
            if (_view == View.Details) PaintHeader(graphics);
            return;
        }

        var rects = ComputeItemLayout(out _);

        if (_view == View.Details)
        {
            PaintGroupBands(graphics, rects);
            PaintDetailsItems(graphics, rects);
            if (_gridLines) PaintGridLines(graphics, rects);
            PaintHeader(graphics);
        }
        else
        {
            PaintIconOrListItems(graphics, rects);
        }
    }

    private void PaintHeader(Graphics graphics)
    {
        if (HeaderHeight == 0) return;

        var x = -_scrollX;
        for (var i = 0; i < _columns.Count; i++)
        {
            var width = _columns[i].Width;
            var rect = new Rectangle(x, 0, width, HeaderHeight);

            if (rect.Right > 0 && rect.Left < ClientRectangle.Width) PaintHeaderCell(graphics, _columns[i], rect);
            x += width;
        }

        if (x < ClientRectangle.Width)
        {
            var filler = new Rectangle(x, 0, ClientRectangle.Width - x, HeaderHeight);
            if (PortableVisualStylesEnabled)
            {
                PaintPortableHeaderFace(graphics, filler);
            }
            else
            {
                ControlPaint.DrawButton(graphics, filler, ButtonState.Normal);
            }
        }
    }

    private void PaintHeaderCell(Graphics graphics, ColumnHeader column, Rectangle rect)
    {
        if (TryOwnerDrawColumnHeader(graphics, column, rect)) return;

        if (PortableVisualStylesEnabled)
        {
            PaintPortableHeaderFace(graphics, rect);
        }
        else
        {
            ControlPaint.DrawButton(graphics, rect, ButtonState.Normal);
        }

        var textX = rect.X + 3;
        var imageList = column.ImageList;
        if (imageList is not null)
        {
            var imageIndex = column.ImageKey.Length > 0
                ? imageList.Images.IndexOfKey(column.ImageKey)
                : column.ImageIndex;
            if (imageIndex >= 0 && imageIndex < imageList.Images.Count)
            {
                imageList.Draw(graphics, textX, rect.Y + Math.Max((rect.Height - imageList.ImageSize.Height) / 2, 0), imageIndex);
                textX += imageList.ImageSize.Width + 3;
            }
        }

        var textRect = new Rectangle(textX, rect.Y, Math.Max(rect.Right - textX - 3, 0), rect.Height);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | (column.TextAlign switch
            {
                HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
                HorizontalAlignment.Right => TextFormatFlags.Right,
                _ => TextFormatFlags.Left,
            });

        TextRenderer.DrawText(graphics, column.Text, Font, textRect, EffectiveForeColor, flags);
    }

    private void PaintGroupBands(Graphics graphics, Rectangle[] itemRects)
    {
        if (!_showGroups || _groups.Count == 0) return;

        var details = ComputeDetailsLayout();
        using var textBrush = new SolidBrush(EffectiveForeColor);

        foreach (var (group, bandRect) in details.GroupBands)
        {
            var client = ToClient(bandRect);
            if (client.Bottom <= ContentRectangle.Top || client.Top >= ContentRectangle.Bottom) continue;

            var titleX = client.X + (group.CollapsedState == ListViewGroupCollapsedState.Default ? 4 : 18);
            if (_groupImageList is { } images)
            {
                var index = group.TitleImageKey.Length > 0
                    ? images.Images.IndexOfKey(group.TitleImageKey) : group.TitleImageIndex;
                if (index >= 0 && index < images.Images.Count)
                {
                    images.Draw(graphics, titleX, client.Y + Math.Max((client.Height - images.ImageSize.Height) / 2, 0), index);
                    titleX += images.ImageSize.Width + 3;
                }
            }
            if (group.CollapsedState != ListViewGroupCollapsedState.Default)
            {
                var glyph = group.CollapsedState == ListViewGroupCollapsedState.Collapsed ? "+" : "−";
                TextRenderer.DrawText(graphics, glyph, Font,
                    new Rectangle(client.X + 3, client.Y, 13, client.Height), EffectiveForeColor,
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            }

            var textRect = new Rectangle(titleX, client.Y, Math.Max(client.Right - titleX - 4, 0), client.Height);
            TextRenderer.DrawText(graphics, group.Header, Font, textRect, EffectiveForeColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

            if (group.TaskLink.Length > 0)
            {
                var taskRect = TaskLinkBounds(group, client);
                TextRenderer.DrawText(graphics, group.TaskLink, Font, taskRect,
                    PortableVisualStylesEnabled ? CollectionVisualAppearance.Palette(this).Accent : SystemColors.HotTrack,
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            }

            using var line = new Pen(PortableVisualStylesEnabled
                ? CollectionVisualAppearance.Palette(this).Border : SystemColors.ControlDark);
            graphics.DrawLine(line, client.X, client.Bottom - 1, client.Right, client.Bottom - 1);
        }
    }

    private Rectangle TaskLinkBounds(ListViewGroup group, Rectangle band)
    {
        var width = TextRenderer.MeasureText(group.TaskLink, Font).Width + 8;
        return new Rectangle(Math.Max(band.Right - width - 4, band.X), band.Y, Math.Min(width, band.Width), band.Height);
    }

    private bool HandleGroupBandClick(Point point)
    {
        if (_view != View.Details || !_showGroups || _groups.Count == 0) return false;
        foreach (var (group, band) in ComputeDetailsLayout().GroupBands)
        {
            var client = ToClient(band);
            if (!client.Contains(point)) continue;
            if (group.TaskLink.Length > 0 && TaskLinkBounds(group, client).Contains(point))
                OnGroupTaskLinkClick(new ListViewGroupEventArgs(Groups.IndexOf(group)));
            else if (group.CollapsedState != ListViewGroupCollapsedState.Default)
                group.CollapsedState = group.CollapsedState == ListViewGroupCollapsedState.Collapsed
                    ? ListViewGroupCollapsedState.Expanded : ListViewGroupCollapsedState.Collapsed;
            return true;
        }
        return false;
    }

    private void PaintDetailsItems(Graphics graphics, Rectangle[] itemRects)
    {
        var viewport = ContentRectangle;

        for (var i = 0; i < _items.Count; i++)
        {
            var row = ToClient(itemRects[i]);
            if (row.Bottom <= viewport.Top || row.Top >= viewport.Bottom) continue;

            PaintDetailsRow(graphics, _items[i], row);
        }
    }

    private void PaintDetailsRow(Graphics graphics, ListViewItem item, Rectangle row)
    {
        if (TryOwnerDrawItem(graphics, item, row, out _))
        {
            PaintDetailsRowSubItemsOwnerDrawn(graphics, item, row);
            return;
        }

        var selected = _selected.Contains(item);
        var highlighted = selected && (!_hideSelection || Focused);
        var focused = Focused && ReferenceEquals(_focusedItem, item);

        var highlightWidth = _fullRowSelect ? row.Width : (_columns.Count > 0 ? _columns[0].Width : row.Width);
        var highlightRect = new Rectangle(row.X, row.Y, highlightWidth, row.Height);

        if (highlighted)
        {
            using var brush = new SolidBrush(SelectionBackColor);
            graphics.FillRectangle(brush, highlightRect);
        }

        var x = row.X;
        var checkboxWidth = StateGlyphWidth(item);
        if (checkboxWidth > 0)
        {
            DrawStateGlyph(graphics, item, x, row.Y, row.Height);
            x += checkboxWidth;
        }

        var columnCount = Math.Max(_columns.Count, 1);
        for (var c = 0; c < columnCount && c < item.SubItems.Count; c++)
        {
            var columnWidth = c < _columns.Count ? _columns[c].Width : row.Width;
            var cell = new Rectangle(x, row.Y, Math.Max(columnWidth - (c == 0 ? checkboxWidth : 0), 0), row.Height);
            var subItem = item.SubItems[c];

            var foreColor = highlighted ? SelectionForeColor : ItemTextColor(subItem);
            var textRect = cell;

            if (c == 0)
            {
                var iconSize = SmallIconSize;
                if (_smallImageList is not null && item.ImageIndex >= 0 && item.ImageIndex < _smallImageList.Images.Count)
                {
                    _smallImageList.Draw(graphics, cell.X + 2, row.Y + (row.Height - iconSize.Height) / 2, item.ImageIndex);
                }

                textRect = new Rectangle(cell.X + iconSize.Width + 4, cell.Y, Math.Max(cell.Width - iconSize.Width - 6, 0), cell.Height);
            }
            else
            {
                textRect = new Rectangle(cell.X + 2, cell.Y, Math.Max(cell.Width - 4, 0), cell.Height);
            }

            var textFont = subItem.Font ?? Font;
            using var hotFont = c == 0 ? HotFont(item, textFont) : null;
            TextRenderer.DrawText(graphics, subItem.Text, hotFont ?? textFont, textRect, foreColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            x += cell.Width;
        }

        if (focused) ControlPaint.DrawFocusRectangle(graphics, highlightRect, EffectiveForeColor, EffectiveBackColor);
    }

    /// <summary>Draws one row's columns through DrawSubItem, once the handler asked for a hand-drawn row via DrawItem.</summary>
    private void PaintDetailsRowSubItemsOwnerDrawn(Graphics graphics, ListViewItem item, Rectangle row)
    {
        var x = row.X;
        var columnCount = Math.Max(_columns.Count, 1);

        for (var c = 0; c < columnCount && c < item.SubItems.Count; c++)
        {
            var columnWidth = c < _columns.Count ? _columns[c].Width : row.Width;
            var cell = new Rectangle(x, row.Y, columnWidth, row.Height);

            if (!TryOwnerDrawSubItem(graphics, item, c, cell))
            {
                var subItem = item.SubItems[c];
                TextRenderer.DrawText(graphics, subItem.Text, subItem.Font ?? Font, cell, ItemTextColor(subItem),
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }

            x += cell.Width;
        }
    }

    /// <summary>
    /// Draws every separator as a filled 1-pixel-wide rectangle rather than a
    /// stroked line, so it lands exactly on the pixel row or column GetItemRect
    /// reports, with no antialiasing to blur which row owns the boundary.
    /// </summary>
    private void PaintGridLines(Graphics graphics, Rectangle[] itemRects)
    {
        using var brush = new SolidBrush(PortableVisualStylesEnabled
            ? CollectionVisualAppearance.Palette(this).Border : SystemColors.ControlLight);
        var viewport = ContentRectangle;

        var x = -_scrollX;
        for (var i = 0; i < _columns.Count; i++)
        {
            x += _columns[i].Width;
            if (x >= viewport.Left && x < viewport.Right) graphics.FillRectangle(brush, x, viewport.Top, 1, viewport.Height);
        }

        for (var i = 0; i < itemRects.Length; i++)
        {
            var bottom = ToClient(itemRects[i]).Bottom - 1;
            if (bottom >= viewport.Top && bottom < viewport.Bottom) graphics.FillRectangle(brush, viewport.Left, bottom, viewport.Width, 1);
        }
    }

    private void PaintIconOrListItems(Graphics graphics, Rectangle[] itemRects)
    {
        var viewport = ContentRectangle;

        for (var i = 0; i < _items.Count; i++)
        {
            var cell = ToClient(itemRects[i]);
            if (cell.Bottom <= viewport.Top || cell.Top >= viewport.Bottom) continue;
            if (cell.Right <= viewport.Left || cell.Left >= viewport.Right) continue;

            PaintIconOrListItem(graphics, _items[i], itemRects[i], cell);
        }
    }

    private void PaintIconOrListItem(Graphics graphics, ListViewItem item, Rectangle content, Rectangle cell)
    {
        if (TryOwnerDrawItem(graphics, item, cell, out _)) return;

        var selected = _selected.Contains(item);
        var highlighted = selected && (!_hideSelection || Focused);
        var focused = Focused && ReferenceEquals(_focusedItem, item);

        var (iconContent, labelContent) = SplitItemRect(content);
        var iconRect = ToClient(iconContent);
        var labelRect = ToClient(labelContent);

        var foreColor = highlighted ? SelectionForeColor : EffectiveForeColor;

        if (highlighted)
        {
            using var brush = new SolidBrush(SelectionBackColor);
            graphics.FillRectangle(brush, labelRect);
        }

        var large = _view == View.LargeIcon;
        var imageList = large ? _largeImageList : _smallImageList;
        var stateWidth = StateGlyphWidth(item);
        if (stateWidth > 0)
        {
            DrawStateGlyph(graphics, item, cell.X, cell.Y, cell.Height);
            if (!large)
            {
                iconRect.Offset(stateWidth, 0);
                labelRect.X += stateWidth;
                labelRect.Width = Math.Max(0, labelRect.Width - stateWidth);
            }
        }
        if (imageList is not null && item.ImageIndex >= 0 && item.ImageIndex < imageList.Images.Count)
        {
            var size = large ? LargeIconSize : SmallIconSize;
            imageList.Draw(graphics, iconRect.X + (iconRect.Width - size.Width) / 2, iconRect.Y + (iconRect.Height - size.Height) / 2, item.ImageIndex);
        }

        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter
            | (large ? TextFormatFlags.HorizontalCenter | (_labelWrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine)
                : TextFormatFlags.Left | TextFormatFlags.SingleLine);

        using var hotFont = HotFont(item, item.Font);
        TextRenderer.DrawText(graphics, item.Text, hotFont ?? item.Font, labelRect, foreColor, flags);

        if (focused) ControlPaint.DrawFocusRectangle(graphics, labelRect, EffectiveForeColor, EffectiveBackColor);
    }

    /// <summary>
    /// DefaultPaint's VirtualMode path: paints only the rows VisibleVirtualRange reports (never one
    /// Rectangle, let alone one RetrieveVirtualItem call, per row of a VirtualListSize of 100,000),
    /// and raises CacheVirtualItems once per distinct visible range rather than on every paint.
    /// </summary>
    private void PaintVirtualItems(Graphics graphics)
    {
        var (first, last) = VisibleVirtualRange();
        if (first > last) return;

        NotifyCacheVirtualItemsIfRangeChanged(first, last);

        for (var index = first; index <= last; index++)
        {
            var item = GetVirtualItem(index);
            var row = ToClient(VirtualItemContentBounds(index));

            if (_view == View.Details) PaintVirtualDetailsRow(graphics, item, index, row);
            else PaintVirtualListItem(graphics, item, index, row);
        }
    }

    /// <summary>PaintDetailsRow's VirtualMode counterpart: selection comes from IsVirtualIndexSelected(index), since a virtual item has no identity of its own to look up in _selected.</summary>
    private void PaintVirtualDetailsRow(Graphics graphics, ListViewItem item, int index, Rectangle row)
    {
        var highlighted = IsVirtualIndexSelected(index) && (!_hideSelection || Focused);
        var highlightWidth = _fullRowSelect ? row.Width : (_columns.Count > 0 ? _columns[0].Width : row.Width);
        var highlightRect = new Rectangle(row.X, row.Y, highlightWidth, row.Height);

        if (highlighted)
        {
            using var brush = new SolidBrush(SelectionBackColor);
            graphics.FillRectangle(brush, highlightRect);
        }

        var foreColor = highlighted ? SelectionForeColor : EffectiveForeColor;
        var x = row.X;
        var stateWidth = StateGlyphWidth(item);
        if (stateWidth > 0)
        {
            DrawStateGlyph(graphics, item, x, row.Y, row.Height);
            x += stateWidth;
        }
        var columnCount = Math.Max(_columns.Count, 1);

        for (var c = 0; c < columnCount && c < item.SubItems.Count; c++)
        {
            var columnWidth = c < _columns.Count ? _columns[c].Width : row.Width;
            var cell = new Rectangle(x, row.Y, Math.Max(columnWidth - (c == 0 ? stateWidth : 0), 0), row.Height);
            var subItem = item.SubItems[c];
            var textRect = cell;

            if (c == 0)
            {
                var iconSize = SmallIconSize;
                if (_smallImageList is not null && item.ImageIndex >= 0 && item.ImageIndex < _smallImageList.Images.Count)
                {
                    _smallImageList.Draw(graphics, cell.X + 2, row.Y + (row.Height - iconSize.Height) / 2, item.ImageIndex);
                }

                textRect = new Rectangle(cell.X + iconSize.Width + 4, cell.Y, Math.Max(cell.Width - iconSize.Width - 6, 0), cell.Height);
            }
            else
            {
                textRect = new Rectangle(cell.X + 2, cell.Y, Math.Max(cell.Width - 4, 0), cell.Height);
            }

            var textFont = subItem.Font ?? Font;
            using var hotFont = c == 0 ? HotFont(item, textFont, index) : null;
            TextRenderer.DrawText(graphics, subItem.Text, hotFont ?? textFont, textRect,
                highlighted ? foreColor : ItemTextColor(subItem),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            x += cell.Width;
        }
    }

    /// <summary>PaintIconOrListItem's VirtualMode counterpart, used for View.List (the one icon-ish view VirtualMode supports this round).</summary>
    private void PaintVirtualListItem(Graphics graphics, ListViewItem item, int index, Rectangle row)
    {
        var highlighted = IsVirtualIndexSelected(index) && (!_hideSelection || Focused);
        var foreColor = highlighted ? SelectionForeColor : EffectiveForeColor;

        if (highlighted)
        {
            using var brush = new SolidBrush(SelectionBackColor);
            graphics.FillRectangle(brush, row);
        }

        var iconSize = SmallIconSize;
        var stateWidth = StateGlyphWidth(item);
        if (stateWidth > 0) DrawStateGlyph(graphics, item, row.X, row.Y, row.Height);
        var iconRect = new Rectangle(row.X + 2 + stateWidth, row.Y + (row.Height - iconSize.Height) / 2, iconSize.Width, iconSize.Height);
        if (_smallImageList is not null && item.ImageIndex >= 0 && item.ImageIndex < _smallImageList.Images.Count)
        {
            _smallImageList.Draw(graphics, iconRect.X, iconRect.Y, item.ImageIndex);
        }

        var labelRect = new Rectangle(iconRect.Right + 4, row.Y, Math.Max(row.Width - iconRect.Width - 6, 0), row.Height);
        using var hotFont = HotFont(item, item.Font, index);
        TextRenderer.DrawText(graphics, item.Text, hotFont ?? item.Font, labelRect, foreColor,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    // --- Focus -----------------------------------------------------------------

    internal override void DefaultGotFocus() => Invalidate();

    internal override void DefaultLostFocus() => Invalidate();

    // --- Mouse -------------------------------------------------------------

    internal override void DefaultMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (CanSelect) Focus();

        if (_view == View.Details && e.Y < HeaderHeight)
        {
            HandleHeaderMouseDown(e.X);
            return;
        }

        if (HandleGroupBandClick(e.Location)) return;

        if (IsVirtualLayoutView)
        {
            var virtualIndex = VirtualIndexAt(e.Y);
            if (virtualIndex < 0)
            {
                if ((ModifierKeys & (Keys.Control | Keys.Shift)) == 0) ClearVirtualSelection();
                return;
            }

            SelectVirtualFromMouse(virtualIndex, e);
            return;
        }

        var hit = HitTestCore(e.X, e.Y);
        if (hit.Item is null)
        {
            _dragCandidate = null;
            if ((ModifierKeys & (Keys.Control | Keys.Shift)) == 0) ClearSelection();
            return;
        }

        _dragCandidate = hit.Item;
        _dragOrigin = e.Location;

        if (_checkBoxes && IsCheckBoxHit(hit.Item, e.Location))
        {
            hit.Item.Checked = !hit.Item.Checked;
            return;
        }

        // A click on the label of an item already selected re-opens it for editing, same as Explorer's list view.
        var wasSelected = hit.Item.Selected;
        SelectFromMouse(hit.Item, e);

        if (_labelEdit && wasSelected && hit.Location == ListViewHitTestLocations.Label && (ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
        {
            BeginEditCore(hit.Item);
        }
    }

    internal override void DefaultMouseMove(MouseEventArgs e)
    {
        if (_dragCandidate is { } item &&
            (Math.Abs(e.X - _dragOrigin.X) >= 4 || Math.Abs(e.Y - _dragOrigin.Y) >= 4))
        {
            _dragCandidate = null;
            OnItemDrag(new ItemDragEventArgs(MouseButtons.Left, item));
        }

        if (_resizingColumn >= 0)
        {
            var delta = e.X - _resizeStartX;
            var proposed = Math.Max(_resizeStartWidth + delta, 0);

            var args = new ColumnWidthChangingEventArgs(_resizingColumn, proposed);
            OnColumnWidthChanging(args);
            if (!args.Cancel) _columns[_resizingColumn].Width = args.NewWidth;

            return;
        }

        if (_allowColumnReorder && _pendingColumnClick >= 0 && _reorderingColumn < 0 && Math.Abs(e.X - _columnPressX) > DividerGrabWidth)
        {
            _reorderingColumn = _pendingColumnClick;
        }

        TrackItemPointer(e);
    }

    internal override void DefaultMouseUp(MouseEventArgs e)
    {
        _dragCandidate = null;
        if (_resizingColumn >= 0)
        {
            _resizingColumn = -1;
            _pendingColumnClick = -1;
            return;
        }

        if (_reorderingColumn >= 0)
        {
            var newIndex = HeaderColumnAt(e.X);
            if (newIndex >= 0 && newIndex != _reorderingColumn)
            {
                var header = _columns[_reorderingColumn];
                var args = new ColumnReorderedEventArgs(_reorderingColumn, newIndex, header);
                OnColumnReordered(args);
                if (!args.Cancel) MoveColumnDisplayIndex(header, newIndex);
            }

            _reorderingColumn = -1;
            _pendingColumnClick = -1;
            return;
        }

        if (_pendingColumnClick >= 0)
        {
            var column = HeaderColumnAt(e.X);
            if (column == _pendingColumnClick && _headerStyle == ColumnHeaderStyle.Clickable) OnColumnClick(new ColumnClickEventArgs(column));
            _pendingColumnClick = -1;
        }
    }

    internal override void DefaultCaptureLost()
    {
        _dragCandidate = null;
        _resizingColumn = -1;
        _pendingColumnClick = -1;
        _reorderingColumn = -1;
        _thumbDrag = null;
    }

    private void HandleHeaderMouseDown(int x)
    {
        var divider = HeaderDividerAt(x);
        if (divider >= 0)
        {
            _resizingColumn = divider;
            _resizeStartX = x;
            _resizeStartWidth = _columns[divider].Width;
            return;
        }

        _pendingColumnClick = HeaderColumnAt(x);
        _columnPressX = x;
    }

    /// <summary>The column whose right edge is within grabbing distance of x, in client coordinates; -1 if none.</summary>
    private int HeaderDividerAt(int x)
    {
        var edge = -_scrollX;
        for (var i = 0; i < _columns.Count; i++)
        {
            edge += _columns[i].Width;
            if (Math.Abs(x - edge) <= DividerGrabWidth) return i;
        }

        return -1;
    }

    private int HeaderColumnAt(int x)
    {
        var start = -_scrollX;
        for (var i = 0; i < _columns.Count; i++)
        {
            var end = start + _columns[i].Width;
            if (x >= start && x < end) return i;
            start = end;
        }

        return -1;
    }

    private bool IsCheckBoxHit(ListViewItem item, Point location)
    {
        var index = IndexOfItem(item);
        if (index < 0) return false;

        var row = ToClient(ItemContentBounds(index));
        if (_view == View.Tile) return false;

        var size = _stateImageList?.ImageSize ?? new Size(CheckBoxSize, CheckBoxSize);
        var box = new Rectangle(row.X + 2, row.Y + (row.Height - size.Height) / 2, size.Width, size.Height);
        return box.Contains(location);
    }

    private void SelectFromMouse(ListViewItem item, MouseEventArgs e)
    {
        var shift = (ModifierKeys & Keys.Shift) != 0;
        var control = (ModifierKeys & Keys.Control) != 0;

        if (_multiSelect && shift && RangeAnchor() is { } anchor)
        {
            SelectRange(anchor, item);
        }
        else if (_multiSelect && control)
        {
            SetItemSelected(item, !item.Selected);
            _selectionAnchor = item;
        }
        else
        {
            ClearSelection();
            SetItemSelected(item, true);
            _selectionAnchor = item;
        }

        FocusedItem = item;
        EnsureVisible(IndexOfItem(item));
    }

    /// <summary>The anchor if it is still in this list, else the focused item.</summary>
    private ListViewItem? RangeAnchor() =>
        _selectionAnchor is { } anchor && ReferenceEquals(anchor.ListView, this) ? anchor : _focusedItem;

    private void SelectRange(ListViewItem from, ListViewItem to)
    {
        var fromIndex = IndexOfItem(from);
        var toIndex = IndexOfItem(to);
        if (fromIndex < 0 || toIndex < 0) return;

        var low = Math.Min(fromIndex, toIndex);
        var high = Math.Max(fromIndex, toIndex);

        // Only items whose state really changes are touched, so each raises its events once.
        foreach (var item in _selected.ToArray())
        {
            var index = IndexOfItem(item);
            if (index < low || index > high) SetItemSelected(item, false);
        }

        for (var i = low; i <= high; i++) SetItemSelected(_items[i], true);
    }

    private void ClearSelection()
    {
        foreach (var item in _selected.ToArray()) SetItemSelected(item, false);
    }

    // --- Keyboard ------------------------------------------------------------

    internal override bool DialogCodeWantsChar(char charCode) => false;

    /// <summary>
    /// Claims the arrow keys and paging keys before ContainerControl can read them as dialog
    /// navigation, Shift/Control held or not: keyData carries them ORed in (Keys.Down | Keys.Shift
    /// for Shift+Down), so matching keyData itself only ever recognised the unmodified key and
    /// silently lost every Shift/Control-arrow combination - including the Shift-range and
    /// Control-move-without-selecting gestures DefaultKeyDown and DefaultKeyDownVirtual implement.
    /// </summary>
    internal override bool DialogCodeWantsKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown;

    internal override void DefaultKeyDown(KeyEventArgs e)
    {
        if (EffectiveItemCount == 0) return;

        if (IsVirtualLayoutView)
        {
            DefaultKeyDownVirtual(e);
            return;
        }

        var currentIndex = _focusedItem is null ? -1 : IndexOfItem(_focusedItem);

        if (e.KeyCode == Keys.Space)
        {
            if (_checkBoxes && _focusedItem is not null) _focusedItem.Checked = !_focusedItem.Checked;
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.F2 && _labelEdit && _focusedItem is not null)
        {
            BeginEditCore(_focusedItem);
            e.Handled = true;
            return;
        }

        if (e.KeyCode is Keys.Enter && _focusedItem is not null)
        {
            OnItemActivate(EventArgs.Empty);
            e.Handled = true;
            return;
        }

        var target = NextIndexForKey(e.KeyCode, currentIndex);
        if (target is null) return;

        MoveFocusTo(target.Value, e.Shift, e.Control);
        e.Handled = true;
    }

    /// <summary>The item index a navigation key moves to, laid out per the current View; null when the key is not one of them.</summary>
    private int? NextIndexForKey(Keys keyCode, int current)
    {
        var start = current >= 0 ? current : 0;

        return _view switch
        {
            View.Details => keyCode switch
            {
                Keys.Up => Math.Max(start - 1, 0),
                Keys.Down => Math.Min(start + 1, _items.Count - 1),
                Keys.Home => 0,
                Keys.End => _items.Count - 1,
                Keys.PageUp => Math.Max(start - VisibleRowCount(), 0),
                Keys.PageDown => Math.Min(start + VisibleRowCount(), _items.Count - 1),
                _ => null,
            },
            View.List => keyCode switch
            {
                Keys.Up => Math.Max(start - 1, 0),
                Keys.Down => Math.Min(start + 1, _items.Count - 1),
                Keys.Left => Math.Max(start - RowsPerListColumn(), 0),
                Keys.Right => Math.Min(start + RowsPerListColumn(), _items.Count - 1),
                Keys.Home => 0,
                Keys.End => _items.Count - 1,
                _ => null,
            },
            _ => keyCode switch
            {
                Keys.Left => Math.Max(start - 1, 0),
                Keys.Right => Math.Min(start + 1, _items.Count - 1),
                Keys.Up => Math.Max(start - ColumnsPerIconRow(), 0),
                Keys.Down => Math.Min(start + ColumnsPerIconRow(), _items.Count - 1),
                Keys.Home => 0,
                Keys.End => _items.Count - 1,
                _ => null,
            },
        };
    }

    private int VisibleRowCount() => Math.Max(ContentRectangle.Height / Math.Max(ItemRowHeight, 1), 1);

    private int RowsPerListColumn() => Math.Max(ContentRectangle.Height / Math.Max(ItemRowHeight, 1), 1);

    private int ColumnsPerIconRow() => Math.Max(ContentRectangle.Width / Math.Max(IconCellSize(_view == View.LargeIcon).Width, 1), 1);

    private void MoveFocusTo(int newIndex, bool shift, bool control)
    {
        var item = _items[newIndex];

        if (_multiSelect && shift && RangeAnchor() is { } anchor)
        {
            SelectRange(anchor, item);
        }
        else if (!(control && _multiSelect))
        {
            ClearSelection();
            SetItemSelected(item, true);
            _selectionAnchor = item;
        }

        FocusedItem = item;
        EnsureVisible(newIndex);
    }

    // --- The window's own scroll bars ----------------------------------------

    internal override WindowScrollInfo? GetWindowScroll(bool vertical)
    {
        var contentSize = CurrentContentSize();
        var viewport = ContentRectangle;

        if (vertical)
        {
            if (!NeedsVerticalScrollBar) return null;
            return new WindowScrollInfo(0, Math.Max(contentSize.Height - 1, 0), Math.Max(viewport.Height, 1), ClampScrollY(_scrollY), Enabled);
        }

        if (!NeedsHorizontalScrollBar) return null;
        return new WindowScrollInfo(0, Math.Max(contentSize.Width - 1, 0), Math.Max(viewport.Width, 1), ClampScrollX(_scrollX), Enabled);
    }

    internal override bool IsTrackingNonClient => _thumbDrag is not null;

    internal override bool NonClientMouseDown(MouseButtons button, Point outer)
    {
        if (button != MouseButtons.Left) return false;

        if (TryHandleBar(vertical: true, outer)) return _thumbDrag is { Vertical: true };
        if (TryHandleBar(vertical: false, outer)) return _thumbDrag is { Vertical: false };

        return false;
    }

    private bool TryHandleBar(bool vertical, Point outer)
    {
        if (GetWindowScroll(vertical) is not { } info) return false;

        var parameters = CreateParams;
        var bar = vertical
            ? WindowScrollBars.Vertical(Size, parameters.Style, parameters.ExStyle)
            : WindowScrollBars.Horizontal(Size, parameters.Style, parameters.ExStyle);

        if (bar.IsEmpty || !bar.Contains(outer)) return false;

        var orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        var track = ScrollBarPainter.TrackBounds(orientation, bar);
        var thumb = info.ThumbBounds(track, vertical);

        if (ScrollBarPainter.LineUpButtonBounds(orientation, bar).Contains(outer))
        {
            SetScroll(vertical, CurrentScroll(vertical) - 16);
        }
        else if (ScrollBarPainter.LineDownButtonBounds(orientation, bar).Contains(outer))
        {
            SetScroll(vertical, CurrentScroll(vertical) + 16);
        }
        else if (!thumb.IsEmpty && thumb.Contains(outer))
        {
            _thumbDrag = (vertical, (vertical ? outer.Y - thumb.Y : outer.X - thumb.X));
        }
        else if ((vertical ? outer.Y < thumb.Top : outer.X < thumb.Left))
        {
            SetScroll(vertical, CurrentScroll(vertical) - info.Page);
        }
        else
        {
            SetScroll(vertical, CurrentScroll(vertical) + info.Page);
        }

        return true;
    }

    internal override void NonClientMouseMove(Point outer)
    {
        if (_thumbDrag is not { } drag) return;
        if (GetWindowScroll(drag.Vertical) is not { } info) return;

        var parameters = CreateParams;
        var orientation = drag.Vertical ? Orientation.Vertical : Orientation.Horizontal;
        var bar = drag.Vertical
            ? WindowScrollBars.Vertical(Size, parameters.Style, parameters.ExStyle)
            : WindowScrollBars.Horizontal(Size, parameters.Style, parameters.ExStyle);

        var track = ScrollBarPainter.TrackBounds(orientation, bar);
        var thumb = info.ThumbBounds(track, drag.Vertical);
        var offset = (drag.Vertical ? outer.Y - track.Top : outer.X - track.Left) - drag.GrabOffset;
        var length = drag.Vertical ? thumb.Height : thumb.Width;

        SetScroll(drag.Vertical, info.PositionFromThumb(offset, track, length, drag.Vertical));
    }

    internal override void NonClientMouseUp(Point outer) => _thumbDrag = null;

    private int CurrentScroll(bool vertical) => vertical ? _scrollY : _scrollX;

    private void SetScroll(bool vertical, int value)
    {
        if (vertical) _scrollY = ClampScrollY(value);
        else _scrollX = ClampScrollX(value);

        Invalidate();
    }
}
