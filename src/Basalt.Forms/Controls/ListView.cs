using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// A list of items shown as a grid of icons, a single column of icons, or a
/// multi-column grid of rows (<see cref="View"/>), each one optionally
/// checkable and clustered under a <see cref="ListViewGroup"/>.
/// </summary>
/// <remarks>
/// Native in WinForms: it paints and handles its own mouse and keys through
/// Control's default-procedure hooks (DefaultPaint, DefaultMouseDown/Move/Up,
/// DefaultKeyDown), not OnPaint/OnMouseDown/OnKeyDown. Its scroll bars are the
/// window's own frame, shown through CreateParams and drawn from
/// GetWindowScroll, exactly like ListBox's.
/// </remarks>
[DefaultProperty("Items")]
[DefaultEvent("SelectedIndexChanged")]
public partial class ListView : Control
{
    // The single source of truth for the item and column collections: the
    // nested collection types below read and write these fields directly.
    private readonly List<ListViewItem> _items = [];
    private readonly List<ColumnHeader> _columns = [];
    private readonly List<ListViewGroup> _groups = [];

    /// <summary>Selected items, by reference: avoids renumbering on every insert or removal, unlike an index set would.</summary>
    private readonly HashSet<ListViewItem> _selected = [];

    /// <summary>Checked items, by reference, for the same reason.</summary>
    private readonly HashSet<ListViewItem> _checkedItems = [];

    private ListViewItemCollection? _itemsCollection;
    private ColumnHeaderCollection? _columnsCollection;
    private ListViewGroupCollection? _groupsCollection;
    private SelectedIndexCollection? _selectedIndicesCollection;
    private SelectedListViewItemCollection? _selectedItemsCollection;
    private CheckedIndexCollection? _checkedIndicesCollection;
    private CheckedListViewItemCollection? _checkedItemsCollection;

    private View _view = View.LargeIcon;
    private BorderStyle _borderStyle = BorderStyle.Fixed3D;
    private ItemActivation _activation = ItemActivation.Standard;
    private ColumnHeaderStyle _headerStyle = ColumnHeaderStyle.Clickable;
    private SortOrder _sorting = SortOrder.None;
    private IComparer? _listViewItemSorter;
    private ImageList? _largeImageList;
    private ImageList? _smallImageList;
    private ListViewItem? _focusedItem;

    /// <summary>Where a Shift range starts: the last item chosen without Shift, which repeated Shift gestures pivot on.</summary>
    private ListViewItem? _selectionAnchor;
    private bool _checkBoxes;
    private bool _fullRowSelect;
    private bool _gridLines;
    private bool _hideSelection;
    private bool _multiSelect = true;
    private bool _showGroups = true;
    private int _updateCount;

    /// <summary>Content scrolled past, in pixels: rows above for the vertical axis, columns or icons to the left for the horizontal one.</summary>
    private int _scrollY;
    private int _scrollX;

    public ListView()
    {
        // A native list view paints and handles input itself; it does not go through OnPaint/OnMouseDown.
        SetStyle(ControlStyles.UserPaint, false);
        MouseDoubleClick += OnMouseDoubleClickCore;
    }

