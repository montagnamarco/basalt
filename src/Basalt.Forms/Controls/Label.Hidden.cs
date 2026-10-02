using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Automation;

namespace System.Windows.Forms;

/// <summary>
/// Everything a Label hides from the designer because it takes no input of
/// its own (KeyDown/Up/Press, TabStop), plus the Image/ImageList group real
/// WinForms Labels also carry alongside Text, and LiveSetting: a label is
/// the control IAutomationLiveRegion was made for, announcing new text
/// (OnTextChanged, in Label.cs) without ever taking focus.
/// </summary>
public partial class Label
{
    private AutomationLiveSetting _liveSetting = AutomationLiveSetting.Off;
    private Image? _image;
    private ImageList? _imageList;
    private int _imageIndex = -1;
    private string? _imageKey = "";
    private ContentAlignment _imageAlign = ContentAlignment.MiddleCenter;
    private FlatStyle _flatStyle = FlatStyle.Standard;
    private bool _renderTransparent;

    public AutomationLiveSetting LiveSetting
    {
        get => _liveSetting;
        set => _liveSetting = value;
    }

    [Localizable(true)]
    public Image? Image
    {
        get => _image ?? ResolveListImage();
        set
        {
            if (_image == value) return;

            _image = value;
            if (value is not null) { _imageIndex = -1; _imageKey = ""; }
            Invalidate();
        }
    }

    [DefaultValue(-1)]
    [Localizable(true)]
    public int ImageIndex
    {
        get => _imageIndex;
        set
        {
            if (_imageIndex == value) return;

            _imageIndex = value;
            if (value >= 0) { _image = null; _imageKey = ""; }
            Invalidate();
        }
    }

    [DefaultValue("")]
    [Localizable(true)]
    public string ImageKey
    {
        get => _imageKey ?? "";
        set
        {
            if (_imageKey == value) return;

            _imageKey = value;
            if (!string.IsNullOrEmpty(value)) { _image = null; _imageIndex = -1; }
            Invalidate();
        }
    }

    public ImageList? ImageList
    {
        get => _imageList;
        set
        {
            if (_imageList == value) return;

            _imageList = value;
            Invalidate();
        }
    }

    [DefaultValue(ContentAlignment.MiddleCenter)]
    [Localizable(true)]
    public ContentAlignment ImageAlign
    {
        get => _imageAlign;
        set
        {
            if (_imageAlign == value) return;

            _imageAlign = value;
            Invalidate();
        }
    }

    private Image? ResolveListImage()
    {
        if (_imageList is null) return null;
        if (_imageIndex >= 0) return _imageList.Images[_imageIndex];
        if (!string.IsNullOrEmpty(_imageKey)) return _imageList.Images[_imageKey];

        return null;
    }

    [DefaultValue(FlatStyle.Standard)]
    public FlatStyle FlatStyle
    {
        get => _flatStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(FlatStyle));
            if (_flatStyle == value) return;

            _flatStyle = value;
            Invalidate();
        }
    }

    /// <summary>
    /// GDI+'s compatible text rendering path, dropped from .NET's WinForms:
    /// stored so existing designer files round-trip, but TextRenderer (GDI,
    /// via MeasureText/DrawText in Label.cs) is all that ever draws here.
    /// </summary>
    [DefaultValue(false)]
    public bool UseCompatibleTextRendering { get; set; }

    /// <summary>
    /// Whether the label's own background should be left unfilled, true once
    /// BackColor is transparent: Microsoft does not expose a public
    /// OnPaintBackground override on Label to hook this through (it is not
    /// in the reference surface), so this stays the computed answer alone,
    /// available to a derived class that paints its own background.
    /// </summary>
    protected virtual bool RenderTransparent
    {
        get => _renderTransparent || BackColor.A == 0;
        set
        {
            if (_renderTransparent == value) return;

            _renderTransparent = value;
            Invalidate();
        }
    }

    /// <summary>Where Image sits within bounds, anchored by ImageAlign and sized to the image itself (Label never stretches it).</summary>
    protected Rectangle CalcImageRenderBounds(Image image, Rectangle bounds, ContentAlignment align)
    {
        var x = align switch
        {
            ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => bounds.Left,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => bounds.Right - image.Width,
            _ => bounds.Left + (bounds.Width - image.Width) / 2,
        };

        var y = align switch
        {
            ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight => bounds.Top,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => bounds.Bottom - image.Height,
            _ => bounds.Top + (bounds.Height - image.Height) / 2,
        };

        return new Rectangle(x, y, image.Width, image.Height);
    }

    protected void DrawImage(Graphics graphics, Image image, Rectangle bounds, ContentAlignment align)
    {
        var target = CalcImageRenderBounds(image, bounds, align);
        graphics.DrawImage(image, target);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        base.OnHandleDestroyed(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
    }

    protected override void OnRightToLeftChanged(EventArgs e)
    {
        base.OnRightToLeftChanged(e);
        Invalidate();
    }

    protected virtual void OnTextAlignChanged(EventArgs e) => ((EventHandler?)Events[s_textAlignChangedEvent])?.Invoke(this, e);

    public event EventHandler? TextAlignChanged
    {
        add => Events.AddHandler(s_textAlignChangedEvent, value);
        remove => Events.RemoveHandler(s_textAlignChangedEvent, value);
    }

    private static readonly object s_textAlignChangedEvent = new();

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
    }

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        base.SetBoundsCore(x, y, width, height, specified);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? AutoSizeChanged
    {
        add => base.AutoSizeChanged += value;
        remove => base.AutoSizeChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? BackgroundImageChanged
    {
        add => base.BackgroundImageChanged += value;
        remove => base.BackgroundImageChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? BackgroundImageLayoutChanged
    {
        add => base.BackgroundImageLayoutChanged += value;
        remove => base.BackgroundImageLayoutChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event KeyEventHandler? KeyDown
    {
        add => base.KeyDown += value;
        remove => base.KeyDown -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event KeyPressEventHandler? KeyPress
    {
        add => base.KeyPress += value;
        remove => base.KeyPress -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event KeyEventHandler? KeyUp
    {
        add => base.KeyUp += value;
        remove => base.KeyUp -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? TabStopChanged
    {
        add => base.TabStopChanged += value;
        remove => base.TabStopChanged -= value;
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
}
