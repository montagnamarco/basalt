using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// VirtualMode: the owner supplies items on demand through RetrieveVirtualItem
/// instead of ListView keeping real ListViewItem instances, so lists with
/// millions of rows do not need millions of live objects. Selection is tracked
/// by index (<see cref="_virtualSelectedIndices"/>) since items are not kept.
/// </summary>
public partial class ListView
{
    private bool _virtualMode;
    private int _virtualListSize;

    /// <summary>Which indices are selected while VirtualMode is on; meaningless, and left empty, otherwise.</summary>
    private readonly HashSet<int> _virtualSelectedIndices = [];

    /// <summary>The anchor index Shift-click/Shift-navigate range-selects from; fixed while a Shift gesture extends a range, meaningless once VirtualMode is off.</summary>
    private int? _virtualSelectionAnchor;

    /// <summary>The index keyboard navigation moves from next, and EnsureVisible follows; unlike the anchor, this moves on every arrow key even while Shift extends a range.</summary>
    private int? _virtualFocusedIndex;

    /// <summary>The range last reported through CacheVirtualItems, so scrolling one pixel without crossing a row boundary does not re-raise it on every paint.</summary>
    private (int First, int Last)? _lastNotifiedVirtualRange;

    [DefaultValue(false)]
    [RefreshProperties(RefreshProperties.Repaint)]
    public bool VirtualMode
    {
        get => _virtualMode;
        set
        {
            if (value == _virtualMode) return;

            _virtualMode = value;
            _virtualSelectedIndices.Clear();
            _virtualSelectionAnchor = null;
            _virtualFocusedIndex = null;
            _lastNotifiedVirtualRange = null;
            Invalidate();
        }
    }

    [DefaultValue(0)]
    [RefreshProperties(RefreshProperties.Repaint)]
    public int VirtualListSize
    {
        get => _virtualListSize;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _virtualListSize) return;

            _virtualListSize = value;
            _virtualSelectedIndices.RemoveWhere(i => i >= value);
            _virtualSelectionAnchor = ClampVirtualIndex(_virtualSelectionAnchor, value);
            _virtualFocusedIndex = ClampVirtualIndex(_virtualFocusedIndex, value);

            // A shrink can leave _scrollY scrolled past the new (shorter) content entirely - with
            // nothing to clamp it, VisibleVirtualRange's first > last and PaintVirtualItems draws
            // nothing at all, leaving the viewport blank until something else scrolls it back.
            _scrollY = ClampScrollY(_scrollY);

