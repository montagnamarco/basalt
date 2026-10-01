using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Skins;

namespace System.Windows.Forms;

/// <summary>
/// A list of items the user picks from by clicking or by the keyboard: one,
/// several, or none, depending on <see cref="SelectionMode"/>.
/// </summary>
/// <remarks>
/// ListBox is native in WinForms: it paints and handles its own mouse and
/// keys through Control's default-procedure hooks (DefaultPaint,
/// DefaultMouseDown/Move/Up, DefaultKeyDown), not OnPaint/OnMouseDown/OnKeyDown.
/// Its vertical scroll bar is the window's own frame, shown through
/// CreateParams and drawn from GetWindowScroll, exactly like a native list
/// box's WS_VSCROLL - not the child-scrolling of ScrollableControl.
/// </remarks>
[DefaultProperty("Items")]
[DefaultEvent("SelectedIndexChanged")]
public partial class ListBox : ListControl
{
    public const int DefaultItemHeight = 13;
    public const int NoMatches = -1;

    // The single source of truth for every nested collection: ObjectCollection,
    // SelectedIndexCollection and SelectedObjectCollection all read and write
    // these fields directly, as nested types may.
    private readonly List<object> _items = [];
    private readonly List<int> _selectedIndices = [];
    private readonly List<int> _variableHeights = [];

    private ObjectCollection? _itemsCollection;
    private SelectedIndexCollection? _selectedIndicesCollection;
    private SelectedObjectCollection? _selectedObjectsCollection;
    private IntegerCollection? _customTabOffsets;

    /// <summary>The item with the keyboard cursor; not necessarily selected (MultiSimple moves it independently).</summary>
    private int _focusedIndex = -1;

    /// <summary>Where a Shift range in MultiExtended started.</summary>
    private int _selectionAnchor = -1;

    private int _topIndex;
    private int _itemHeight;

    private BorderStyle _borderStyle = BorderStyle.Fixed3D;
    private DrawMode _drawMode = DrawMode.Normal;
    private SelectionMode _selectionMode = SelectionMode.One;
    private bool _sorted;
    private bool _integralHeight = true;
    private bool _multiColumn;
    private int _columnWidth;
    private int _horizontalExtent;
    private bool _horizontalScrollbar;
    private bool _scrollAlwaysVisible;
    private bool _useTabStops = true;
    private bool _useCustomTabOffsets;

    private int _updateCount;

    private bool _selectingWithMouse;
    private (bool Vertical, int GrabOffset)? _thumbDrag;

    public ListBox()
    {
        // A native list box paints and handles input itself; it does not go through OnPaint/OnMouseDown.
        SetStyle(ControlStyles.UserPaint, false);
        _itemHeight = Math.Max(FontHeight, 1);
    }

    protected override bool AllowSelection => _selectionMode != SelectionMode.None;

    public override Color BackColor
    {
        get => base.BackColor;
        set => base.BackColor = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Image? BackgroundImage
    {
        get => base.BackgroundImage;
        set => base.BackgroundImage = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override ImageLayout BackgroundImageLayout
    {
        get => base.BackgroundImageLayout;
        set => base.BackgroundImageLayout = value;
    }

    [DefaultValue(BorderStyle.Fixed3D)]
    public BorderStyle BorderStyle
    {
        get => _borderStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(BorderStyle));
            if (value == _borderStyle) return;

            _borderStyle = value;
            UpdateStyles();
            AdjustHeight();
        }
    }

