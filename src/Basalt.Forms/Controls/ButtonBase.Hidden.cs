using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// ButtonBase's Image/ImageList group (shared by Button, CheckBox and
/// RadioButton, none of which paint it yet beyond storing and invalidating
/// - the glyph-and-text layouts in ButtonBase/CheckBox.PaintButton are what
/// each subclass actually draws), TextImageRelation, and the designer
/// forwarders AutoSize already set the pattern for (Browsable(true), since
/// a button's own AutoSize is visible unlike a plain Control's).
/// </summary>
public abstract partial class ButtonBase
{
    private Image? _image;
    private ImageList? _imageList;
    private int _imageIndex = -1;
    private string? _imageKey = "";
    private ContentAlignment _imageAlign = ContentAlignment.MiddleCenter;
    private TextImageRelation _textImageRelation = TextImageRelation.Overlay;
    private bool _useCompatibleTextRendering;
    private bool _useVisualStyleBackColor;

    protected override AccessibleObject CreateAccessibilityInstance() => new ButtonBaseAccessibleObject(this);

    protected override void Dispose(bool disposing)
    {
        if (disposing && _command is not null) _command.CanExecuteChanged -= CommandCanExecuteChangedHandler;

        base.Dispose(disposing);
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        Invalidate();
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        Invalidate();
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
    }

    /// <summary>Drops the pressed/hot state and repaints: what WinForms does when a button can no longer trust its last mouse state (losing capture, its window recreated).</summary>
    protected void ResetFlagsandPaint()
    {
        _inputPressed = false;
        _mouseInside = false;
        Invalidate();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ClassName = "Button";
            return parameters;
        }
    }

    public override Size GetPreferredSize(Size proposedSize) => base.GetPreferredSize(proposedSize);

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    public new event EventHandler? AutoSizeChanged
    {
        add => base.AutoSizeChanged += value;
        remove => base.AutoSizeChanged -= value;
    }

    [DefaultValue(false)]
    public bool AutoEllipsis { get; set; }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Color BackColor
    {
        get => base.BackColor;
        set => base.BackColor = value;
    }

    [Bindable(true)]
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

    [DefaultValue(TextImageRelation.Overlay)]
    [Localizable(true)]
    public TextImageRelation TextImageRelation
    {
        get => _textImageRelation;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(TextImageRelation));
            if (_textImageRelation == value) return;

            _textImageRelation = value;
            Invalidate();
        }
    }

    /// <summary>GDI+'s compatible text rendering path, dropped from .NET's WinForms: stored for round-tripping, TextRenderer always draws.</summary>
    [DefaultValue(false)]
    public bool UseCompatibleTextRendering
    {
        get => _useCompatibleTextRendering;
        set => _useCompatibleTextRendering = value;
    }

    /// <summary>Whether BackColor paints through the current visual style instead of as a flat fill; we have one look (the classic one), so this only remembers the setting.</summary>
    [DefaultValue(false)]
    public bool UseVisualStyleBackColor
    {
        get => _useVisualStyleBackColor;
        set
        {
            if (_useVisualStyleBackColor == value) return;

            _useVisualStyleBackColor = value;
            Invalidate();
        }
    }
}
