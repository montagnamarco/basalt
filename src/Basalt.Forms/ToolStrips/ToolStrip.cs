using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>
/// A horizontal (or vertical) strip of <see cref="ToolStripItem"/>s: a
/// toolbar. The strip is the only Control; its items are laid out, painted
/// through a <see cref="ToolStripRenderer"/>, and tracked for the mouse by
/// the strip itself.
/// </summary>
[DefaultProperty("Items")]
[DefaultEvent("ItemClicked")]
public partial class ToolStrip : ScrollableControl
{
    private ToolStripItemCollection? _itemsField;

    private ToolStripRenderer? _renderer;
    private ToolStripRenderMode _renderMode = ToolStripRenderMode.ManagerRenderMode;
    private ToolStripLayoutStyle _layoutStyle;
    private ToolStripGripStyle _gripStyle = ToolStripGripStyle.Visible;
    private Padding _gripMargin;
    private ImageList? _imageList;
    private Size _imageScalingSize = new(16, 16);
    private bool _showItemToolTips = true;

    /// <summary>The item the mouse is currently over, for MouseEnter/MouseLeave and the hover highlight.</summary>
    private ToolStripItem? _mouseOverItem;

    /// <summary>The item a mouse button went down on, until it is released.</summary>
    private ToolStripItem? _mouseDownItem;

    public ToolStrip()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        _gripMargin = DefaultGripMargin;

        TabStop = false;
        Dock = DockStyle.Top;
        AutoSize = true;
        _layoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;

