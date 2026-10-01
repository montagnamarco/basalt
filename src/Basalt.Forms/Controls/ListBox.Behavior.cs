using System.Drawing;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Skins;

namespace System.Windows.Forms;

/// <summary>
/// The native parts of ListBox: selection bookkeeping, item-structure
/// notifications, layout measurements, painting, the keyboard and the mouse,
/// and the window's own vertical scroll bar.
/// </summary>
public partial class ListBox
{
    // --- Data-binding guard -------------------------------------------------

    /// <summary>Items cannot be edited directly while a DataSource drives them, as in WinForms.</summary>
    private void EnsureNotDataBound()
    {
        if (DataSource is not null) throw new ArgumentException("The items collection cannot be modified when the DataSource property is set.");
    }

    // --- Item-structure notifications, called by ObjectCollection ----------

    // Per-item state kept by a subclass (CheckedListBox's check marks) follows
    // the items through these, before any selection event lets a handler look.
    internal virtual void ItemInsertedInternal(int index)
    {
    }

    internal virtual void ItemRemovedInternal(int index)
    {
    }

    internal virtual void ItemsClearedInternal()
    {
    }

    /// <summary>The whole list was replaced with this many new items.</summary>
    internal virtual void ItemsResetInternal(int count)
    {
    }

    /// <summary>The items were reordered: the item now at position i was at oldIndexAt[i].</summary>
    internal virtual void ItemsReorderedInternal(int[] oldIndexAt)
    {
    }

    private void OnItemsInserted(int index)
    {
        ItemInsertedInternal(index);

        for (var i = 0; i < _selectedIndices.Count; i++)
        {
            if (_selectedIndices[i] >= index) _selectedIndices[i]++;
        }

        if (_focusedIndex >= index) _focusedIndex++;
        if (_selectionAnchor >= index) _selectionAnchor++;
        if (_topIndex > index) _topIndex++;

        if (_drawMode == DrawMode.OwnerDrawVariable)
        {
            _variableHeights.Insert(index, _itemHeight);
            MeasureVariableItem(index);
        }

        AdjustHeight();
        UpdateStyles();
        Invalidate();
    }

    private void OnItemsRemoved(int index)
    {
        ItemRemovedInternal(index);

        var wasSelected = _selectedIndices.Remove(index);

        for (var i = 0; i < _selectedIndices.Count; i++)
        {
            if (_selectedIndices[i] > index) _selectedIndices[i]--;
        }

        if (_focusedIndex == index) _focusedIndex = Math.Min(index, _items.Count - 1);
        else if (_focusedIndex > index) _focusedIndex--;

        if (_selectionAnchor == index) _selectionAnchor = -1;
        else if (_selectionAnchor > index) _selectionAnchor--;

        if (_topIndex > index) _topIndex--;
        _topIndex = ClampTopIndex(_topIndex);

        if (_drawMode == DrawMode.OwnerDrawVariable && index < _variableHeights.Count) _variableHeights.RemoveAt(index);

        AdjustHeight();
        UpdateStyles();
        Invalidate();

        if (wasSelected) RaiseSelectionChangedIfNeeded(true);
    }

    private void OnItemsCleared()
    {
        ItemsClearedInternal();

        var hadSelection = _selectedIndices.Count > 0;

        _selectedIndices.Clear();
        _focusedIndex = -1;
        _selectionAnchor = -1;
        _topIndex = 0;
        _variableHeights.Clear();

        AdjustHeight();
        UpdateStyles();
        Invalidate();

        if (hadSelection) RaiseSelectionChangedIfNeeded(true);
    }

    /// <summary>The whole list was replaced from a DataSource: selection and scroll reset, nothing to compare against.</summary>
    private void OnItemsReset()
    {
        ItemsResetInternal(_items.Count);

        _selectedIndices.Clear();
        _focusedIndex = -1;
        _selectionAnchor = -1;
        _topIndex = 0;
        _variableHeights.Clear();

        if (_drawMode == DrawMode.OwnerDrawVariable) RemeasureAllItems();

        AdjustHeight();
        UpdateStyles();
        Invalidate();
    }

    private void OnItemReplaced(int index)
    {
        if (_drawMode == DrawMode.OwnerDrawVariable) MeasureVariableItem(index);
        InvalidateItem(index);
    }

    // --- Selection primitives ------------------------------------------------

