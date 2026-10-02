using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>A frame that shows an Image, placed inside it as SizeMode describes.</summary>
[DefaultProperty("Image")]
[DefaultBindingProperty("Image")]
public partial class PictureBox : Control, ISupportInitialize
{
    private Image? _image;
    private string? _imageLocation;
    private Image? _errorImage;
    private Image? _initialImage;
    private PictureBoxSizeMode _sizeMode = PictureBoxSizeMode.Normal;
    private BorderStyle _borderStyle = BorderStyle.None;

    public PictureBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    protected override Size DefaultSize => new(100, 50);

    [DefaultValue(BorderStyle.None)]
    public BorderStyle BorderStyle
    {
        get => _borderStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(BorderStyle));
            if (_borderStyle == value) return;

            _borderStyle = value;
            UpdateStyles();
        }
    }

    /// <summary>Fixed3D is a sunken two-pixel edge, as a container's would be.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;

            if (_borderStyle == BorderStyle.FixedSingle) parameters.Style |= WindowStyles.Border;
            else if (_borderStyle == BorderStyle.Fixed3D) parameters.ExStyle |= WindowStyles.ExClientEdge;

            return parameters;
        }
    }

    [Localizable(true)]
    [Bindable(true)]
    public Image? Image
    {
        get => _image;
        set
        {
            if (_image == value) return;

            _image = value;
            _imageLocation = null;

            if (_sizeMode == PictureBoxSizeMode.AutoSize) AdjustSize();
            Invalidate();
        }
    }

    [Localizable(true)]
    [DefaultValue(null)]
    public string? ImageLocation
    {
        get => _imageLocation;
        set
        {
            if (_imageLocation == value) return;

            _imageLocation = value;
            Invalidate();
        }
    }

    [Localizable(true)]
    public Image? ErrorImage
    {
        get => _errorImage;
        set
        {
            if (_errorImage == value) return;

            _errorImage = value;
            Invalidate();
        }
    }

    [Localizable(true)]
    public Image? InitialImage
    {
        get => _initialImage;
        set
        {
            if (_initialImage == value) return;

            _initialImage = value;
            Invalidate();
        }
    }

    [DefaultValue(PictureBoxSizeMode.Normal)]
    [Localizable(true)]
    [RefreshProperties(RefreshProperties.Repaint)]
    public PictureBoxSizeMode SizeMode
    {
        get => _sizeMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(PictureBoxSizeMode));
            if (_sizeMode == value) return;

            _sizeMode = value;

            // AutoSize tracks SizeMode here: the two can never disagree, as in WinForms.
            AutoSize = value == PictureBoxSizeMode.AutoSize;

            // Adjusting now, before an Image is set, would cache DefaultSize as the
            // preferred size and that stale cache would survive the image arriving.
            if (AutoSize && _image is not null) AdjustSize();

            Invalidate();
            OnSizeModeChanged(EventArgs.Empty);
        }
    }

    protected virtual void OnSizeModeChanged(EventArgs e) => SizeModeChanged?.Invoke(this, e);

    public event EventHandler? SizeModeChanged;

    internal override Size GetPreferredSizeCore(Size proposedSize)
    {
        if (_sizeMode == PictureBoxSizeMode.AutoSize && _image is { } image) return new Size(image.Width, image.Height);

        return base.GetPreferredSizeCore(proposedSize);
    }

    private void AdjustSize()
    {
        if (!AutoSize) return;

        var preferred = GetPreferredSize(Size.Empty);
        if (preferred != Size) Size = preferred;
    }

    /// <summary>Loads the image ImageLocation names, synchronously, from a local file.</summary>
    public void Load()
    {
        if (string.IsNullOrEmpty(_imageLocation))
        {
            throw new InvalidOperationException("The ImageLocation property has not been set, so an image cannot be loaded.");
        }

        Load(_imageLocation);
    }

    /// <summary>
    /// Loads an image from a local file path. A non-file URL is not
    /// supported in this implementation: WinForms fetches it over the
    /// network, which this reimplementation does not attempt.
    /// </summary>
    public void Load(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        var path = url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            throw new PlatformNotSupportedException("PictureBox can only load images from local files in this implementation.");
        }

        if (uri is { IsFile: true }) path = uri.LocalPath;

        // The Image setter clears ImageLocation, as a direct assignment should;
        // restore it afterwards, since this load came from that same location.
        Image = Image.FromFile(path);
        _imageLocation = url;
    }

    void ISupportInitialize.BeginInit()
    {
    }

    void ISupportInitialize.EndInit()
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_image is { } image) DrawImage(e.Graphics, image);
        base.OnPaint(e);
    }

    private void DrawImage(Graphics graphics, Image image)
    {
        var client = ClientRectangle;

        switch (_sizeMode)
        {
            case PictureBoxSizeMode.StretchImage:
                graphics.DrawImage(image, client);
                break;

            case PictureBoxSizeMode.CenterImage:
                graphics.DrawImage(image, client.Left + (client.Width - image.Width) / 2, client.Top + (client.Height - image.Height) / 2, image.Width, image.Height);
                break;

            case PictureBoxSizeMode.Zoom:
                DrawZoomed(graphics, image, client);
                break;

            default:
                graphics.DrawImage(image, client.Left, client.Top, image.Width, image.Height);
                break;
        }
    }

    private static void DrawZoomed(Graphics graphics, Image image, Rectangle client)
    {
        if (image.Width <= 0 || image.Height <= 0 || client.Width <= 0 || client.Height <= 0) return;

        var scale = Math.Min((float)client.Width / image.Width, (float)client.Height / image.Height);
        var width = (int)(image.Width * scale);
        var height = (int)(image.Height * scale);
        var x = client.Left + (client.Width - width) / 2;
        var y = client.Top + (client.Height - height) / 2;

        graphics.DrawImage(image, x, y, width, height);
    }

    public override string ToString() => $"{base.ToString()}, SizeMode: {SizeMode}";
}
