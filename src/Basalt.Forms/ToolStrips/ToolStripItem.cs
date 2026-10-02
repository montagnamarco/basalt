using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>
/// One button, label or separator of a <see cref="ToolStrip"/>: it has
/// bounds and paints itself like a control, but it is a component, not a
/// control, so the strip (not Windows) tracks its mouse and lays it out.
/// </summary>
public abstract class ToolStripItem : BindableComponent, IArrangedElement
{
    private ToolStripItemAlignment _alignment = ToolStripItemAlignment.Left;
    private bool _autoSize = true;
    private bool _available = true;
    private bool _enabled = true;
    private bool _pressed;
    private bool _selected;
    private Rectangle _bounds;
    private Color _backColor;
    private Color _foreColor;
    private Font? _font;
    private Image? _image;
    private int _imageIndex = -1;
    private string _imageKey = string.Empty;
    private ContentAlignment _imageAlign = ContentAlignment.MiddleCenter;
    private ContentAlignment _textAlign = ContentAlignment.MiddleCenter;
    private ToolStripItemDisplayStyle _displayStyle;
    private string? _text;
    private string? _name;
    private Padding _margin;
    private Padding _padding;
    private Size? _specifiedSize;
    private ToolStripItemOverflow _overflow;

    /// <summary>The strip whose Items hold this item; Parent is the one showing it (its overflow drop-down, when it overflowed).</summary>
    private ToolStrip? _owner;

    protected ToolStripItem()
    {
        _displayStyle = DefaultDisplayStyle;
        _overflow = DefaultOverflow;
        _margin = DefaultMargin;
        _padding = DefaultPadding;
        _bounds = new Rectangle(Point.Empty, DefaultSize);
    }

    protected ToolStripItem(string? text, Image? image, EventHandler? onClick)
        : this()
    {
        _text = text;
        _image = image;
        if (onClick is not null) Click += onClick;
    }

    protected ToolStripItem(string? text, Image? image, EventHandler? onClick, string? name)
        : this(text, image, onClick)
    {
        Name = name;
    }

    [DefaultValue(ToolStripItemAlignment.Left)]
    public ToolStripItemAlignment Alignment
    {
        get => _alignment;
        set
        {
            if (value == _alignment) return;

            _alignment = value;
            Owner?.DoLayout(this, nameof(Alignment));
        }
    }

    [DefaultValue(true)]
    public bool AutoSize
    {
        get => _autoSize;
        set
        {
            if (value == _autoSize) return;

            _autoSize = value;
            Owner?.DoLayout(this, nameof(AutoSize));
        }
    }

    /// <summary>
    /// Whether the item is placed on its strip, regardless of whether the
    /// strip itself is shown; <see cref="Visible"/> adds that condition.
    /// </summary>
    [Browsable(false)]
    public bool Available
    {
        get => _available;
        set => SetVisibleCore(value);
    }

    public virtual Color BackColor
    {
        get => !_backColor.IsEmpty ? _backColor : Owner?.BackColor ?? Control.DefaultBackColor;
        set
        {
            if (value == _backColor) return;

            _backColor = value;
            OnBackColorChanged(EventArgs.Empty);
        }
    }

    [Browsable(false)]
    public virtual Rectangle Bounds => _bounds;

    [Browsable(false)]
    public virtual bool CanSelect => true;

    /// <summary>The area inside the padding, where the image and text are drawn.</summary>
    [Browsable(false)]
    public Rectangle ContentRectangle
    {
        get
        {
            var padding = Padding;

            return new Rectangle(
                padding.Left,
                padding.Top,
                Math.Max(_bounds.Width - padding.Horizontal, 0),
                Math.Max(_bounds.Height - padding.Vertical, 0));
        }
    }

    protected virtual bool DefaultAutoToolTip => false;

    protected virtual ToolStripItemDisplayStyle DefaultDisplayStyle => ToolStripItemDisplayStyle.ImageAndText;

    protected virtual Padding DefaultMargin => new(0, 1, 0, 2);

    protected virtual Padding DefaultPadding => Padding.Empty;

    protected virtual Size DefaultSize => new(23, 23);

    public virtual ToolStripItemDisplayStyle DisplayStyle
    {
        get => _displayStyle;
        set
        {
            if (value == _displayStyle) return;

            _displayStyle = value;
            OnDisplayStyleChanged(EventArgs.Empty);
        }
    }

