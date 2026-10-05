using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms;

public partial class ListView
{
    private ImageList? _stateImageList;
    private bool _hotTracking;
    private bool _hoverSelection;
    private bool _showItemToolTips = true;
    private ListViewItem? _hoverItem;
    private int _hoverIndex = -1;
    private Point _hoverPoint;
    private ToolTip? _itemToolTip;

    [DefaultValue(null)]
    public ImageList? StateImageList
    {
        get => _stateImageList;
        set
        {
            if (ReferenceEquals(value, _stateImageList)) return;
            if (_stateImageList is not null)
            {
                _stateImageList.RecreateHandle -= OnStateImageListChanged;
                _stateImageList.Disposed -= OnStateImageListDisposed;
            }
            _stateImageList = value;
            if (value is not null)
            {
                value.RecreateHandle += OnStateImageListChanged;
                value.Disposed += OnStateImageListDisposed;
            }
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool HotTracking
    {
        get => _hotTracking;
        set
        {
            if (_hotTracking == value) return;
            _hotTracking = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool HoverSelection
    {
        get => _hoverSelection;
        set => _hoverSelection = value;
    }

    [DefaultValue(true)]
    public bool ShowItemToolTips
    {
        get => _showItemToolTips;
        set
        {
            _showItemToolTips = value;
            if (!value) _itemToolTip?.Hide(this);
        }
    }

    public event ListViewItemMouseHoverEventHandler? ItemMouseHover
    {
        add => Events.AddHandler(s_itemMouseHoverEvent, value);
        remove => Events.RemoveHandler(s_itemMouseHoverEvent, value);
    }

    private static readonly object s_itemMouseHoverEvent = new();

    protected virtual void OnItemMouseHover(ListViewItemMouseHoverEventArgs e) =>
        ((ListViewItemMouseHoverEventHandler?)Events[s_itemMouseHoverEvent])?.Invoke(this, e);

    internal ToolTip ItemToolTip => _itemToolTip ??= new ToolTip();

    private void OnStateImageListChanged(object? sender, EventArgs e) => Invalidate();

    private void OnStateImageListDisposed(object? sender, EventArgs e) => StateImageList = null;

    private int StateGlyphWidth(ListViewItem item)
    {
        if (_stateImageList is not null && (_checkBoxes || item.StateImageIndex >= 0))
            return _stateImageList.ImageSize.Width + 4;
        return _checkBoxes ? CheckBoxSize + 4 : 0;
    }

    private void DrawStateGlyph(Graphics graphics, ListViewItem item, int x, int rowY, int rowHeight)
    {
        var imageIndex = _checkBoxes ? (item.Checked ? 1 : 0) : item.StateImageIndex;
        if (_stateImageList is { } images && imageIndex >= 0 && imageIndex < images.Images.Count)
        {
            images.Draw(graphics, x + 2, rowY + Math.Max((rowHeight - images.ImageSize.Height) / 2, 0), imageIndex);
        }
        else if (_checkBoxes)
        {
            var box = new Rectangle(x + 2, rowY + (rowHeight - CheckBoxSize) / 2, CheckBoxSize, CheckBoxSize);
            if (PortableVisualStylesEnabled)
            {
                PortableVisualStyles.PaintGlyph(graphics, box, CollectionVisualAppearance.Palette(this),
                    radio: false, selected: item.Checked, mixed: false, pressed: false, enabled: Enabled);
            }
            else
            {
                ControlPaint.DrawCheckBox(graphics, box, item.Checked ? ButtonState.Checked : ButtonState.Normal);
            }
        }
    }

    private Font? HotFont(ListViewItem item, Font font, int index = -1) =>
        _hotTracking && (ReferenceEquals(item, _hoverItem) || (_virtualMode && index >= 0 && index == _hoverIndex))
            ? new Font(font, font.Style | FontStyle.Underline)
            : null;

    private void TrackItemPointer(MouseEventArgs e)
    {
        _hoverPoint = e.Location;
        var item = HitTestCore(e.X, e.Y).Item;
        var index = _virtualMode ? VirtualIndexAt(e.Y) : item?.Index ?? -1;
        if (index == _hoverIndex) return;

        _hoverIndex = index;
        _hoverItem = item;
        _itemToolTip?.Hide(this);
        if (_hotTracking) Invalidate();
    }

    protected override void OnMouseHover(EventArgs e)
    {
        base.OnMouseHover(e);
        var item = HitTestCore(_hoverPoint.X, _hoverPoint.Y).Item;
        if (item is null) return;
        _hoverItem = item;
        OnItemMouseHover(new ListViewItemMouseHoverEventArgs(item));

        if (_hoverSelection && _virtualMode)
        {
            var index = VirtualIndexAt(_hoverPoint.Y);
            if (index >= 0 && !IsVirtualIndexSelected(index))
            {
                ClearVirtualSelection();
                SetVirtualItemSelected(index, true);
            }
        }
        else if (_hoverSelection && !item.Selected)
        {
            ClearSelection();
            item.Selected = true;
        }

        if (_showItemToolTips && item.ToolTipText.Length > 0)
            ItemToolTip.Show(item.ToolTipText, this, new Point(_hoverPoint.X + 12, _hoverPoint.Y + 18), 5000);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverItem = null;
        _hoverIndex = -1;
        _itemToolTip?.Hide(this);
        if (_hotTracking) Invalidate();
    }

    private void DisposeStateImagesAndTip()
    {
        StateImageList = null;
        _itemToolTip?.Dispose();
        _itemToolTip = null;
    }
}
