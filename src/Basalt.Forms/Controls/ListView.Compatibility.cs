using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

public partial class ListView
{
    private ListViewAlignment _alignment = ListViewAlignment.Top;
    private bool _autoArrange = true;
    private bool _backgroundImageTiled;
    private ImageList? _groupImageList;
    private bool _labelWrap = true;
    private bool _rightToLeftLayout;
    private bool _scrollable = true;
    private Size _tileSize = Size.Empty;
    private bool _useCompatibleStateImageBehavior = true;
    private ListViewItem? _dragCandidate;
    private Point _dragOrigin;

    private static readonly object s_groupCollapsedStateChangedEvent = new();
    private static readonly object s_groupTaskLinkClickEvent = new();
    private static readonly object s_listItemDragEvent = new();
    private static readonly object s_listRightToLeftLayoutChangedEvent = new();

    [DefaultValue(ListViewAlignment.Top)]
    public ListViewAlignment Alignment
    {
        get => _alignment;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ListViewAlignment));
            if (_alignment == value) return;
            _alignment = value;
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool AutoArrange
    {
        get => _autoArrange;
        set { if (_autoArrange == value) return; _autoArrange = value; Invalidate(); }
    }

    [DefaultValue(false)]
    public bool BackgroundImageTiled
    {
        get => _backgroundImageTiled;
        set
        {
            if (_backgroundImageTiled == value) return;
            _backgroundImageTiled = value;
            BackgroundImageLayout = value ? ImageLayout.Tile : ImageLayout.None;
        }
    }

    [DefaultValue(null)]
    public ImageList? GroupImageList
    {
        get => _groupImageList;
        set { if (ReferenceEquals(_groupImageList, value)) return; _groupImageList = value; InvalidateHeader(); }
    }

    [DefaultValue(true)]
    public bool LabelWrap
    {
        get => _labelWrap;
        set { if (_labelWrap == value) return; _labelWrap = value; Invalidate(); }
    }

    [DefaultValue(false)]
    public virtual bool RightToLeftLayout
    {
        get => _rightToLeftLayout;
        set
        {
            if (_rightToLeftLayout == value) return;
            _rightToLeftLayout = value;
            Invalidate();
            OnRightToLeftLayoutChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(true)]
    public bool Scrollable
    {
        get => _scrollable;
        set
        {
            if (_scrollable == value) return;
            _scrollable = value;
            if (!value) { _scrollX = 0; _scrollY = 0; }
            UpdateStyles();
            Invalidate();
        }
    }

    [DefaultValue(typeof(Size), "0, 0")]
    public Size TileSize
    {
        get => _tileSize;
        set
        {
            if (value.Width < 0 || value.Height < 0) throw new ArgumentOutOfRangeException(nameof(value));
            _tileSize = value;
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool UseCompatibleStateImageBehavior
    {
        get => _useCompatibleStateImageBehavior;
        set { if (_useCompatibleStateImageBehavior == value) return; _useCompatibleStateImageBehavior = value; Invalidate(); }
    }

    public event EventHandler<ListViewGroupEventArgs>? GroupCollapsedStateChanged
    {
        add => Events.AddHandler(s_groupCollapsedStateChangedEvent, value);
        remove => Events.RemoveHandler(s_groupCollapsedStateChangedEvent, value);
    }

    public event EventHandler<ListViewGroupEventArgs>? GroupTaskLinkClick
    {
        add => Events.AddHandler(s_groupTaskLinkClickEvent, value);
        remove => Events.RemoveHandler(s_groupTaskLinkClickEvent, value);
    }

    public event ItemDragEventHandler? ItemDrag
    {
        add => Events.AddHandler(s_listItemDragEvent, value);
        remove => Events.RemoveHandler(s_listItemDragEvent, value);
    }

    public event EventHandler? RightToLeftLayoutChanged
    {
        add => Events.AddHandler(s_listRightToLeftLayoutChangedEvent, value);
        remove => Events.RemoveHandler(s_listRightToLeftLayoutChangedEvent, value);
    }

    protected virtual void OnGroupCollapsedStateChanged(ListViewGroupEventArgs e) =>
        ((EventHandler<ListViewGroupEventArgs>?)Events[s_groupCollapsedStateChangedEvent])?.Invoke(this, e);

    protected virtual void OnGroupTaskLinkClick(ListViewGroupEventArgs e) =>
        ((EventHandler<ListViewGroupEventArgs>?)Events[s_groupTaskLinkClickEvent])?.Invoke(this, e);

    protected virtual void OnItemDrag(ItemDragEventArgs e) =>
        ((ItemDragEventHandler?)Events[s_listItemDragEvent])?.Invoke(this, e);

    protected virtual void OnRightToLeftLayoutChanged(EventArgs e) =>
        ((EventHandler?)Events[s_listRightToLeftLayoutChangedEvent])?.Invoke(this, e);

    protected void RealizeProperties() { UpdateStyles(); Invalidate(); }

    protected void UpdateExtendedStyles() { UpdateStyles(); Invalidate(); }

    protected override void WndProc(ref Message m) => base.WndProc(ref m);

    internal void GroupCollapsedStateChangedCore(ListViewGroup group)
    {
        Invalidate();
        OnGroupCollapsedStateChanged(new ListViewGroupEventArgs(Groups.IndexOf(group)));
    }

    public void ArrangeIcons() => ArrangeIcons(Alignment);

    public void ArrangeIcons(ListViewAlignment value)
    {
        if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ListViewAlignment));
        Alignment = value;
        if (!_autoArrange && _view is View.LargeIcon or View.SmallIcon)
        {
            var cell = IconCellSize(_view == View.LargeIcon);
            var columns = Math.Max(ContentRectangle.Width / Math.Max(cell.Width, 1), 1);
            var rows = Math.Max(ContentRectangle.Height / Math.Max(cell.Height, 1), 1);
            for (var index = 0; index < _items.Count; index++)
            {
                var column = value == ListViewAlignment.Left ? index / rows : index % columns;
                var row = value == ListViewAlignment.Left ? index % rows : index / columns;
                _items[index].Position = new Point(column * cell.Width, row * cell.Height);
            }
        }
        Invalidate();
    }

    public void RedrawItems(int startIndex, int endIndex, bool invalidateOnly)
    {
        if (startIndex < 0 || startIndex >= EffectiveItemCount) throw new ArgumentOutOfRangeException(nameof(startIndex));
        if (endIndex < startIndex || endIndex >= EffectiveItemCount) throw new ArgumentOutOfRangeException(nameof(endIndex));
        for (var index = startIndex; index <= endIndex; index++) Invalidate(GetItemRect(index));
        if (!invalidateOnly) Update();
    }
}