        ToolStripManager.Track(this);
    }

    /// <summary>The manager's renderer changed: a strip that uses it raises RendererChanged and repaints.</summary>
    internal void OnManagerRendererChanged()
    {
        if (_renderMode != ToolStripRenderMode.ManagerRenderMode || IsDisposed) return;

        OnRendererChanged(EventArgs.Empty);
        Invalidate(true);
    }

    public ToolStrip(params ToolStripItem[] items)
        : this()
    {
        Items.AddRange(items);
    }

    protected override Size DefaultSize => new(100, 25);

    protected override Padding DefaultMargin => Padding.Empty;

    protected override Padding DefaultPadding => Padding.Empty;

    protected virtual Padding DefaultGripMargin => new(2);

    protected virtual DockStyle DefaultDock => DockStyle.Top;

    protected virtual bool DefaultShowItemToolTips => true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    [MergableProperty(false)]
    public virtual ToolStripItemCollection Items => _itemsField ??= new ToolStripItemCollection(this, []);

    protected internal virtual ToolStripItemCollection DisplayedItems => Items;

    public override LayoutEngine LayoutEngine => ToolStripLayoutEngine.Instance;

    [AmbientValue(ToolStripLayoutStyle.StackWithOverflow)]
    public ToolStripLayoutStyle LayoutStyle
    {
        get => _layoutStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ToolStripLayoutStyle));
            if (value == _layoutStyle) return;

            _layoutStyle = value;
            OnLayoutStyleChanged(EventArgs.Empty);
            DoLayout(this, nameof(LayoutStyle));
        }
    }

    [Browsable(false)]
    public Orientation Orientation => _layoutStyle == ToolStripLayoutStyle.VerticalStackWithOverflow ? Orientation.Vertical : Orientation.Horizontal;

    public ToolStripGripStyle GripStyle
    {
        get => _gripStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ToolStripGripStyle));
            if (value == _gripStyle) return;

            _gripStyle = value;
            DoLayout(this, nameof(GripStyle));
            Invalidate();
        }
    }

    [Browsable(false)]
    public ToolStripGripDisplayStyle GripDisplayStyle => Orientation == Orientation.Horizontal ? ToolStripGripDisplayStyle.Vertical : ToolStripGripDisplayStyle.Horizontal;

    /// <summary>
    /// Whether items stack top-to-bottom instead of left-to-right. A drop-down
    /// (always a vertical list of items, whatever its LayoutStyle) overrides
    /// this instead of Orientation, which plain ToolStrip computes from
    /// LayoutStyle alone and does not expose as virtual.
    /// </summary>
    internal virtual bool StacksVertically => Orientation == Orientation.Vertical;

    public Padding GripMargin
    {
        get => _gripMargin;
        set
        {
            if (value == _gripMargin) return;

            _gripMargin = value;
            DoLayout(this, nameof(GripMargin));
            Invalidate();
        }
    }

    /// <summary>The space the grip occupies at the strip's leading edge; empty when the grip is hidden.</summary>
    [Browsable(false)]
    public Rectangle GripRectangle
    {
        get
        {
            if (_gripStyle == ToolStripGripStyle.Hidden) return Rectangle.Empty;

            var display = DisplayRectangle;
            const int thickness = GripThickness;

            return Orientation == Orientation.Horizontal
                ? new Rectangle(display.Left + _gripMargin.Left, display.Top, thickness, display.Height)
                : new Rectangle(display.Left, display.Top + _gripMargin.Top, display.Width, thickness);
        }
    }

    private const int GripThickness = 3;

    [DefaultValue(null)]
    public ImageList? ImageList
    {
        get => _imageList;
        set
        {
            if (value == _imageList) return;

            _imageList = value;
            Invalidate();
        }
    }

    public Size ImageScalingSize
    {
        get => _imageScalingSize;
        set
        {
            if (value == _imageScalingSize) return;

            _imageScalingSize = value;
            DoLayout(this, nameof(ImageScalingSize));
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool ShowItemToolTips
    {
        get => _showItemToolTips;
        set => _showItemToolTips = value;
    }

    /// <summary>
    /// The renderer that paints this strip: its own, else the mode asks for a
    /// stock one, else <see cref="ToolStripManager.Renderer"/>.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ToolStripRenderer Renderer
    {
        get => _renderMode switch
        {
            ToolStripRenderMode.System => ToolStripSystemRendererInstance,
            ToolStripRenderMode.Professional => ToolStripProfessionalRendererInstance,
            ToolStripRenderMode.Custom => _renderer ?? ToolStripSystemRendererInstance,
            _ => ToolStripManager.Renderer,
        };
        set
        {
            _renderer = value;
            _renderMode = ToolStripRenderMode.Custom;
            OnRendererChanged(EventArgs.Empty);
            Invalidate(true);
        }
    }

    public ToolStripRenderMode RenderMode
    {
        get => _renderMode;
        set
        {
            if (value == ToolStripRenderMode.Custom)
            {
                throw new NotSupportedException("RenderMode.Custom is applied automatically when Renderer is set; set Renderer instead.");
            }

            if (value == _renderMode) return;

            _renderMode = value;
            OnRendererChanged(EventArgs.Empty);
            Invalidate(true);
        }
    }

    private static readonly ToolStripSystemRenderer ToolStripSystemRendererInstance = new();
    private static readonly ToolStripProfessionalRenderer ToolStripProfessionalRendererInstance = new();

    private ToolStripDropDownDirection _defaultDropDownDirection = ToolStripDropDownDirection.BelowRight;

    /// <summary>Which way a drop-down item on this strip opens by default, absent its own DropDownDirection.</summary>
    public virtual ToolStripDropDownDirection DefaultDropDownDirection
    {
        get => _defaultDropDownDirection;
        set => _defaultDropDownDirection = value;
    }

    public event ToolStripItemEventHandler? ItemAdded
    {
        add => Events.AddHandler(s_itemAddedEvent, value);
        remove => Events.RemoveHandler(s_itemAddedEvent, value);
    }

    public event ToolStripItemClickedEventHandler? ItemClicked
    {
        add => Events.AddHandler(s_itemClickedEvent, value);
        remove => Events.RemoveHandler(s_itemClickedEvent, value);
    }

    public event ToolStripItemEventHandler? ItemRemoved
    {
        add => Events.AddHandler(s_itemRemovedEvent, value);
        remove => Events.RemoveHandler(s_itemRemovedEvent, value);
    }

    public event EventHandler? LayoutCompleted
    {
        add => Events.AddHandler(s_layoutCompletedEvent, value);
        remove => Events.RemoveHandler(s_layoutCompletedEvent, value);
    }

    public event EventHandler? LayoutStyleChanged
    {
        add => Events.AddHandler(s_layoutStyleChangedEvent, value);
        remove => Events.RemoveHandler(s_layoutStyleChangedEvent, value);
    }

    public event PaintEventHandler? PaintGrip
    {
        add => Events.AddHandler(s_paintGripEvent, value);
        remove => Events.RemoveHandler(s_paintGripEvent, value);
    }

    public event EventHandler? RendererChanged
    {
        add => Events.AddHandler(s_rendererChangedEvent, value);
        remove => Events.RemoveHandler(s_rendererChangedEvent, value);
    }

    private static readonly object s_itemAddedEvent = new();
    private static readonly object s_itemClickedEvent = new();
    private static readonly object s_itemRemovedEvent = new();
    private static readonly object s_layoutCompletedEvent = new();
    private static readonly object s_layoutStyleChangedEvent = new();
    private static readonly object s_paintGripEvent = new();
    private static readonly object s_rendererChangedEvent = new();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The overflow drop-down only borrows its items: the strip that owns them disposes them.
            if (!Items.IsReadOnly)
            {
                for (var i = Items.Count - 1; i >= 0; i--) Items[i].Dispose();
            }
            _overflowButton?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>The default kind of item <see cref="ToolStripItemCollection.Add(string?)"/> and friends create.</summary>
    protected internal virtual ToolStripItem CreateDefaultItem(string? text, Image? image, EventHandler? onClick) =>
        new ToolStripButton(text, image, onClick);

    public ToolStripItem GetItemAt(int x, int y) => GetItemAt(new Point(x, y));

    public ToolStripItem GetItemAt(Point point) => GetItemAtInternal(point)!;

    private ToolStripItem? GetItemAtInternal(Point point)
    {
        if (_overflowActive && OverflowButton.Bounds.Contains(point)) return OverflowButton;

        foreach (ToolStripItem item in DisplayedItems)
        {
            if (IsShownHere(item) && item.Bounds.Contains(point)) return item;
        }

        return null;
    }

    /// <summary>The next item in the given arrow direction, wrapping at the ends: minimal keyboard navigation between items.</summary>
    public virtual ToolStripItem GetNextItem(ToolStripItem? start, ArrowDirection direction)
    {
        var displayed = DisplayedItems;
        var count = displayed.Count;
        if (count == 0) return null!;

        var forward = direction is ArrowDirection.Right or ArrowDirection.Down;
        var index = start is null ? -1 : displayed.IndexOf(start);

        for (var step = 1; step <= count; step++)
        {
            index = forward ? (index + 1) % count : (index - 1 + count) % count;
            var candidate = displayed[index];
            if (IsShownHere(candidate) && candidate.CanSelect) return candidate;
        }

        return null!;
    }

    /// <summary>Lets code outside the Items collection (the designer, a merge) transfer an item here.</summary>
    protected internal static void SetItemParent(ToolStripItem item, ToolStrip parent)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(parent);

        parent.Items.Add(item);
    }

    protected internal void SetItemLocation(ToolStripItem item, Point location) =>
        ((IArrangedElement)item).SetBounds(new Rectangle(location, item.Size), BoundsSpecified.None);

    protected internal virtual void OnItemAdded(ToolStripItemEventArgs e)
    {
        DoLayout(((IArrangedElement?)e.Item) ?? this, "Items");
        ((ToolStripItemEventHandler?)Events[s_itemAddedEvent])?.Invoke(this, e);
    }

    protected internal virtual void OnItemClicked(ToolStripItemClickedEventArgs e) =>
        ((ToolStripItemClickedEventHandler?)Events[s_itemClickedEvent])?.Invoke(this, e);

    protected internal virtual void OnItemRemoved(ToolStripItemEventArgs e)
    {
        DoLayout(this, "Items");
        ((ToolStripItemEventHandler?)Events[s_itemRemovedEvent])?.Invoke(this, e);
    }

    protected virtual void OnLayoutCompleted(EventArgs e) => ((EventHandler?)Events[s_layoutCompletedEvent])?.Invoke(this, e);

    protected virtual void OnLayoutStyleChanged(EventArgs e) => ((EventHandler?)Events[s_layoutStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnRendererChanged(EventArgs e) => ((EventHandler?)Events[s_rendererChangedEvent])?.Invoke(this, e);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        OnLayoutCompleted(EventArgs.Empty);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        foreach (ToolStripItem item in Items) item.RaiseOwnerFontChanged(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mea)
    {
        base.OnMouseDown(mea);

        var item = GetItemAtInternal(mea.Location);
        if (item is not { Enabled: true, CanSelect: true }) return;

        _mouseDownItem = item;
        item.SetPressedInternal(true);
        item.RaiseMouseDown(mea);
    }

    protected override void OnMouseMove(MouseEventArgs mea)
    {
        base.OnMouseMove(mea);

        var item = GetItemAtInternal(mea.Location);

        if (item != _mouseOverItem)
        {
            if (_mouseOverItem is { } leaving)
            {
                leaving.SetSelectedInternal(false);
                leaving.RaiseMouseLeave();
            }

            _mouseOverItem = item;

            if (item is { Enabled: true, CanSelect: true })
            {
                item.SetSelectedInternal(true);
                item.RaiseMouseEnter();
            }
        }

        item?.RaiseMouseMove(mea);

        // Dragging the button out of its own area lifts it, back in presses it again, as a plain Control button does.
        _mouseDownItem?.SetPressedInternal(_mouseDownItem == item);
    }

    protected override void OnMouseUp(MouseEventArgs mea)
    {
        base.OnMouseUp(mea);

        var item = GetItemAtInternal(mea.Location);
        item?.RaiseMouseUp(mea);

        if (_mouseDownItem is not { } pressedItem)
        {
            return;
        }

        pressedItem.SetPressedInternal(false);
        _mouseDownItem = null;

        if (pressedItem == item && pressedItem.Enabled)
        {
            pressedItem.PerformClick();
            OnItemClicked(new ToolStripItemClickedEventArgs(pressedItem));
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_mouseOverItem is not { } leaving) return;

        leaving.SetSelectedInternal(false);
        leaving.RaiseMouseLeave();
        _mouseOverItem = null;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);

        if (Capture || _mouseDownItem is not { } pressed) return;

        pressed.SetPressedInternal(false);
        _mouseDownItem = null;
    }

    /// <summary>A character that is one of an item's mnemonics clicks that item, as Alt+letter does for a menu.</summary>
    protected internal override bool ProcessMnemonic(char charCode)
    {
        foreach (ToolStripItem item in Items)
        {
            if (!item.Available || !item.Enabled || !Control.IsMnemonic(charCode, item.Text)) continue;

            item.PerformClick();
            OnItemClicked(new ToolStripItemClickedEventArgs(item));

            return true;
        }

        return base.ProcessMnemonic(charCode);
    }

    /// <summary>
    /// Whether this strip wants a dialog key (an arrow, Enter, Escape, Space,
    /// Tab) for its own keyboard navigation, routed to <see cref="ProcessNavigationKey"/>.
    /// A drop-down (always) and a menu bar (once a key has opened one of its
    /// items) claim keys this way; a plain toolbar claims none.
    /// </summary>
    internal virtual bool WantsNavigationKey(Keys keyData) => false;

    /// <summary>Handles a key <see cref="WantsNavigationKey"/> claimed; true when it did something with it.</summary>
    internal virtual bool ProcessNavigationKey(Keys keyData) => false;

    /// <summary>
    /// Arrows, Enter, Escape, Space and Tab reach here (not OnKeyDown) because
    /// the base IsInputKey (unchanged: DialogCodeWantsKey is false for a
    /// ToolStrip) already answers false for them, the way it must for
    /// ProcessDialogKey to run at all.
    /// </summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (WantsNavigationKey(keyData) && ProcessNavigationKey(keyData)) return true;

        return base.ProcessDialogKey(keyData);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // The renderer paints the whole background in OnPaint; Control's plain fill would show through the gaps between items.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var renderer = Renderer;

        renderer.DrawToolStripBackground(new ToolStripRenderEventArgs(e.Graphics, this, ClientRectangle, BackColor));
        PaintLeadingMargin(e);
        OnPaintGrip(e);

        foreach (ToolStripItem item in DisplayedItems)
        {
            if (!IsShownHere(item) || item.Bounds.Width <= 0 || item.Bounds.Height <= 0) continue;

            item.PaintInternal(new PaintEventArgs(e.Graphics, item.Bounds));
        }

        if (_overflowActive) OverflowButton.PaintInternal(new PaintEventArgs(e.Graphics, OverflowButton.Bounds));

        renderer.DrawToolStripBorder(new ToolStripRenderEventArgs(e.Graphics, this, ClientRectangle, BackColor));
        PaintTrailingDecoration(e);

        base.OnPaint(e);
    }

    /// <summary>
    /// A band a subclass reserves on its leading edge, painted after the
    /// ordinary background fill and before the grip and items: a drop-down
    /// menu's image margin.
    /// </summary>
    internal virtual void PaintLeadingMargin(PaintEventArgs e)
    {
    }

    /// <summary>Something a subclass paints after everything else, on top: a status bar's sizing grip.</summary>
    internal virtual void PaintTrailingDecoration(PaintEventArgs e)
    {
    }

    protected virtual void OnPaintGrip(PaintEventArgs e)
    {
        if (_gripStyle == ToolStripGripStyle.Visible)
        {
            Renderer.DrawGrip(new ToolStripGripRenderEventArgs(e.Graphics, this));
        }

        ((PaintEventHandler?)Events[s_paintGripEvent])?.Invoke(this, e);
    }

    /// <summary>The strip's own preferred size: wide/tall enough for every available item, in a single row or column.</summary>
    internal override Size GetPreferredSizeCore(Size proposedSize)
    {
        var padding = Padding;

        if (StacksVertically)
        {
            var width = 0;
            var height = padding.Vertical + LeadingGripSpace();

            foreach (ToolStripItem item in DisplayedItems)
            {
                if (!item.Available) continue;

                var size = MeasureItem(item);
                width = Math.Max(width, size.Width);
                height += size.Height + item.Margin.Vertical;
            }

            return new Size(width + padding.Horizontal, height == padding.Vertical ? DefaultSize.Height : height);
        }

        var totalWidth = padding.Horizontal + LeadingGripSpace();
        var tallest = 0;

        foreach (ToolStripItem item in DisplayedItems)
        {
            if (!item.Available) continue;

            var size = MeasureItem(item);
            tallest = Math.Max(tallest, size.Height);
            totalWidth += size.Width + item.Margin.Horizontal;
        }

        return new Size(totalWidth, tallest == 0 ? DefaultSize.Height : tallest + padding.Vertical);
    }

    private int LeadingGripSpace() => _gripStyle == ToolStripGripStyle.Visible ? _gripMargin.Left + GripThickness + _gripMargin.Right : 0;

    private static Size MeasureItem(ToolStripItem item) => item.AutoSize ? item.GetPreferredSize(Size.Empty) : item.Size;

    /// <summary>Arranges every item this strip shows in one row (or column): first settles the overflow, then places what stays.</summary>
    internal void PerformItemLayout()
    {
        ApplyOverflow();

        if (StacksVertically) LayoutVertical();
        else LayoutHorizontal();
    }

    private void LayoutHorizontal()
    {
        var display = DisplayRectangle;
        var leading = display.Left + LeadingGripSpace();
        var trailing = _overflowActive ? display.Right - OverflowButtonThickness : display.Right;

        var items = DisplayedItems;
        foreach (ToolStripItem item in items)
        {
            if (!item.Available) Place(item, Rectangle.Empty);
        }

        foreach (ToolStripItem item in items)
        {
            if (!LaysOut(item) || item.Alignment == ToolStripItemAlignment.Right) continue;

            var size = MeasureItem(item);
            var margin = item.Margin;
            leading += margin.Left;
            var y = display.Top + Math.Max((display.Height - size.Height) / 2, 0);
            PlaceShown(item, new Rectangle(leading, y, size.Width, size.Height));
            leading += size.Width + margin.Right;
        }

        for (var i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (!LaysOut(item) || item.Alignment != ToolStripItemAlignment.Right) continue;

            var size = MeasureItem(item);
            var margin = item.Margin;
            trailing -= margin.Right + size.Width;
            var y = display.Top + Math.Max((display.Height - size.Height) / 2, 0);
            PlaceShown(item, new Rectangle(trailing, y, size.Width, size.Height));
            trailing -= margin.Left;
        }

        PlaceOverflowButton(display, horizontal: true);
    }

    private void LayoutVertical()
    {
        var display = DisplayRectangle;
        var leading = display.Top + LeadingGripSpace();
        var trailing = _overflowActive ? display.Bottom - OverflowButtonThickness : display.Bottom;

        var items = DisplayedItems;
        foreach (ToolStripItem item in items)
        {
            if (!item.Available) Place(item, Rectangle.Empty);
        }

        foreach (ToolStripItem item in items)
        {
            if (!LaysOut(item) || item.Alignment == ToolStripItemAlignment.Right) continue;

            var size = MeasureItem(item);
            var margin = item.Margin;
            leading += margin.Top;
            var x = display.Left + Math.Max((display.Width - size.Width) / 2, 0);
            PlaceShown(item, new Rectangle(x, leading, size.Width, size.Height));
            leading += size.Height + margin.Bottom;
        }

        for (var i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (!LaysOut(item) || item.Alignment != ToolStripItemAlignment.Right) continue;

            var size = MeasureItem(item);
            var margin = item.Margin;
            trailing -= margin.Bottom + size.Height;
            var x = display.Left + Math.Max((display.Width - size.Width) / 2, 0);
            PlaceShown(item, new Rectangle(x, trailing, size.Width, size.Height));
            trailing -= margin.Top;
        }

        PlaceOverflowButton(display, horizontal: false);
    }

    /// <summary>Places an item this strip shows, taking it back from the overflow drop-down if it was lent there.</summary>
    private void PlaceShown(ToolStripItem item, Rectangle bounds)
    {
        item.SetDisplayParentInternal(this);
        Place(item, bounds);
    }

    private static void Place(ToolStripItem item, Rectangle bounds) => ((IArrangedElement)item).SetBounds(bounds, BoundsSpecified.None);

    public override string ToString() => $"{base.ToString()}, Items.Count: {Items.Count}";
}

/// <summary>
/// The strip's own arrangement of its items: unlike <see cref="DefaultLayout"/>,
/// it does not anchor or dock Controls, because the things it arranges are
/// not Controls.
/// </summary>
internal sealed class ToolStripLayoutEngine : LayoutEngine
{
    public static readonly ToolStripLayoutEngine Instance = new();

    public override void InitLayout(object child, BoundsSpecified specified)
    {
        // Items are not measured ahead of being placed: nothing to remember here.
    }

    public override bool Layout(object container, LayoutEventArgs layoutEventArgs)
    {
        if (container is not ToolStrip strip) throw new NotSupportedException($"ToolStripLayoutEngine cannot lay out {container?.GetType()}.");

        if (strip.AutoSize)
        {
            var preferred = strip.GetPreferredSize(Size.Empty);
            if (preferred != strip.Size) strip.Size = preferred;
        }

        strip.PerformItemLayout();

        return false;
    }
}