            _lastNotifiedVirtualRange = null;
            Invalidate();
        }
    }

    /// <summary>index clamped to the last valid position for a list of size count, or null when count is 0 - so a Shift-click after VirtualListSize shrinks range-selects from a position that still exists.</summary>
    private static int? ClampVirtualIndex(int? index, int count)
    {
        if (index is not { } value || value < count) return index;

        return count > 0 ? count - 1 : null;
    }

    public event CacheVirtualItemsEventHandler? CacheVirtualItems
    {
        add => Events.AddHandler(s_cacheVirtualItemsEvent, value);
        remove => Events.RemoveHandler(s_cacheVirtualItemsEvent, value);
    }

    public event RetrieveVirtualItemEventHandler? RetrieveVirtualItem
    {
        add => Events.AddHandler(s_retrieveVirtualItemEvent, value);
        remove => Events.RemoveHandler(s_retrieveVirtualItemEvent, value);
    }

    public event SearchForVirtualItemEventHandler? SearchForVirtualItem
    {
        add => Events.AddHandler(s_searchForVirtualItemEvent, value);
        remove => Events.RemoveHandler(s_searchForVirtualItemEvent, value);
    }

    public event ListViewVirtualItemsSelectionRangeChangedEventHandler? VirtualItemsSelectionRangeChanged
    {
        add => Events.AddHandler(s_virtualItemsSelectionRangeChangedEvent, value);
        remove => Events.RemoveHandler(s_virtualItemsSelectionRangeChangedEvent, value);
    }

    private static readonly object s_cacheVirtualItemsEvent = new();
    private static readonly object s_retrieveVirtualItemEvent = new();
    private static readonly object s_searchForVirtualItemEvent = new();
    private static readonly object s_virtualItemsSelectionRangeChangedEvent = new();

    protected virtual void OnCacheVirtualItems(CacheVirtualItemsEventArgs e) => ((CacheVirtualItemsEventHandler?)Events[s_cacheVirtualItemsEvent])?.Invoke(this, e);

    protected virtual void OnRetrieveVirtualItem(RetrieveVirtualItemEventArgs e) => ((RetrieveVirtualItemEventHandler?)Events[s_retrieveVirtualItemEvent])?.Invoke(this, e);

    protected virtual void OnSearchForVirtualItem(SearchForVirtualItemEventArgs e) => ((SearchForVirtualItemEventHandler?)Events[s_searchForVirtualItemEvent])?.Invoke(this, e);

    protected virtual void OnVirtualItemsSelectionRangeChanged(ListViewVirtualItemsSelectionRangeChangedEventArgs e) =>
        ((ListViewVirtualItemsSelectionRangeChangedEventHandler?)Events[s_virtualItemsSelectionRangeChangedEvent])?.Invoke(this, e);

    /// <summary>Asks the owner, through RetrieveVirtualItem, for the item at this index; every ListViewItemCollection read in VirtualMode goes through here.</summary>
    internal ListViewItem GetVirtualItem(int index)
    {
        var args = new RetrieveVirtualItemEventArgs(index);
        OnRetrieveVirtualItem(args);

        return args.Item ?? throw new InvalidOperationException(
            $"RetrieveVirtualItem did not set Item for index {index}; a VirtualMode ListView needs a handler that always does.");
    }

    /// <summary>Hints the owner, through CacheVirtualItems, that items in this index range are about to be read repeatedly.</summary>
    internal void NotifyCacheVirtualItems(int startIndex, int endIndex) => OnCacheVirtualItems(new CacheVirtualItemsEventArgs(startIndex, endIndex));

    internal bool IsVirtualIndexSelected(int index) => _virtualSelectedIndices.Contains(index);

    internal int VirtualSelectedCount => _virtualSelectedIndices.Count;

    internal IEnumerable<int> VirtualSelectedIndices => _virtualSelectedIndices;

    /// <summary>Selects or deselects one virtual index, raising VirtualItemsSelectionRangeChanged the way SetItemSelected raises ItemSelectionChanged for a real item; an index outside [0, VirtualListSize) is rejected rather than left selected forever once the list shrinks past it.</summary>
    internal void SetVirtualItemSelected(int index, bool value)
    {
        if (index < 0 || index >= _virtualListSize) return;

        var changed = value ? _virtualSelectedIndices.Add(index) : _virtualSelectedIndices.Remove(index);
        if (!changed) return;

        OnVirtualItemsSelectionRangeChanged(new ListViewVirtualItemsSelectionRangeChangedEventArgs(index, index, value));
        Invalidate();
    }

    /// <summary>FindItemWithText and FindNearestItem delegate here instead of scanning Items, since VirtualMode items are not kept.</summary>
    private ListViewItem? FindVirtualItem(bool isTextSearch, bool isPrefixSearch, bool includeSubItemsInSearch, string? text, Point startingPoint, SearchDirectionHint direction, int startIndex)
    {
        var args = new SearchForVirtualItemEventArgs(isTextSearch, isPrefixSearch, includeSubItemsInSearch, text, startingPoint, direction, startIndex);
        OnSearchForVirtualItem(args);

        return args.Index >= 0 && args.Index < _virtualListSize ? GetVirtualItem(args.Index) : null;
    }

    /// <summary>
    /// The views VirtualMode lays out and paints for real this round: Details and List, both as
    /// one full-width column of uniform-height rows (List does not wrap into several columns here,
    /// since that needs measuring every item's text - the one thing VirtualMode exists to avoid).
    /// SmallIcon/LargeIcon still fall back to the normal _items-based layout, which is empty in
    /// VirtualMode and so shows nothing; left for a later round.
    /// </summary>
    private bool IsVirtualLayoutView => _virtualMode && _view is View.Details or View.List;

    private int VirtualRowHeight => ItemRowHeight;

    /// <summary>Content size for scroll bars/clamping, computed by arithmetic from VirtualListSize alone - never one Rectangle per item.</summary>
    private Size VirtualContentSize()
    {
        var width = _view == View.Details ? Math.Max(TotalColumnsWidth(), ContentRectangle.Width) : ContentRectangle.Width;
        return new Size(width, _virtualListSize * VirtualRowHeight);
    }

    /// <summary>The content-space rectangle (unscrolled) of one virtual row, by arithmetic.</summary>
    private Rectangle VirtualItemContentBounds(int index) => new(0, index * VirtualRowHeight, VirtualContentSize().Width, VirtualRowHeight);

    /// <summary>
    /// The inclusive range of row indices intersecting the visible viewport - the only rows
    /// VirtualMode ever computes a Rectangle for, paints, hit-tests, or asks RetrieveVirtualItem
    /// about, whether VirtualListSize is 10 or 10,000,000.
    /// </summary>
    private (int First, int Last) VisibleVirtualRange()
    {
        if (_virtualListSize == 0) return (0, -1);

        var viewport = ContentRectangle;
        if (viewport.Height <= 0) return (0, -1);

        // Exclusive bottom (height - 1), matching VirtualIndexAt's own "y < viewport.Bottom" check:
        // without the -1, a viewport whose height is an exact multiple of the row height counts one
        // row past the last pixel actually drawn as "visible" too.
        var rowHeight = Math.Max(VirtualRowHeight, 1);
        var first = Math.Max(_scrollY / rowHeight, 0);
        var last = Math.Min((_scrollY + viewport.Height - 1) / rowHeight, _virtualListSize - 1);

        return (first, last);
    }

    /// <summary>The virtual row index under client-space y, or -1 outside the viewport or past the last item.</summary>
    private int VirtualIndexAt(int y)
    {
        var viewport = ContentRectangle;
        if (y < viewport.Top || y >= viewport.Bottom) return -1;

        var index = (y - viewport.Top + _scrollY) / Math.Max(VirtualRowHeight, 1);
        return index >= 0 && index < _virtualListSize ? index : -1;
    }

    /// <summary>Raises CacheVirtualItems, but only once for a given visible range: scrolling by a pixel that does not cross a row boundary must not re-raise it on every paint.</summary>
    private void NotifyCacheVirtualItemsIfRangeChanged(int first, int last)
    {
        if (_lastNotifiedVirtualRange is { } previous && previous.First == first && previous.Last == last) return;

        _lastNotifiedVirtualRange = (first, last);
        NotifyCacheVirtualItems(first, last);
    }

    /// <summary>Mouse-click selection for a virtual row, mirroring SelectFromMouse's Shift-range/Ctrl-toggle/plain-replace rules but by index, since virtual items have no identity of their own.</summary>
    private void SelectVirtualFromMouse(int index, MouseEventArgs e)
    {
        var shift = (ModifierKeys & Keys.Shift) != 0;
        var control = (ModifierKeys & Keys.Control) != 0;

        if (_multiSelect && shift && RangeAnchorVirtual() is { } anchor)
        {
            SelectVirtualRange(anchor, index);
        }
        else if (_multiSelect && control)
        {
            SetVirtualItemSelected(index, !IsVirtualIndexSelected(index));
            _virtualSelectionAnchor = index;
        }
        else
        {
            ClearVirtualSelection();
            SetVirtualItemSelected(index, true);
            _virtualSelectionAnchor = index;
        }

        _virtualFocusedIndex = index;
    }

    /// <summary>The anchor if VirtualListSize still reaches it, else the focused index - DefaultKeyDownVirtual's and SelectVirtualFromMouse's counterpart to the real ListView's RangeAnchor().</summary>
    private int? RangeAnchorVirtual() => _virtualSelectionAnchor ?? _virtualFocusedIndex;

    private void SelectVirtualRange(int from, int to)
    {
        var low = Math.Min(from, to);
        var high = Math.Max(from, to);

        // Only indices whose state really changes are touched, so each raises VirtualItemsSelectionRangeChanged once.
        foreach (var selected in _virtualSelectedIndices.ToArray())
        {
            if (selected < low || selected > high) SetVirtualItemSelected(selected, false);
        }

        for (var i = low; i <= high; i++) SetVirtualItemSelected(i, true);
    }

    private void ClearVirtualSelection()
    {
        foreach (var selected in _virtualSelectedIndices.ToArray()) SetVirtualItemSelected(selected, false);
    }

    /// <summary>
    /// DefaultKeyDown's VirtualMode path: Up/Down/Home/End/PageUp/PageDown move by arithmetic over
    /// VirtualListSize, never by scanning _items (always empty here). List's own Left/Right
    /// column-wrap (RowsPerListColumn) is not implemented here, since VirtualMode lays List out as
    /// one column (see IsVirtualLayoutView) with no columns to wrap between. Enter still raises
    /// ItemActivate, but Space and F2 are left as documented no-ops (see their own comments below)
    /// rather than touching a RetrieveVirtualItem result that has no identity to toggle or edit.
    /// </summary>
    private void DefaultKeyDownVirtual(KeyEventArgs e)
    {
        var currentIndex = _virtualFocusedIndex ?? -1;

        if (e.KeyCode == Keys.Space)
        {
            // CheckBoxes in VirtualMode need the host to persist checked state itself (RetrieveVirtualItem
            // returns a fresh item with no identity ListView can hold onto between calls), so there is
            // nothing of this list's own to toggle here.
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.F2)
        {
            // LabelEdit's BeginEditCore needs a real, already-added ListViewItem (it looks the item up
            // in _items through IndexOfItem); a VirtualMode item from RetrieveVirtualItem has no place there.
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Enter && currentIndex >= 0)
        {
            OnItemActivate(EventArgs.Empty);
            e.Handled = true;
            return;
        }

        var target = NextVirtualIndexForKey(e.KeyCode, currentIndex);
        if (target is null) return;

        MoveVirtualFocusTo(target.Value, e.Shift, e.Control);
        e.Handled = true;
    }

    /// <summary>NextIndexForKey's VirtualMode counterpart: the same keys, laid out the same way (Details/List only - see IsVirtualLayoutView), but against VirtualListSize instead of _items.Count.</summary>
    private int? NextVirtualIndexForKey(Keys keyCode, int current)
    {
        var start = current >= 0 ? current : 0;
        var last = _virtualListSize - 1;

        return keyCode switch
        {
            Keys.Up => Math.Max(start - 1, 0),
            Keys.Down => Math.Min(start + 1, last),
            Keys.Home => 0,
            Keys.End => last,
            Keys.PageUp => Math.Max(start - VisibleRowCount(), 0),
            Keys.PageDown => Math.Min(start + VisibleRowCount(), last),
            _ => null,
        };
    }

    /// <summary>MoveFocusTo's VirtualMode counterpart: Shift extends the range from RangeAnchorVirtual() without moving the anchor; Ctrl moves focus alone; otherwise the old selection is replaced and the anchor follows.</summary>
    private void MoveVirtualFocusTo(int newIndex, bool shift, bool control)
    {
        if (_multiSelect && shift && RangeAnchorVirtual() is { } anchor)
        {
            SelectVirtualRange(anchor, newIndex);
        }
        else if (!(control && _multiSelect))
        {
            ClearVirtualSelection();
            SetVirtualItemSelected(newIndex, true);
            _virtualSelectionAnchor = newIndex;
        }

        _virtualFocusedIndex = newIndex;
        EnsureVisible(newIndex);
    }
}