    private bool SelectIndex(int index, bool value)
    {
        if (index < 0 || index >= _items.Count) return false;

        var isSelected = _selectedIndices.Contains(index);
        if (isSelected == value) return false;

        if (value)
        {
            var position = _selectedIndices.BinarySearch(index);
            _selectedIndices.Insert(position < 0 ? ~position : position, index);
        }
        else
        {
            _selectedIndices.Remove(index);
        }

        InvalidateItem(index);
        return true;
    }

    /// <summary>Clears every other selection and selects only this one, as SelectedIndex and single clicks do.</summary>
    private void SelectOnly(int index)
    {
        var changed = false;

        foreach (var existing in _selectedIndices.ToArray())
        {
            if (existing != index) changed |= SelectIndex(existing, false);
        }

        changed |= SelectIndex(index, true);

        _focusedIndex = index;
        _selectionAnchor = index;
        EnsureVisible(index);

        RaiseSelectionChangedIfNeeded(changed);
    }

    /// <summary>Selects exactly the contiguous range between the two indices, as Shift does in MultiExtended.</summary>
    private void SelectRange(int from, int to)
    {
        var low = Math.Min(from, to);
        var high = Math.Max(from, to);
        var changed = false;

        foreach (var existing in _selectedIndices.ToArray())
        {
            if (existing < low || existing > high) changed |= SelectIndex(existing, false);
        }

        for (var i = low; i <= high; i++) changed |= SelectIndex(i, true);

        RaiseSelectionChangedIfNeeded(changed);
    }

    private void RaiseSelectionChangedIfNeeded(bool changed)
    {
        if (!changed) return;

        Invalidate();
        OnSelectedIndexChanged(EventArgs.Empty);
    }

    private void InvalidateItem(int index)
    {
        if (!IsHandleCreated) return;
        if (index < _topIndex) return;

        Invalidate(GetItemRectangle(index));
    }

    // --- Measurement and layout -----------------------------------------------

    private bool NeedsVerticalScrollBar => !_multiColumn && (_scrollAlwaysVisible || TotalItemsHeight() > Math.Max(ClientRectangle.Height, 0));

    private int TotalItemsHeight()
    {
        if (_drawMode == DrawMode.OwnerDrawVariable && _variableHeights.Count == _items.Count)
        {
            var sum = 0;
            foreach (var height in _variableHeights) sum += height;
            return sum;
        }

        return _items.Count * Math.Max(_itemHeight, 1);
    }

    private int VisibleItemCount() => Math.Max(ClientRectangle.Height / Math.Max(_itemHeight, 1), 1);

    private int RowsPerColumn() => Math.Max(ClientRectangle.Height / Math.Max(_itemHeight, 1), 1);

    private int VisibleColumnCount() => Math.Max(ClientRectangle.Width / Math.Max(ColumnWidthEffective(), 1), 1);

    private int TotalColumnCount()
    {
        if (_items.Count == 0) return 0;

        var rows = RowsPerColumn();
        return (_items.Count + rows - 1) / rows;
    }

    /// <summary>The column width in effect: the explicit ColumnWidth, or the widest item's text plus a margin.</summary>
    private int ColumnWidthEffective()
    {
        if (_columnWidth > 0) return _columnWidth;
        if (_items.Count == 0) return Math.Max(ClientRectangle.Width, 1);

        var widest = 0;
        foreach (var item in _items) widest = Math.Max(widest, TextRenderer.MeasureText(GetItemText(item), Font).Width);

        return widest + 8;
    }

    private int ClampTopIndex(int value)
    {
        if (_items.Count == 0) return 0;

        if (_multiColumn)
        {
            var rows = RowsPerColumn();
            var maxTop = Math.Max((TotalColumnCount() - VisibleColumnCount()) * rows, 0);
            var aligned = value - ((value % rows) + rows) % rows;

            return Math.Clamp(aligned, 0, maxTop);
        }

        var maxTopSingle = Math.Max(_items.Count - VisibleItemCount(), 0);
        return Math.Clamp(value, 0, maxTopSingle);
    }

    /// <summary>Scrolls the minimum amount so the item is inside the visible window.</summary>
    private void EnsureVisible(int index)
    {
        if (index < 0 || index >= _items.Count) return;

        if (_multiColumn)
        {
            var rows = RowsPerColumn();
            var column = index / rows;
            var topColumn = _topIndex / rows;
            var visibleColumns = VisibleColumnCount();

            if (column < topColumn) TopIndex = column * rows;
            else if (column >= topColumn + visibleColumns) TopIndex = (column - visibleColumns + 1) * rows;

            return;
        }

        if (index < _topIndex) TopIndex = index;
        else if (index >= _topIndex + VisibleItemCount()) TopIndex = index - VisibleItemCount() + 1;
    }

