using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The ancillary, rarely-touched properties and seams every item inherits:
/// layout hints the strip reads (<see cref="Anchor"/>, <see cref="Dock"/>,
/// merge bookkeeping), ambient ones it mirrors from its owner
/// (<see cref="RightToLeft"/>, <see cref="BackgroundImage"/>), and the
/// protected hooks a subclass overrides instead of the strip reaching in.
/// </summary>
public abstract partial class ToolStripItem
{
    private AnchorStyles _anchor = AnchorStyles.Top | AnchorStyles.Left;
    private bool _autoToolTip;
    private Image? _backgroundImage;
    private ImageLayout _backgroundImageLayout;
    private DockStyle _dock;
    private bool _doubleClickEnabled;
    private ToolStripItemImageScaling _imageScaling = ToolStripItemImageScaling.SizeToFit;
    private Color _imageTransparentColor = Color.Empty;
    private MergeAction _mergeAction = MergeAction.Append;
    private int _mergeIndex = -1;
    private bool _rightToLeftAutoMirrorImage;
    private RightToLeft _rightToLeft = RightToLeft.Inherit;
    private ToolStripTextDirection _textDirection = ToolStripTextDirection.Horizontal;
    private TextImageRelation _textImageRelation = TextImageRelation.ImageBeforeText;

    public AnchorStyles Anchor
    {
        get => _anchor;
        set => _anchor = value;
    }

    [DefaultValue(false)]
    public bool AutoToolTip
    {
        get => _autoToolTip;
        set => _autoToolTip = value;
    }

    public virtual Image? BackgroundImage
    {
        get => _backgroundImage;
        set
        {
            if (value == _backgroundImage) return;

            _backgroundImage = value;
            Invalidate();
        }
    }

    public virtual ImageLayout BackgroundImageLayout
    {
        get => _backgroundImageLayout;
        set
        {
            if (value == _backgroundImageLayout) return;

            _backgroundImageLayout = value;
            Invalidate();
        }
    }

    [DefaultValue(DockStyle.None)]
    public DockStyle Dock
    {
        get => _dock;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(DockStyle));
            if (value == _dock) return;