    public virtual bool Enabled
    {
        get => _enabled;
        set
        {
            if (value == _enabled) return;

            _enabled = value;
            OnEnabledChanged(EventArgs.Empty);
        }
    }

    public virtual Font Font
    {
        get => _font ?? Owner?.Font ?? Control.DefaultFont;
        set
        {
            if (Equals(value, _font)) return;

            _font = value;
            OnFontChanged(EventArgs.Empty);
        }
    }

    public virtual Color ForeColor
    {
        get => !_foreColor.IsEmpty ? _foreColor : Owner?.ForeColor ?? Control.DefaultForeColor;
        set
        {
            if (value == _foreColor) return;

            _foreColor = value;
            OnForeColorChanged(EventArgs.Empty);
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    public int Height
    {
        get => Size.Height;
        set => Size = new Size(Size.Width, value);
    }

    public virtual Image? Image
    {
        get => _image;
        set
        {
            if (value == _image) return;

            _image = value;
            Invalidate();
        }
    }

    [DefaultValue(ContentAlignment.MiddleCenter)]
    public ContentAlignment ImageAlign
    {
        get => _imageAlign;
        set
        {
            if (value == _imageAlign) return;

            _imageAlign = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    public int ImageIndex
    {
        get => _imageIndex;
        set
        {
            if (value == _imageIndex) return;

            _imageIndex = value;
            _imageKey = string.Empty;
            Invalidate();
        }
    }

    [Browsable(false)]
    public string ImageKey
    {
        get => _imageKey;
        set
        {
            value ??= string.Empty;
            if (value == _imageKey) return;

            _imageKey = value;
            _imageIndex = -1;
            Invalidate();
        }
    }

    [Browsable(false)]
    public bool IsDisposed { get; private set; }

    /// <summary>Whether this item is currently shown on its owner's <see cref="ToolStrip.OverflowButton"/> drop-down rather than on the strip itself.</summary>
    [Browsable(false)]
    public bool IsOnOverflow => Placement == ToolStripItemPlacement.Overflow;

    public Padding Margin
    {
        get => _margin;
        set
        {
            if (value == _margin) return;

            _margin = value;
            Owner?.DoLayout(this, nameof(Margin));
        }
    }

    [Browsable(false)]
    [DefaultValue(null)]
    public string? Name
    {
        get => _name;
        set => _name = value;
    }

    /// <summary>
    /// Whether a <see cref="ToolStrip"/> too narrow (or short) to show every
    /// item may move this one to its <see cref="ToolStrip.OverflowButton"/>
    /// drop-down instead of leaving it on the strip. AsNeeded is the default;
    /// Never keeps it on the strip no matter what, Always keeps it off.
    /// </summary>
    /// <summary>The Overflow an item starts with: a menu item never overflows.</summary>
    internal virtual ToolStripItemOverflow DefaultOverflow => ToolStripItemOverflow.AsNeeded;

    [DefaultValue(ToolStripItemOverflow.AsNeeded)]
    public ToolStripItemOverflow Overflow
    {
        get => _overflow;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ToolStripItemOverflow));
            if (value == _overflow) return;

            _overflow = value;
            Owner?.DoLayout(this, nameof(Overflow));
        }
    }

    [Browsable(false)]
    public ToolStrip? Owner
    {
        get => _owner;
        set
        {
            if (value == _owner) return;

            // Items.Add/Remove are what really move the item: they keep the
            // collection and the owner in step, the way setting Owner does in WinForms.
            _owner?.Items.Remove(this);
            value?.Items.Add(this);
        }
    }

    [Browsable(false)]
    protected ToolStrip? Parent { get; set; }

    /// <summary>Where the strip's last layout put this item: on the strip itself, on its overflow drop-down, or nowhere (not available).</summary>
    [Browsable(false)]
    public ToolStripItemPlacement Placement { get; private set; } = ToolStripItemPlacement.None;

    public virtual Padding Padding
    {
        get => _padding;
        set
        {
            if (value == _padding) return;

            _padding = value;
            Owner?.DoLayout(this, nameof(Padding));
        }
    }

    [Browsable(false)]
    public virtual bool Pressed => _pressed;

    [Browsable(false)]
    public virtual bool Selected => _selected;

    public virtual Size Size
    {
        get => _bounds.Size;
        set
        {
            _specifiedSize = value;
            SetBounds(new Rectangle(_bounds.Location, value));
        }
    }

    [DefaultValue(null)]
    public object? Tag { get; set; }

    [DefaultValue("")]
    public virtual string? Text
    {
        get => _text ?? string.Empty;
        set
        {
            value ??= string.Empty;
            if (value == _text) return;

            _text = value;
            OnTextChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(ContentAlignment.MiddleCenter)]
    public virtual ContentAlignment TextAlign
    {
        get => _textAlign;
        set
        {
            if (value == _textAlign) return;

            _textAlign = value;
            Invalidate();
        }
    }

    public string? ToolTipText { get; set; }

    /// <summary>
    /// Whether the item is on screen: placed (<see cref="Available"/>) on a
    /// strip that is itself visible. Setting it is the same as setting Available.
    /// </summary>
    public bool Visible
    {
        get => Parent is { Visible: true } && _available;
        set => SetVisibleCore(value);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    public int Width
    {
        get => Size.Width;
        set => Size = new Size(value, Size.Height);
    }

    public event EventHandler? AvailableChanged
    {
        add => Events.AddHandler(s_availableEvent, value);
        remove => Events.RemoveHandler(s_availableEvent, value);
    }

    public event EventHandler? BackColorChanged
    {
        add => Events.AddHandler(s_backColorEvent, value);
        remove => Events.RemoveHandler(s_backColorEvent, value);
    }

    public event EventHandler? Click
    {
        add => Events.AddHandler(s_clickEvent, value);
        remove => Events.RemoveHandler(s_clickEvent, value);
    }

    public event EventHandler? DisplayStyleChanged
    {
        add => Events.AddHandler(s_displayStyleEvent, value);
        remove => Events.RemoveHandler(s_displayStyleEvent, value);
    }

    public event EventHandler? DoubleClick
    {
        add => Events.AddHandler(s_doubleClickEvent, value);
        remove => Events.RemoveHandler(s_doubleClickEvent, value);
    }

    public event EventHandler? EnabledChanged
    {
        add => Events.AddHandler(s_enabledEvent, value);
        remove => Events.RemoveHandler(s_enabledEvent, value);
    }

    public event EventHandler? ForeColorChanged
    {
        add => Events.AddHandler(s_foreColorEvent, value);
        remove => Events.RemoveHandler(s_foreColorEvent, value);
    }

    public event MouseEventHandler? MouseDown
    {
        add => Events.AddHandler(s_mouseDownEvent, value);
        remove => Events.RemoveHandler(s_mouseDownEvent, value);
    }

    public event EventHandler? MouseEnter
    {
        add => Events.AddHandler(s_mouseEnterEvent, value);
        remove => Events.RemoveHandler(s_mouseEnterEvent, value);
    }

    public event EventHandler? MouseHover
    {
        add => Events.AddHandler(s_mouseHoverEvent, value);
        remove => Events.RemoveHandler(s_mouseHoverEvent, value);
    }

    public event EventHandler? MouseLeave
    {
        add => Events.AddHandler(s_mouseLeaveEvent, value);
        remove => Events.RemoveHandler(s_mouseLeaveEvent, value);
    }

    public event MouseEventHandler? MouseMove
    {
        add => Events.AddHandler(s_mouseMoveEvent, value);
        remove => Events.RemoveHandler(s_mouseMoveEvent, value);
    }

    public event MouseEventHandler? MouseUp
    {
        add => Events.AddHandler(s_mouseUpEvent, value);
        remove => Events.RemoveHandler(s_mouseUpEvent, value);
    }

    public event EventHandler? OwnerChanged
    {
        add => Events.AddHandler(s_ownerEvent, value);
        remove => Events.RemoveHandler(s_ownerEvent, value);
    }

    public event PaintEventHandler? Paint
    {
        add => Events.AddHandler(s_paintEvent, value);
        remove => Events.RemoveHandler(s_paintEvent, value);
    }

    public event EventHandler? SelectedChanged
    {
        add => Events.AddHandler(s_selectedEvent, value);
        remove => Events.RemoveHandler(s_selectedEvent, value);
    }

    public event EventHandler? TextChanged
    {
        add => Events.AddHandler(s_textEvent, value);
        remove => Events.RemoveHandler(s_textEvent, value);
    }

    public event EventHandler? VisibleChanged
    {
        add => Events.AddHandler(s_visibleEvent, value);
        remove => Events.RemoveHandler(s_visibleEvent, value);
    }

    private static readonly object s_availableEvent = new();
    private static readonly object s_backColorEvent = new();
    private static readonly object s_clickEvent = new();
    private static readonly object s_displayStyleEvent = new();
    private static readonly object s_doubleClickEvent = new();
    private static readonly object s_enabledEvent = new();
    private static readonly object s_foreColorEvent = new();
    private static readonly object s_mouseDownEvent = new();
    private static readonly object s_mouseEnterEvent = new();
    private static readonly object s_mouseHoverEvent = new();
    private static readonly object s_mouseLeaveEvent = new();
    private static readonly object s_mouseMoveEvent = new();
    private static readonly object s_mouseUpEvent = new();
    private static readonly object s_ownerEvent = new();
    private static readonly object s_paintEvent = new();
    private static readonly object s_selectedEvent = new();
    private static readonly object s_textEvent = new();
    private static readonly object s_visibleEvent = new();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            IsDisposed = true;
            _owner?.Items.Remove(this);
        }

        base.Dispose(disposing);
    }

    /// <summary>The strip showing the item, for code that places or invalidates it there; null while it is on no strip.</summary>
    internal ToolStrip? ParentInternal => Parent;

    /// <summary>The strip currently showing the item: its owner, or the owner's overflow drop-down.</summary>
    public ToolStrip GetCurrentParent() => Parent!;

    /// <summary>The size the item would like: the glyph plus a gap plus the measured text, within <see cref="DisplayStyle"/>.</summary>
    public virtual Size GetPreferredSize(Size constrainingSize)
    {
        if (_specifiedSize is { } specified && !AutoSize) return specified;

        var showText = (_displayStyle & ToolStripItemDisplayStyle.Text) != 0 && !string.IsNullOrEmpty(Text);
        var showImage = (_displayStyle & ToolStripItemDisplayStyle.Image) != 0 && ImageToDraw is not null;

        var textSize = showText ? TextRenderer.MeasureText(Text, Font) : Size.Empty;
        var imageSize = showImage ? ImageToDraw!.Size : Size.Empty;

        var gap = showText && showImage ? 2 : 0;
        var width = textSize.Width + imageSize.Width + gap + Padding.Horizontal;
        var height = Math.Max(textSize.Height, imageSize.Height) + Padding.Vertical;

        // A toolbar button is never narrower or shorter than its default size.
        var defaultSize = DefaultSize;

        return new Size(Math.Max(width, showText || showImage ? width : defaultSize.Width), Math.Max(height, defaultSize.Height));
    }

    /// <summary>The image to paint: the explicit Image, else one looked up in the owner's ImageList.</summary>
    internal Image? ImageToDraw
    {
        get
        {
            if (_image is not null) return _image;

            var list = Owner?.ImageList;
            if (list is null) return null;

            if (_imageIndex >= 0 && _imageIndex < list.Images.Count) return list.Images[_imageIndex];
            if (!string.IsNullOrEmpty(_imageKey)) return list.Images[_imageKey];

            return null;
        }
    }

    public void Invalidate() => Parent?.Invalidate(_bounds, true);

    public void Invalidate(Rectangle r)
    {
        var offset = r;
        offset.Offset(_bounds.Location);
        Parent?.Invalidate(offset, true);
    }

    protected virtual void OnAvailableChanged(EventArgs e) => ((EventHandler?)Events[s_availableEvent])?.Invoke(this, e);

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnBackColorChanged(EventArgs e)
    {
        Invalidate();
        ((EventHandler?)Events[s_backColorEvent])?.Invoke(this, e);
    }

    protected virtual void OnBoundsChanged()
    {
    }

    protected virtual void OnClick(EventArgs e) => ((EventHandler?)Events[s_clickEvent])?.Invoke(this, e);

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnDisplayStyleChanged(EventArgs e)
    {
        Invalidate();
        Owner?.DoLayout(this, nameof(DisplayStyle));
        ((EventHandler?)Events[s_displayStyleEvent])?.Invoke(this, e);
    }

    protected virtual void OnDoubleClick(EventArgs e) => ((EventHandler?)Events[s_doubleClickEvent])?.Invoke(this, e);

    protected virtual void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        ((EventHandler?)Events[s_enabledEvent])?.Invoke(this, e);
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnFontChanged(EventArgs e)
    {
        Owner?.DoLayout(this, nameof(Font));
        Invalidate();
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnForeColorChanged(EventArgs e)
    {
        Invalidate();
        ((EventHandler?)Events[s_foreColorEvent])?.Invoke(this, e);
    }

    protected virtual void OnMouseDown(MouseEventArgs e) => ((MouseEventHandler?)Events[s_mouseDownEvent])?.Invoke(this, e);

    protected virtual void OnMouseEnter(EventArgs e) => ((EventHandler?)Events[s_mouseEnterEvent])?.Invoke(this, e);

    protected virtual void OnMouseHover(EventArgs e) => ((EventHandler?)Events[s_mouseHoverEvent])?.Invoke(this, e);

    protected virtual void OnMouseLeave(EventArgs e) => ((EventHandler?)Events[s_mouseLeaveEvent])?.Invoke(this, e);

    protected virtual void OnMouseMove(MouseEventArgs mea) => ((MouseEventHandler?)Events[s_mouseMoveEvent])?.Invoke(this, mea);

    protected virtual void OnMouseUp(MouseEventArgs e) => ((MouseEventHandler?)Events[s_mouseUpEvent])?.Invoke(this, e);

    protected virtual void OnOwnerChanged(EventArgs e) => ((EventHandler?)Events[s_ownerEvent])?.Invoke(this, e);

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnOwnerFontChanged(EventArgs e)
    {
        if (_font is null) OnFontChanged(e);
    }

    protected virtual void OnPaint(PaintEventArgs e) => ((PaintEventHandler?)Events[s_paintEvent])?.Invoke(this, e);

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnParentBackColorChanged(EventArgs e)
    {
        if (_backColor.IsEmpty) OnBackColorChanged(e);
    }

    protected virtual void OnParentChanged(ToolStrip? oldParent, ToolStrip? newParent)
    {
    }

    protected virtual void OnParentEnabledChanged(EventArgs e)
    {
        if (_enabled) OnEnabledChanged(e);
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnParentForeColorChanged(EventArgs e)
    {
        if (_foreColor.IsEmpty) OnForeColorChanged(e);
    }

    protected virtual void OnSelectedChanged(EventArgs e) => ((EventHandler?)Events[s_selectedEvent])?.Invoke(this, e);

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnTextChanged(EventArgs e)
    {
        Owner?.DoLayout(this, nameof(Text));
        Invalidate();
        ((EventHandler?)Events[s_textEvent])?.Invoke(this, e);
    }

    protected virtual void OnVisibleChanged(EventArgs e) => ((EventHandler?)Events[s_visibleEvent])?.Invoke(this, e);

    public virtual void ResetBackColor() => BackColor = Color.Empty;

    public virtual void ResetForeColor() => ForeColor = Color.Empty;

    // Keyboard processing an item can take part in; ToolStripControlHost forwards it to its control.

    protected internal virtual bool ProcessCmdKey(ref Message m, Keys keyData) => false;

    /// <summary>Enter on the selected item clicks it, as WinForms does.</summary>
    protected internal virtual bool ProcessDialogKey(Keys keyData)
    {
        if (keyData != Keys.Enter) return false;

        PerformClick();
        return true;
    }

    /// <summary>The item's mnemonic was pressed: it is clicked.</summary>
    protected internal virtual bool ProcessMnemonic(char charCode)
    {
        if (!Enabled || !Available || !Control.IsMnemonic(charCode, Text)) return false;

        PerformClick();
        return true;
    }

    /// <summary>Clicks the item as if the user had, running its Click handlers.</summary>
    public void PerformClick()
    {
        if (!Enabled || !Available) return;

        OnClick(EventArgs.Empty);
    }

    public void Select()
    {
        if (!CanSelect) return;

        SetSelectedInternal(true);
    }

    protected virtual void SetBounds(Rectangle bounds)
    {
        if (bounds == _bounds) return;

        _bounds = bounds;
        OnBoundsChanged();
    }

    // Visible and Available are one switch, as in WinForms: hiding an item takes it out of the layout.
    protected virtual void SetVisibleCore(bool visible)
    {
        if (visible == _available) return;

        _available = visible;
        OnAvailableChanged(EventArgs.Empty);
        OnVisibleChanged(EventArgs.Empty);
        Owner?.DoLayout(this, nameof(Visible));
    }

    public override string ToString() => string.IsNullOrEmpty(Text) ? base.ToString() ?? GetType().Name : $"{GetType().Name}: {Text}";

    /// <summary>The strip's own hit-tracking: pressed by a mouse button held over the item.</summary>
    internal void SetPressedInternal(bool value)
    {
        if (value == _pressed) return;

        _pressed = value;
        Invalidate();
    }

    /// <summary>The strip's own hit-tracking: highlighted because the mouse (or keyboard focus) is over the item.</summary>
    internal void SetSelectedInternal(bool value)
    {
        if (value == _selected) return;

        _selected = value;
        Invalidate();
        (Parent as ToolStripDropDown)?.OnShownItemSelectedChanged(this);
        OnSelectedChanged(EventArgs.Empty);
    }

    internal void RaiseMouseEnter() => OnMouseEnter(EventArgs.Empty);

    internal void RaiseMouseLeave() => OnMouseLeave(EventArgs.Empty);

    internal void RaiseMouseHover() => OnMouseHover(EventArgs.Empty);

    internal void RaiseMouseDown(MouseEventArgs e) => OnMouseDown(e);

    internal void RaiseMouseUp(MouseEventArgs e) => OnMouseUp(e);

    internal void RaiseMouseMove(MouseEventArgs e) => OnMouseMove(e);

    internal void RaiseDoubleClick(EventArgs e) => OnDoubleClick(e);

    internal void RaiseOwnerFontChanged(EventArgs e) => OnOwnerFontChanged(e);

    internal void PaintInternal(PaintEventArgs e) => OnPaint(e);

    /// <summary>Called by <see cref="ToolStripItemCollection"/> when the item joins or leaves a strip's Items: it is then both owner and parent.</summary>
    internal void SetParentInternal(ToolStrip? owner)
    {
        if (owner == _owner && owner == Parent) return;

        var ownerChanged = owner != _owner;
        _owner = owner;
        SetDisplayParentInternal(owner);
        if (ownerChanged) OnOwnerChanged(EventArgs.Empty);
        ParentChangedInternal();
    }

    /// <summary>Moves where the item is shown without changing who owns it: the overflow drop-down borrows it, its strip takes it back.</summary>
    internal void SetDisplayParentInternal(ToolStrip? parent)
    {
        if (parent == Parent) return;

        var old = Parent;
        Parent = parent;
        (old as ToolStripDropDown)?.ForgetActiveItem(this);
        OnParentChanged(old, parent);
    }

    /// <summary>After the item moved to another strip or left its strip: an item with a drop-down closes it.</summary>
    internal virtual void ParentChangedInternal()
    {
    }

    /// <summary>Set by the owning <see cref="ToolStrip"/>'s layout once it decides where this item goes.</summary>
    internal void SetPlacementInternal(ToolStripItemPlacement placement) => Placement = placement;

    Rectangle IArrangedElement.Bounds => Bounds;

    void IArrangedElement.SetBounds(Rectangle bounds, BoundsSpecified specified) => SetBounds(bounds);

    Size IArrangedElement.GetPreferredSize(Size proposedSize) => GetPreferredSize(proposedSize);

    Rectangle IArrangedElement.DisplayRectangle => ContentRectangle;

    bool IArrangedElement.ParticipatesInLayout => Available;

    IArrangedElement? IArrangedElement.Container => _owner;

    ArrangedElementCollection IArrangedElement.Children => EmptyArrangedElementCollection.Instance;

    LayoutSettingsStore IArrangedElement.LayoutStore => _layoutStore ??= new LayoutSettingsStore();

    private LayoutSettingsStore? _layoutStore;

    void IArrangedElement.PerformLayout(IArrangedElement affectedElement, string? propertyName) =>
        _owner?.DoLayout(affectedElement, propertyName ?? string.Empty);
}
