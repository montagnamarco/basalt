using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

public partial class Form
{
    private Color _formBorderColor;
    private Color _formCaptionBackColor;
    private Color _formCaptionTextColor;

    public Color FormBorderColor
    {
        get => _formBorderColor;
        set { if (SetFrameColor(ref _formBorderColor, value)) OnFormBorderColorChanged(EventArgs.Empty); }
    }

    public Color FormCaptionBackColor
    {
        get => _formCaptionBackColor;
        set { if (SetFrameColor(ref _formCaptionBackColor, value)) OnFormCaptionBackColorChanged(EventArgs.Empty); }
    }

    public Color FormCaptionTextColor
    {
        get => _formCaptionTextColor;
        set { if (SetFrameColor(ref _formCaptionTextColor, value)) OnFormCaptionTextColorChanged(EventArgs.Empty); }
    }

    private bool SetFrameColor(ref Color storage, Color value)
    {
        // Embedded frames belong to the canvas. Native title bars require an OS backend.
        if (GetTopLevel() && !value.IsEmpty)
            throw new PlatformNotSupportedException("Custom native window frame colors are not supported by this host.");
        if (storage == value) return false;
        storage = value;
        InvalidateFrame();
        return true;
    }

    public FormCornerPreference FormCornerPreference
    {
        get => FormCornerPreference.Default;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(FormCornerPreference));
            if (value != FormCornerPreference.Default)
                throw new PlatformNotSupportedException("Window corner preferences require native host support.");
        }
    }

    public ScreenCaptureMode FormScreenCaptureMode
    {
        get => ScreenCaptureMode.Allow;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ScreenCaptureMode));
            if (value != ScreenCaptureMode.Allow)
                throw new PlatformNotSupportedException("Screen capture exclusion requires native host support.");
        }
    }

    public MainMenu? Menu
    {
        get => null;
        set
        {
            if (value is not null)
                throw new PlatformNotSupportedException("Legacy MainMenu is not supported. Use MenuStrip.");
        }
    }

    public MainMenu? MergedMenu => null;

    public event EventHandler? FormBorderColorChanged;
    public event EventHandler? FormCaptionBackColorChanged;
    public event EventHandler? FormCaptionTextColorChanged;
    public event EventHandler? FormCornerPreferenceChanged;
    protected virtual void OnFormBorderColorChanged(EventArgs e) => FormBorderColorChanged?.Invoke(this, e);
    protected virtual void OnFormCaptionBackColorChanged(EventArgs e) => FormCaptionBackColorChanged?.Invoke(this, e);
    protected virtual void OnFormCaptionTextColorChanged(EventArgs e) => FormCaptionTextColorChanged?.Invoke(this, e);
    protected virtual void OnFormCornerPreferenceChanged(EventArgs e) => FormCornerPreferenceChanged?.Invoke(this, e);
}