            _dock = value;
            Owner?.DoLayout(this, nameof(Dock));
        }
    }

    [DefaultValue(false)]
    public bool DoubleClickEnabled
    {
        get => _doubleClickEnabled;
        set => _doubleClickEnabled = value;
    }

    [DefaultValue(ToolStripItemImageScaling.SizeToFit)]
    public ToolStripItemImageScaling ImageScaling
    {
        get => _imageScaling;
        set
        {
            if (value == _imageScaling) return;

            _imageScaling = value;
            Invalidate();
        }
    }

    public Color ImageTransparentColor
    {
        get => _imageTransparentColor;
        set
        {
            if (value == _imageTransparentColor) return;

            _imageTransparentColor = value;

            // Recolours the actual bitmap, the way Microsoft's own ImageTransparentColor does for an
            // owner-drawn item: there is no separate "transparent key" concept kept at paint time.
            if (!value.IsEmpty && _image is Bitmap bitmap) bitmap.MakeTransparent(value);

            Invalidate();
        }
    }

    /// <summary>Whether this item is shown on a drop-down (its own, a sibling's, or a context menu) rather than directly on a strip.</summary>
    [Browsable(false)]
    public bool IsOnDropDown => ParentInternal is ToolStripDropDown;

    [DefaultValue(MergeAction.Append)]
    public MergeAction MergeAction
    {
        get => _mergeAction;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(MergeAction));

            _mergeAction = value;
        }
    }

    [DefaultValue(-1)]
    public int MergeIndex
    {
        get => _mergeIndex;
        set => _mergeIndex = value;
    }

    /// <summary>The item that owns the drop-down this one is shown on, or null when it is shown directly on a strip.</summary>
    [Browsable(false)]
    public ToolStripItem? OwnerItem => (ParentInternal as ToolStripDropDown)?.OwnerItem;

    [Localizable(true)]
    [AmbientValue(RightToLeft.Inherit)]
    public virtual RightToLeft RightToLeft
    {
        get => _rightToLeft != RightToLeft.Inherit ? _rightToLeft : Owner?.RightToLeft ?? RightToLeft.No;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(RightToLeft));

            var old = RightToLeft;
            _rightToLeft = value;
            if (old != RightToLeft) OnRightToLeftChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(false)]
    public bool RightToLeftAutoMirrorImage
    {
        get => _rightToLeftAutoMirrorImage;
        set
        {
            if (value == _rightToLeftAutoMirrorImage) return;

            _rightToLeftAutoMirrorImage = value;
            Invalidate();
        }
    }

    [DefaultValue(ToolStripTextDirection.Horizontal)]
    public virtual ToolStripTextDirection TextDirection
    {
        get => _textDirection;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ToolStripTextDirection));
            if (value == _textDirection) return;

            _textDirection = value;
            Owner?.DoLayout(this, nameof(TextDirection));
            Invalidate();
        }
    }

    [DefaultValue(TextImageRelation.ImageBeforeText)]
    public TextImageRelation TextImageRelation
    {
        get => _textImageRelation;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(TextImageRelation));
            if (value == _textImageRelation) return;

            _textImageRelation = value;
            Owner?.DoLayout(this, nameof(TextImageRelation));
            Invalidate();
        }
    }

    public event EventHandler? LocationChanged
    {
        add => Events.AddHandler(s_locationChangedEvent, value);
        remove => Events.RemoveHandler(s_locationChangedEvent, value);
    }

    public event EventHandler? RightToLeftChanged
    {
        add => Events.AddHandler(s_rightToLeftChangedEvent, value);
        remove => Events.RemoveHandler(s_rightToLeftChangedEvent, value);
    }

    private static readonly object s_locationChangedEvent = new();
    private static readonly object s_rightToLeftChangedEvent = new();

    /// <summary>Whether a click on this item should close the drop-down chain it is shown on; a split button's arrow overrides this to say no.</summary>
    protected virtual bool DismissWhenClicked => true;

    /// <summary>Not part of Microsoft's public contract: lets ToolStripDropDown (a Control, not a ToolStripItem) read the protected DismissWhenClicked above when deciding whether a click should close it.</summary>
    internal bool DismissWhenClickedInternal => DismissWhenClicked;

    /// <summary>The renderer this item paints with: its owning strip's, or none while it has no owner yet.</summary>
    protected virtual ToolStripRenderer? Renderer => Owner?.Renderer;

    protected virtual bool IsInputChar(char charCode) => false;

    protected virtual bool IsInputKey(Keys keyData) => false;

    protected virtual void OnLayout(LayoutEventArgs e)
    {
    }

    protected virtual void OnLocationChanged(EventArgs e) => ((EventHandler?)Events[s_locationChangedEvent])?.Invoke(this, e);

    /// <summary>Called when the owning strip's own RightToLeft changes; forwarded only while this item still inherits it.</summary>
    protected virtual void OnParentRightToLeftChanged(EventArgs e)
    {
        if (_rightToLeft == RightToLeft.Inherit) OnRightToLeftChanged(e);
    }

    protected virtual void OnRightToLeftChanged(EventArgs e)
    {
        Owner?.DoLayout(this, nameof(RightToLeft));
        Invalidate();
        ((EventHandler?)Events[s_rightToLeftChangedEvent])?.Invoke(this, e);
    }

    public virtual void ResetDisplayStyle() => DisplayStyle = DefaultDisplayStyle;

    public virtual void ResetFont()
    {
        if (_font is null) return;

        _font = null;
        OnFontChanged(EventArgs.Empty);
    }

    public virtual void ResetImage() => Image = null;

    public void ResetMargin() => Margin = DefaultMargin;

    public void ResetPadding() => Padding = DefaultPadding;

    public virtual void ResetRightToLeft() => RightToLeft = RightToLeft.Inherit;

    public virtual void ResetTextDirection() => TextDirection = ToolStripTextDirection.Horizontal;

    internal void RaiseParentRightToLeftChanged(EventArgs e) => OnParentRightToLeftChanged(e);
}