    /// <summary>Rounds a proposed height to a whole number of items, when IntegralHeight asks for it.</summary>
    private int RoundedHeight(int proposedHeight)
    {
        if (!_integralHeight || _multiColumn) return proposedHeight;

        var itemHeight = Math.Max(_itemHeight, 1);
        var frame = NonClient.Vertical;
        var clientHeight = Math.Max(proposedHeight - frame, 0);
        var rows = Math.Max(clientHeight / itemHeight, 1);

        return rows * itemHeight + frame;
    }

    private void AdjustHeight()
    {
        if (!_integralHeight || _multiColumn) return;

        var rounded = RoundedHeight(Height);
        if (rounded != Height) Height = rounded;
    }

    /// <summary>Recomputes every item's owner-drawn height, as switching DrawMode or rebinding does.</summary>
    private void RemeasureAllItems()
    {
        _variableHeights.Clear();
        for (var i = 0; i < _items.Count; i++) _variableHeights.Add(_itemHeight);
        for (var i = 0; i < _items.Count; i++) MeasureVariableItem(i);
    }

    private void MeasureVariableItem(int index)
    {
        if (_drawMode != DrawMode.OwnerDrawVariable || index < 0 || index >= _variableHeights.Count) return;

        var args = new MeasureItemEventArgs(MeasurementGraphics, index, _itemHeight);
        OnMeasureItem(args);
        _variableHeights[index] = Math.Max(args.ItemHeight, 1);
    }

    // --- Colours -------------------------------------------------------------

    private Color EffectiveBackColor => ShouldSerializeBackColor() ? BackColor : SystemColors.Window;

    private Color EffectiveForeColor => Enabled
        ? (ShouldSerializeForeColor() ? ForeColor : SystemColors.WindowText)
        : SystemColors.GrayText;

    // --- Search ---------------------------------------------------------------

    private int FindStringCore(string? text, int startIndex, bool exact)
    {
        if (startIndex < -1 || startIndex >= _items.Count) throw new ArgumentOutOfRangeException(nameof(startIndex));
        if (_items.Count == 0) return NoMatches;

        text ??= string.Empty;
        var count = _items.Count;

        for (var offset = 1; offset <= count; offset++)
        {
            var index = (startIndex + offset) % count;
            if (index < 0) index += count;

            var itemText = GetItemText(_items[index]);
            var matches = exact
                ? string.Equals(itemText, text, StringComparison.CurrentCultureIgnoreCase)
                : itemText.StartsWith(text, StringComparison.CurrentCultureIgnoreCase);

            if (matches) return index;
        }

        return NoMatches;
    }

    // --- Focus and font --------------------------------------------------------

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        // Normal mode always tracks the font, as the native list box does; owner-draw modes keep an explicit height.
        if (_drawMode == DrawMode.Normal) _itemHeight = Math.Max(FontHeight, 1);