    [DefaultValue(ItemActivation.Standard)]
    public ItemActivation Activation
    {
        get => _activation;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ItemActivation));
            _activation = value;
        }
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
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool CheckBoxes
    {
        get => _checkBoxes;
        set
        {
            if (value == _checkBoxes) return;

            _checkBoxes = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    public CheckedIndexCollection CheckedIndices => _checkedIndicesCollection ??= new CheckedIndexCollection(this);

    [Browsable(false)]
    public CheckedListViewItemCollection CheckedItems => _checkedItemsCollection ??= new CheckedListViewItemCollection(this);

    [MergableProperty(false)]
    public ColumnHeaderCollection Columns => _columnsCollection ??= new ColumnHeaderCollection(this);

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;

            if (_borderStyle == BorderStyle.Fixed3D) parameters.ExStyle |= WindowStyles.ExClientEdge;
            else if (_borderStyle == BorderStyle.FixedSingle) parameters.Style |= WindowStyles.Border;

            if (NeedsVerticalScrollBar) parameters.Style |= WindowStyles.VerticalScroll;
            if (NeedsHorizontalScrollBar) parameters.Style |= WindowStyles.HorizontalScroll;

            return parameters;
        }
    }

    protected override Size DefaultSize => new(121, 97);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ListViewItem? FocusedItem
    {
        get => _focusedItem;
        set
        {
            if (ReferenceEquals(_focusedItem, value)) return;
            if (value is not null && value.ListView != this) throw new ArgumentException("The item does not belong to this ListView.", nameof(value));

            var previous = _focusedItem;
            if (previous is not null) previous.Focused = false;

            _focusedItem = value;
            if (value is not null) value.Focused = true;
        }
    }

    [DefaultValue(false)]
    public bool FullRowSelect
    {
        get => _fullRowSelect;
        set
        {
            if (value == _fullRowSelect) return;

            _fullRowSelect = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool GridLines
    {
        get => _gridLines;
        set
        {
            if (value == _gridLines) return;

            _gridLines = value;
            Invalidate();
        }
    }

    [MergableProperty(false)]
    public ListViewGroupCollection Groups => _groupsCollection ??= new ListViewGroupCollection(this);

    [DefaultValue(ColumnHeaderStyle.Clickable)]
    public ColumnHeaderStyle HeaderStyle
    {
        get => _headerStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ColumnHeaderStyle));
            if (value == _headerStyle) return;

            _headerStyle = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool HideSelection
    {
        get => _hideSelection;
        set
        {
            if (value == _hideSelection) return;

            _hideSelection = value;
            Invalidate();
        }
    }

    [MergableProperty(false)]
    public ListViewItemCollection Items => _itemsCollection ??= new ListViewItemCollection(this);

    [DefaultValue(null)]
    public ImageList? LargeImageList
    {
        get => _largeImageList;
        set
        {
            if (value == _largeImageList) return;

            _largeImageList = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    public IComparer? ListViewItemSorter
    {
        get => _listViewItemSorter;
        set => _listViewItemSorter = value;
    }

    [DefaultValue(true)]
    public bool MultiSelect
    {
        get => _multiSelect;
        set => _multiSelect = value;
    }

    [Browsable(false)]
    public SelectedIndexCollection SelectedIndices => _selectedIndicesCollection ??= new SelectedIndexCollection(this);

    [Browsable(false)]
    public SelectedListViewItemCollection SelectedItems => _selectedItemsCollection ??= new SelectedListViewItemCollection(this);

    [DefaultValue(true)]
    public bool ShowGroups
    {
        get => _showGroups;
        set
        {
            if (value == _showGroups) return;

            _showGroups = value;
            Invalidate();
        }
    }

    [DefaultValue(null)]
    public ImageList? SmallImageList
    {
        get => _smallImageList;
        set
        {
            if (value == _smallImageList) return;

            _smallImageList = value;
            Invalidate();
        }
    }

    [DefaultValue(SortOrder.None)]
    public SortOrder Sorting
    {
        get => _sorting;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(SortOrder));
            if (value == _sorting) return;

            _sorting = value;
            if (value != SortOrder.None) Sort();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ListViewItem? TopItem
    {
        get => TopItemCore();
        set
        {
            if (value is null) return;
            EnsureVisible(IndexOfItem(value));
        }
    }

    [DefaultValue(View.LargeIcon)]
    public View View
    {
        get => _view;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(View));
            if (value == _view) return;

            _view = value;
            _scrollX = 0;
            _scrollY = 0;
            UpdateStyles();
            Invalidate();
        }
    }

    public event ColumnClickEventHandler? ColumnClick
    {
        add => Events.AddHandler(s_columnClickEvent, value);
        remove => Events.RemoveHandler(s_columnClickEvent, value);
    }

    public event ColumnWidthChangedEventHandler? ColumnWidthChanged
    {
        add => Events.AddHandler(s_columnWidthChangedEvent, value);
        remove => Events.RemoveHandler(s_columnWidthChangedEvent, value);
    }

    public event EventHandler? ItemActivate
    {
        add => Events.AddHandler(s_itemActivateEvent, value);
        remove => Events.RemoveHandler(s_itemActivateEvent, value);
    }

    public event ItemCheckEventHandler? ItemCheck
    {
        add => Events.AddHandler(s_itemCheckEvent, value);
        remove => Events.RemoveHandler(s_itemCheckEvent, value);
    }

    public event ItemCheckedEventHandler? ItemChecked
    {
        add => Events.AddHandler(s_itemCheckedEvent, value);
        remove => Events.RemoveHandler(s_itemCheckedEvent, value);
    }

    public event ListViewItemSelectionChangedEventHandler? ItemSelectionChanged
    {
        add => Events.AddHandler(s_itemSelectionChangedEvent, value);
        remove => Events.RemoveHandler(s_itemSelectionChangedEvent, value);
    }

    public event EventHandler? SelectedIndexChanged
    {
        add => Events.AddHandler(s_selectedIndexChangedEvent, value);
        remove => Events.RemoveHandler(s_selectedIndexChangedEvent, value);
    }

    private static readonly object s_columnClickEvent = new();
    private static readonly object s_columnWidthChangedEvent = new();
    private static readonly object s_itemActivateEvent = new();
    private static readonly object s_itemCheckEvent = new();
    private static readonly object s_itemCheckedEvent = new();
    private static readonly object s_itemSelectionChangedEvent = new();
    private static readonly object s_selectedIndexChangedEvent = new();

    protected virtual void OnColumnClick(ColumnClickEventArgs e) => ((ColumnClickEventHandler?)Events[s_columnClickEvent])?.Invoke(this, e);

    protected virtual void OnColumnWidthChanged(ColumnWidthChangedEventArgs e) => ((ColumnWidthChangedEventHandler?)Events[s_columnWidthChangedEvent])?.Invoke(this, e);

    protected virtual void OnItemActivate(EventArgs e) => ((EventHandler?)Events[s_itemActivateEvent])?.Invoke(this, e);

    protected virtual void OnItemCheck(ItemCheckEventArgs ice) => ((ItemCheckEventHandler?)Events[s_itemCheckEvent])?.Invoke(this, ice);

    protected virtual void OnItemChecked(ItemCheckedEventArgs e) => ((ItemCheckedEventHandler?)Events[s_itemCheckedEvent])?.Invoke(this, e);

    protected virtual void OnItemSelectionChanged(ListViewItemSelectionChangedEventArgs e) => ((ListViewItemSelectionChangedEventHandler?)Events[s_itemSelectionChangedEvent])?.Invoke(this, e);

    protected virtual void OnSelectedIndexChanged(EventArgs e) => ((EventHandler?)Events[s_selectedIndexChangedEvent])?.Invoke(this, e);

    private void OnMouseDoubleClickCore(object? sender, MouseEventArgs e)
    {
        // Only Activation.Standard is implemented: OneClick/TwoClick hover-activation is left out.
        if (_activation != ItemActivation.Standard) return;
        if (GetItemAt(e.X, e.Y) is not null) OnItemActivate(EventArgs.Empty);
    }

    public void BeginUpdate()
    {
        _updateCount++;
        SuspendLayout();
    }

    public void Clear()
    {
        Items.Clear();
        Columns.Clear();
    }

    public void EndUpdate()
    {
        if (_updateCount > 0) _updateCount--;

        ResumeLayout(_updateCount == 0);
        if (_updateCount == 0) Invalidate();
    }

    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= _items.Count) return;

        ScrollIntoView(ItemContentBounds(index));
    }

    public ListViewItem? GetItemAt(int x, int y) => HitTest(x, y).Item;

    public Rectangle GetItemRect(int index) => GetItemRect(index, ItemBoundsPortion.Entire);

    public Rectangle GetItemRect(int index, ItemBoundsPortion portion)
    {
        if (index < 0 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index));

        return ItemPortionBounds(index, portion);
    }

    public new ListViewHitTestInfo HitTest(Point point) => HitTest(point.X, point.Y);

    public ListViewHitTestInfo HitTest(int x, int y) => HitTestCore(x, y);

    public void Sort()
    {
        if (_items.Count == 0) return;

        Comparison<ListViewItem> comparison = _listViewItemSorter is not null
            ? (a, b) => _listViewItemSorter.Compare(a, b)
            : (a, b) => string.Compare(a.Text, b.Text, StringComparison.CurrentCultureIgnoreCase);

        if (_sorting == SortOrder.Descending && _listViewItemSorter is null)
        {
            var ascending = comparison;
            comparison = (a, b) => -ascending(a, b);
        }

        _items.Sort(comparison);
        Invalidate();
    }

    public override string ToString() => $"{base.ToString()}, Items.Count: {_items.Count}";
}