    [DefaultValue(0)]
    public int ColumnWidth
    {
        get => _columnWidth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (value == _columnWidth) return;

            _columnWidth = value;
            if (_multiColumn) Invalidate();
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;

            if (_borderStyle == BorderStyle.Fixed3D) parameters.ExStyle |= WindowStyles.ExClientEdge;
            else if (_borderStyle == BorderStyle.FixedSingle) parameters.Style |= WindowStyles.Border;

            if (NeedsVerticalScrollBar) parameters.Style |= WindowStyles.VerticalScroll;

            return parameters;
        }
    }

    [Browsable(false)]
    public IntegerCollection CustomTabOffsets => _customTabOffsets ??= new IntegerCollection(this);

    protected override Size DefaultSize => new(120, 96);

    [DefaultValue(DrawMode.Normal)]
    public virtual DrawMode DrawMode
    {
        get => _drawMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(DrawMode));
            if (value == _drawMode) return;
            if (value == DrawMode.OwnerDrawVariable && _multiColumn) throw new ArgumentException("OwnerDrawVariable cannot be combined with MultiColumn.", nameof(value));

            _drawMode = value;
            if (value == DrawMode.OwnerDrawVariable) RemeasureAllItems();

            AdjustHeight();
            Invalidate();
        }
    }

    public override Font Font
    {
        get => base.Font;
        set => base.Font = value;
    }

    public override Color ForeColor
    {
        get => base.ForeColor;
        set => base.ForeColor = value;
    }

    [DefaultValue(0)]
    [Localizable(true)]
    public int HorizontalExtent
    {
        get => _horizontalExtent;
        set
        {
            if (value == _horizontalExtent) return;

            _horizontalExtent = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    [Localizable(true)]
    public bool HorizontalScrollbar
    {
        get => _horizontalScrollbar;
        set
        {
            if (value == _horizontalScrollbar) return;

            _horizontalScrollbar = value;
            Invalidate();
        }
    }

    [DefaultValue(true)]
    [Localizable(true)]
    public bool IntegralHeight
    {
        get => _integralHeight;
        set
        {
            if (value == _integralHeight) return;

            _integralHeight = value;
            AdjustHeight();
        }
    }

    [Localizable(true)]
    public virtual int ItemHeight
    {
        get => _itemHeight;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (value == _itemHeight) return;

            _itemHeight = value;
            AdjustHeight();
            Invalidate();
        }
    }

    [Localizable(true)]
    public ObjectCollection Items => _itemsCollection ??= CreateItemCollection();

    [DefaultValue(false)]
    public bool MultiColumn
    {
        get => _multiColumn;
        set
        {
            if (value == _multiColumn) return;
            if (value && _drawMode == DrawMode.OwnerDrawVariable) throw new ArgumentException("MultiColumn cannot be combined with OwnerDrawVariable.", nameof(value));

            _multiColumn = value;
            UpdateStyles();
            Invalidate();
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new Padding Padding
    {
        get => base.Padding;
        set => base.Padding = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public int PreferredHeight
    {
        get
        {
            var rows = Math.Max(_items.Count, 1);
            return rows * Math.Max(_itemHeight, 1) + NonClient.Vertical;
        }
    }

    [DefaultValue(false)]
    [Localizable(true)]
    public bool ScrollAlwaysVisible
    {
        get => _scrollAlwaysVisible;
        set
        {
            if (value == _scrollAlwaysVisible) return;

            _scrollAlwaysVisible = value;
            UpdateStyles();
            Invalidate();
        }
    }

    public override int SelectedIndex
    {
        get => _selectedIndices.Count > 0 ? _selectedIndices[0] : -1;
        set
        {
            if (value < -1 || value >= _items.Count) throw new ArgumentOutOfRangeException(nameof(value));

            if (value == -1) ClearSelected();
            else SelectOnly(value);
        }
    }

    [Browsable(false)]
    public SelectedIndexCollection SelectedIndices => _selectedIndicesCollection ??= new SelectedIndexCollection(this);

    [Browsable(false)]
    public object? SelectedItem
    {
        get => SelectedIndex >= 0 ? _items[SelectedIndex] : null;
        set
        {
            if (value is null)
            {
                ClearSelected();
                return;
            }

            var index = _items.IndexOf(value);
            if (index >= 0) SelectOnly(index);
            else ClearSelected();
        }
    }

    [Browsable(false)]
    public SelectedObjectCollection SelectedItems => _selectedObjectsCollection ??= new SelectedObjectCollection(this);

    [DefaultValue(SelectionMode.One)]
    public virtual SelectionMode SelectionMode
    {
        get => _selectionMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(SelectionMode));
            if (value == _selectionMode) return;

            var selectionBefore = _selectedIndices.Count;
            _selectionMode = value;

            // Only None and One can drop selected items; other switches keep the selection and raise nothing.
            if (value == SelectionMode.None) _selectedIndices.Clear();
            else if (value == SelectionMode.One && _selectedIndices.Count > 1)
            {
                var first = _selectedIndices[0];
                _selectedIndices.Clear();
                _selectedIndices.Add(first);
            }

            Invalidate();
            RaiseSelectionChangedIfNeeded(_selectedIndices.Count != selectionBefore);
        }
    }

    [DefaultValue(false)]
    public bool Sorted
    {
        get => _sorted;
        set
        {
            if (value == _sorted) return;

            _sorted = value;
            if (value) Sort();
        }
    }

    [Bindable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [AllowNull]
    public override string Text
    {
        get
        {
            if (_selectionMode != SelectionMode.None && SelectedItem is { } item) return GetItemText(item);

            return base.Text;
        }
        set
        {
            base.Text = value;

            if (_selectionMode == SelectionMode.None) return;

            var index = FindStringExact(value ?? string.Empty);
            if (index >= 0) SelectedIndex = index;
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int TopIndex
    {
        get => _topIndex;
        set
        {
            var clamped = ClampTopIndex(value);
            if (clamped == _topIndex) return;

            _topIndex = clamped;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    [Browsable(false)]
    public bool UseCustomTabOffsets
    {
        get => _useCustomTabOffsets;
        set
        {
            if (value == _useCustomTabOffsets) return;

            _useCustomTabOffsets = value;
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool UseTabStops
    {
        get => _useTabStops;
        set
        {
            if (value == _useTabStops) return;

            _useTabStops = value;
            Invalidate();
        }
    }

    public new event EventHandler? BackgroundImageChanged
    {
        add => base.BackgroundImageChanged += value;
        remove => base.BackgroundImageChanged -= value;
    }

    public new event EventHandler? BackgroundImageLayoutChanged
    {
        add => base.BackgroundImageLayoutChanged += value;
        remove => base.BackgroundImageLayoutChanged -= value;
    }

    public new event EventHandler? Click
    {
        add => base.Click += value;
        remove => base.Click -= value;
    }

    public event DrawItemEventHandler? DrawItem
    {
        add => Events.AddHandler(s_drawItemEvent, value);
        remove => Events.RemoveHandler(s_drawItemEvent, value);
    }

    public event MeasureItemEventHandler? MeasureItem
    {
        add => Events.AddHandler(s_measureItemEvent, value);
        remove => Events.RemoveHandler(s_measureItemEvent, value);
    }

    public new event MouseEventHandler? MouseClick
    {
        add => base.MouseClick += value;
        remove => base.MouseClick -= value;
    }

    public new event EventHandler? PaddingChanged
    {
        add => base.PaddingChanged += value;
        remove => base.PaddingChanged -= value;
    }

    public new event PaintEventHandler? Paint
    {
        add => base.Paint += value;
        remove => base.Paint -= value;
    }

    public event EventHandler? SelectedIndexChanged
    {
        add => Events.AddHandler(s_selectedIndexChangedEvent, value);
        remove => Events.RemoveHandler(s_selectedIndexChangedEvent, value);
    }

    public new event EventHandler? TextChanged
    {
        add => base.TextChanged += value;
        remove => base.TextChanged -= value;
    }

    private static readonly object s_drawItemEvent = new();
    private static readonly object s_measureItemEvent = new();
    private static readonly object s_selectedIndexChangedEvent = new();

    [Obsolete("This method has been deprecated.  There is no replacement.")]
    protected virtual void AddItemsCore(object[] value) => Items.AddRange(value);

    public void BeginUpdate()
    {
        _updateCount++;
        SuspendLayout();
    }

    public void ClearSelected()
    {
        if (_selectedIndices.Count == 0) return;

        foreach (var index in _selectedIndices.ToArray()) InvalidateItem(index);
        _selectedIndices.Clear();

        RaiseSelectionChangedIfNeeded(true);
    }

    protected virtual ObjectCollection CreateItemCollection() => new(this);

    public void EndUpdate()
    {
        if (_updateCount > 0) _updateCount--;

        ResumeLayout(_updateCount == 0);
        if (_updateCount == 0) Invalidate();
    }

    public int FindString(string s) => FindStringCore(s, -1, exact: false);

    public int FindString(string s, int startIndex) => FindStringCore(s, startIndex, exact: false);

    public int FindStringExact(string s) => FindStringCore(s, -1, exact: true);

    public int FindStringExact(string s, int startIndex) => FindStringCore(s, startIndex, exact: true);

    public int GetItemHeight(int index)
    {
        if (index < 0 || index >= Math.Max(_items.Count, 1)) throw new ArgumentOutOfRangeException(nameof(index));

        return _drawMode == DrawMode.OwnerDrawVariable && index < _variableHeights.Count ? _variableHeights[index] : _itemHeight;
    }

    public Rectangle GetItemRectangle(int index)
    {
        if (index < 0 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index));

        if (_multiColumn)
        {
            var rows = RowsPerColumn();
            var relative = index - _topIndex;
            var width = ColumnWidthEffective();
            var column = relative >= 0 ? relative / rows : (relative - rows + 1) / rows;
            var row = relative - column * rows;

            return new Rectangle(column * width, row * _itemHeight, width, _itemHeight);
        }

        if (_drawMode == DrawMode.OwnerDrawVariable)
        {
            var y = 0;
            if (index >= _topIndex) for (var i = _topIndex; i < index; i++) y += GetItemHeight(i);
            else for (var i = index; i < _topIndex; i++) y -= GetItemHeight(i);

            return new Rectangle(0, y, ClientRectangle.Width, GetItemHeight(index));
        }

        return new Rectangle(0, (index - _topIndex) * _itemHeight, ClientRectangle.Width, _itemHeight);
    }

    public bool GetSelected(int index)
    {
        if (index < 0 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index));

        return _selectedIndices.Contains(index);
    }

    public int IndexFromPoint(Point p) => IndexFromPoint(p.X, p.Y);

    public int IndexFromPoint(int x, int y)
    {
        if (_items.Count == 0 || y < 0 || x < 0) return NoMatches;

        if (_multiColumn)
        {
            var width = Math.Max(ColumnWidthEffective(), 1);
            var rows = RowsPerColumn();
            var row = y / Math.Max(_itemHeight, 1);
            if (row >= rows) return NoMatches;

            var index = _topIndex + (x / width) * rows + row;
            return index >= 0 && index < _items.Count ? index : NoMatches;
        }

        if (_drawMode == DrawMode.OwnerDrawVariable)
        {
            var top = 0;
            for (var i = _topIndex; i < _items.Count; i++)
            {
                var height = GetItemHeight(i);
                if (y < top + height) return i;
                top += height;
            }

            return NoMatches;
        }

        var singleIndex = _topIndex + y / Math.Max(_itemHeight, 1);
        return singleIndex >= 0 && singleIndex < _items.Count ? singleIndex : NoMatches;
    }

    protected virtual void OnDrawItem(DrawItemEventArgs e) => ((DrawItemEventHandler?)Events[s_drawItemEvent])?.Invoke(this, e);

    protected virtual void OnMeasureItem(MeasureItemEventArgs e) => ((MeasureItemEventHandler?)Events[s_measureItemEvent])?.Invoke(this, e);

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        ((EventHandler?)Events[s_selectedIndexChangedEvent])?.Invoke(this, e);
    }

    public override void Refresh()
    {
        if (_drawMode == DrawMode.OwnerDrawVariable) RemeasureAllItems();
        base.Refresh();
    }

    protected override void RefreshItem(int index)
    {
        if (index < 0 || index >= _items.Count) return;

        if (_drawMode == DrawMode.OwnerDrawVariable) MeasureVariableItem(index);
        InvalidateItem(index);
    }

    protected override void RefreshItems()
    {
        if (_drawMode == DrawMode.OwnerDrawVariable) RemeasureAllItems();
        Invalidate();
    }

    public override void ResetBackColor() => base.ResetBackColor();

    public override void ResetForeColor() => base.ResetForeColor();

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if ((specified & BoundsSpecified.Height) != 0) height = RoundedHeight(height);

        base.SetBoundsCore(x, y, width, height, specified);
    }

    protected override void SetItemCore(int index, object value)
    {
        if (index < 0 || index >= _items.Count || value is null) return;

        _items[index] = value;
        OnItemReplaced(index);
    }

    protected override void SetItemsCore(IList value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Items.ResetFromBinding(value);
    }

    public void SetSelected(int index, bool value)
    {
        if (index < 0 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (!AllowSelection) return;

        var changed = SelectIndex(index, value);
        if (changed)
        {
            _focusedIndex = index;
            _selectionAnchor = index;
        }

        RaiseSelectionChangedIfNeeded(changed);
    }

    protected virtual void Sort()
    {
        if (_items.Count == 0) return;

        // Selection, focus, anchor and measured heights belong to the items, so they move with them.
        var oldIndexAt = Enumerable.Range(0, _items.Count)
            .OrderBy(index => GetItemText(_items[index]), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var newIndexOf = new int[oldIndexAt.Length];
        for (var newIndex = 0; newIndex < oldIndexAt.Length; newIndex++) newIndexOf[oldIndexAt[newIndex]] = newIndex;

        ItemsReorderedInternal(oldIndexAt);

        var sortedItems = oldIndexAt.Select(index => _items[index]).ToArray();
        _items.Clear();
        _items.AddRange(sortedItems);

        if (_variableHeights.Count == oldIndexAt.Length)
        {
            var sortedHeights = oldIndexAt.Select(index => _variableHeights[index]).ToArray();
            _variableHeights.Clear();
            _variableHeights.AddRange(sortedHeights);
        }

        var selected = _selectedIndices.Select(index => newIndexOf[index]).Order().ToArray();
        _selectedIndices.Clear();
        _selectedIndices.AddRange(selected);

        if (_focusedIndex >= 0) _focusedIndex = newIndexOf[_focusedIndex];
        if (_selectionAnchor >= 0) _selectionAnchor = newIndexOf[_selectionAnchor];

        Invalidate();
    }

    public override string ToString() => $"{base.ToString()}, Items.Count: {_items.Count}";
}