        AdjustHeight();
        Invalidate();
    }

    internal override void DefaultGotFocus()
    {
        if (_focusedIndex < 0 && _items.Count > 0) _focusedIndex = SelectedIndex >= 0 ? SelectedIndex : 0;
        Invalidate();
    }

    internal override void DefaultLostFocus() => Invalidate();

    // --- Painting ----------------------------------------------------------

    internal override void DefaultPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var client = ClientRectangle;

        using (var back = new SolidBrush(EffectiveBackColor))
        {
            graphics.FillRectangle(back, client);
        }

        if (_items.Count == 0) return;

        if (_multiColumn)
        {
            PaintMultiColumn(graphics, client);
            return;
        }

        for (var index = _topIndex; index < _items.Count; index++)
        {
            var bounds = GetItemRectangle(index);
            if (bounds.Top >= client.Height) break;

            PaintItem(graphics, index, bounds);
        }
    }

    private void PaintMultiColumn(Graphics graphics, Rectangle client)
    {
        var rows = RowsPerColumn();
        var width = ColumnWidthEffective();
        var visibleColumns = VisibleColumnCount() + 1;

        for (var column = 0; column < visibleColumns; column++)
        {
            if (column * width >= client.Width) break;

            for (var row = 0; row < rows; row++)
            {
                var index = _topIndex + column * rows + row;
                if (index >= _items.Count) break;

                PaintItem(graphics, index, GetItemRectangle(index));
            }
        }
    }

    private void PaintItem(Graphics graphics, int index, Rectangle bounds)
    {
        var selected = _selectedIndices.Contains(index);
        var focused = Focused && index == _focusedIndex;

        var foreColor = selected ? SystemColors.HighlightText : EffectiveForeColor;
        var backColor = selected ? SystemColors.Highlight : EffectiveBackColor;

        if (_drawMode != DrawMode.Normal)
        {
            var state = DrawItemState.None;
            if (selected) state |= DrawItemState.Selected;
            if (focused) state |= DrawItemState.Focus;
            if (!Enabled) state |= DrawItemState.Disabled;

            using var args = new DrawItemEventArgs(graphics, Font, bounds, index, state, foreColor, backColor);
            OnDrawItem(args);
            return;
        }

        using (var brush = new SolidBrush(backColor)) graphics.FillRectangle(brush, bounds);

        var text = GetItemText(_items[index]);
        var textBounds = new Rectangle(bounds.X + 2, bounds.Y, Math.Max(bounds.Width - 4, 0), bounds.Height);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(graphics, text, Font, textBounds, foreColor, flags);

        if (focused) ControlPaint.DrawFocusRectangle(graphics, bounds, foreColor, backColor);
    }

    // --- Keyboard ------------------------------------------------------------

    internal override bool DialogCodeWantsChar(char charCode) => true;

    internal override void DefaultKeyDown(KeyEventArgs e)
    {
        if (_items.Count == 0) return;

        var current = _focusedIndex >= 0 ? _focusedIndex : Math.Max(SelectedIndex, 0);

        int? target = e.KeyCode switch
        {
            Keys.Up => Math.Max(current - 1, 0),
            Keys.Down => Math.Min(current + 1, _items.Count - 1),
            Keys.Home => 0,
            Keys.End => _items.Count - 1,
            Keys.PageUp => Math.Max(current - Math.Max(VisibleItemCount() - 1, 1), 0),
            Keys.PageDown => Math.Min(current + Math.Max(VisibleItemCount() - 1, 1), _items.Count - 1),
            _ => null,
        };

        if (target is { } newIndex)
        {
            MoveFocus(newIndex, e.Shift, e.Control);
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Space && _selectionMode == SelectionMode.MultiSimple && _focusedIndex >= 0)
        {
            SetSelected(_focusedIndex, !GetSelected(_focusedIndex));
            e.Handled = true;
        }
    }

    internal override void DefaultKeyPress(KeyPressEventArgs e)
    {
        if (_items.Count == 0 || char.IsControl(e.KeyChar)) return;

        var start = (_focusedIndex >= 0 ? _focusedIndex : Math.Max(SelectedIndex, -1));
        var index = FindStringCore(e.KeyChar.ToString(), start, exact: false);

        if (index >= 0) MoveFocus(index, shift: false, control: false);
        e.Handled = true;
    }

    /// <summary>Where the keyboard cursor goes, and what that does to the selection under each SelectionMode.</summary>
    private void MoveFocus(int newIndex, bool shift, bool control)
    {
        switch (_selectionMode)
        {
            case SelectionMode.None:
                _focusedIndex = newIndex;
                EnsureVisible(newIndex);
                Invalidate();
                break;

            case SelectionMode.One:
                SelectOnly(newIndex);
                break;

            case SelectionMode.MultiSimple:
                _focusedIndex = newIndex;
                EnsureVisible(newIndex);
                Invalidate();
                break;

            case SelectionMode.MultiExtended:
                if (shift)
                {
                    if (_selectionAnchor < 0) _selectionAnchor = RangeStartWithoutAnchor();
                    _focusedIndex = newIndex;
                    EnsureVisible(newIndex);
                    SelectRange(_selectionAnchor, newIndex);
                }
                else if (control)
                {
                    _focusedIndex = newIndex;
                    EnsureVisible(newIndex);
                    Invalidate();
                }
                else
                {
                    SelectOnly(newIndex);
                }

                break;
        }
    }

    /// <summary>
    /// Where a Shift range starts when nothing set an anchor yet: the focused
    /// item, or the first one, as the native list box's anchor starts at the caret.
    /// </summary>
    private int RangeStartWithoutAnchor() => _focusedIndex >= 0 ? _focusedIndex : 0;

    // --- Mouse ---------------------------------------------------------------

    internal override void DefaultMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (CanSelect) Focus();

        var index = IndexFromPoint(e.Location);
        if (index < 0) return;

        switch (_selectionMode)
        {
            case SelectionMode.None:
                _focusedIndex = index;
                Invalidate();
                break;

            case SelectionMode.One:
                SelectOnly(index);
                _selectingWithMouse = true;
                break;

            case SelectionMode.MultiSimple:
                _focusedIndex = index;
                _selectionAnchor = index;
                SetSelected(index, !GetSelected(index));
                break;

            case SelectionMode.MultiExtended:
                if ((ModifierKeys & Keys.Shift) != 0)
                {
                    if (_selectionAnchor < 0) _selectionAnchor = RangeStartWithoutAnchor();
                    _focusedIndex = index;
                    SelectRange(_selectionAnchor, index);
                }
                else if ((ModifierKeys & Keys.Control) != 0)
                {
                    _focusedIndex = index;
                    _selectionAnchor = index;
                    SetSelected(index, !GetSelected(index));
                }
                else
                {
                    SelectOnly(index);
                    _selectingWithMouse = true;
                }

                break;
        }
    }

    internal override void DefaultMouseMove(MouseEventArgs e)
    {
        if (!_selectingWithMouse || (e.Button & MouseButtons.Left) == 0) return;
        if (_selectionMode is not (SelectionMode.One or SelectionMode.MultiExtended)) return;

        var index = IndexFromPoint(e.Location);
        if (index < 0) return;

        if (_selectionMode == SelectionMode.One)
        {
            SelectOnly(index);
            return;
        }

        _focusedIndex = index;
        EnsureVisible(index);
        SelectRange(_selectionAnchor, index);
    }

    internal override void DefaultMouseUp(MouseEventArgs e) => _selectingWithMouse = false;

    internal override void DefaultCaptureLost() => _selectingWithMouse = false;

    // --- The window's own vertical scroll bar -------------------------------

    internal override WindowScrollInfo? GetWindowScroll(bool vertical)
    {
        if (!vertical || !NeedsVerticalScrollBar) return null;

        var maximum = Math.Max(_items.Count - 1, 0);
        var page = Math.Max(VisibleItemCount(), 1);

        return new WindowScrollInfo(0, maximum, page, Math.Clamp(_topIndex, 0, maximum), Enabled);
    }

    internal override bool IsTrackingNonClient => _thumbDrag is not null;

    internal override bool NonClientMouseDown(MouseButtons button, Point outer)
    {
        if (button != MouseButtons.Left) return false;
        if (GetWindowScroll(vertical: true) is not { } info) return false;

        var parameters = CreateParams;
        var bar = WindowScrollBars.Vertical(Size, parameters.Style, parameters.ExStyle);
        if (bar.IsEmpty || !bar.Contains(outer)) return false;

        var track = ScrollBarPainter.TrackBounds(Orientation.Vertical, bar);
        var thumb = info.ThumbBounds(track, vertical: true);

        if (ScrollBarPainter.LineUpButtonBounds(Orientation.Vertical, bar).Contains(outer))
        {
            TopIndex--;
        }
        else if (ScrollBarPainter.LineDownButtonBounds(Orientation.Vertical, bar).Contains(outer))
        {
            TopIndex++;
        }
        else if (!thumb.IsEmpty && thumb.Contains(outer))
        {
            _thumbDrag = (true, outer.Y - thumb.Y);
            return true;
        }
        else if (outer.Y < thumb.Top)
        {
            TopIndex -= info.Page;
        }
        else
        {
            TopIndex += info.Page;
        }

        return false;
    }

    internal override void NonClientMouseMove(Point outer)
    {
        if (_thumbDrag is not { } drag || GetWindowScroll(vertical: true) is not { } info) return;

        var parameters = CreateParams;
        var bar = WindowScrollBars.Vertical(Size, parameters.Style, parameters.ExStyle);
        var track = ScrollBarPainter.TrackBounds(Orientation.Vertical, bar);
        var thumb = info.ThumbBounds(track, vertical: true);
        var offset = outer.Y - track.Top - drag.GrabOffset;

        TopIndex = info.PositionFromThumb(offset, track, thumb.Height, vertical: true);
    }

    internal override void NonClientMouseUp(Point outer) => _thumbDrag = null;
}
