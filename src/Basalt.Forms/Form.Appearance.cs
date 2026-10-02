using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The chrome around a form's client area that is not shared with every
/// control: its icon, help button, size grip and the window-level colour key
/// used for translucency.
/// </summary>
public partial class Form
{
    private Icon? _icon;
    private bool _showIcon = true;
    private bool _helpButton;
    private SizeGripStyle _sizeGripStyle;
    private Color _transparencyKey = Color.Empty;
    private bool _allowTransparency;

    /// <summary>
    /// The icon in the title bar and the taskbar. A top-level form hands it to
    /// its native window (<see cref="Internal.Hosting.HostWindow.SetIcon"/>);
    /// one placed in another control draws it itself, in the caption
    /// <see cref="NonClientCaption"/> paints (see Internal/NonClientArea.cs
    /// for the frame it is painted inside of).
    /// </summary>
    [AmbientValue(null)]
    [Localizable(true)]
    public Icon? Icon
    {
        get => _icon;
        set
        {
            if (Equals(value, _icon)) return;

            _icon = value;
            ApplyIcon();
        }
    }

    /// <summary>Whether the icon (and, with it, the system menu it stands for) is shown at all.</summary>
    [DefaultValue(true)]
    public bool ShowIcon
    {
        get => _showIcon;
        set
        {
            if (value == _showIcon) return;

            _showIcon = value;
            ApplyIcon();
        }
    }

    private void ApplyIcon()
    {
        if (GetTopLevel()) Host?.SetIcon(_showIcon ? _icon : null);
        else InvalidateFrame();
    }

    /// <summary>
    /// A help button next to the close box, shown only when there is neither a
    /// minimize nor a maximize box. The title bar that would draw it belongs
    /// to the native window on a top-level form, which this platform's
    /// windowing layer does not let a caller add buttons to. As on Windows,
    /// <see cref="HelpButtonClicked"/> comes only from that button: F1 is a
    /// help request for the focused control (HelpRequested), button or not.
    /// </summary>
    [DefaultValue(false)]
    public bool HelpButton
    {
        get => _helpButton;
        set
        {
            if (value == _helpButton) return;

            _helpButton = value;
            InvalidateFrame();
        }
    }

    protected virtual void OnHelpButtonClicked(CancelEventArgs e) => ((CancelEventHandler?)Events[s_helpButtonClickedEvent])?.Invoke(this, e);

    public event CancelEventHandler? HelpButtonClicked
    {
        add => Events.AddHandler(s_helpButtonClickedEvent, value);
        remove => Events.RemoveHandler(s_helpButtonClickedEvent, value);
    }

    private static readonly object s_helpButtonClickedEvent = new();

    [Browsable(false)]
    [DefaultValue(SizeGripStyle.Auto)]
    public SizeGripStyle SizeGripStyle
    {
        get => _sizeGripStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(SizeGripStyle));
            if (value == _sizeGripStyle) return;

            _sizeGripStyle = value;
            InvalidateFrame();
        }
    }

    /// <summary>
    /// The colour painted fully transparent, when <see cref="AllowTransparency"/>
    /// is set. Per-pixel colour-key transparency needs a layered window, which
    /// this platform's windowing layer (Avalonia) does not expose; the colour
    /// is kept so round-tripping the property works, but nothing is drawn
    /// through yet.
    /// </summary>
    public Color TransparencyKey
    {
        get => _transparencyKey;
        set
        {
            if (value == _transparencyKey) return;

            _transparencyKey = value;
            if (_allowTransparency) InvalidateFrame();
        }
    }

    /// <summary>
    /// Whether <see cref="TransparencyKey"/> takes effect. Windows needs a
    /// layered window for it; this platform's windowing layer has none, so
    /// setting this is a documented no-op on the drawing itself (as Windows
    /// would do nothing either, without <see cref="OSFeature.LayeredWindows"/>),
    /// kept only so the property round-trips for code that sets and reads it.
    /// </summary>
    [DefaultValue(false)]
    public bool AllowTransparency
    {
        get => _allowTransparency;
        set => _allowTransparency = value;
    }

    private bool _rightToLeftLayout;

    /// <summary>
    /// Whether the window's own non-client parts (the ones drawn in
    /// Internal/Skins, not a control's own RightToLeft) mirror left to right.
    /// The self-drawn frame of a non-top-level form does not yet mirror; a
    /// top-level one keeps its native title bar either way, so this flips
    /// only what <see cref="Control.RightToLeft"/> already flips for its
    /// children.
    /// </summary>
    [Localizable(true)]
    [DefaultValue(false)]
    public virtual bool RightToLeftLayout
    {
        get => _rightToLeftLayout;
        set
        {
            if (value == _rightToLeftLayout) return;

            _rightToLeftLayout = value;
            OnRightToLeftLayoutChanged(EventArgs.Empty);
        }
    }

    protected virtual void OnRightToLeftLayoutChanged(EventArgs e) => ((EventHandler?)Events[s_rightToLeftLayoutEvent])?.Invoke(this, e);

    public event EventHandler? RightToLeftLayoutChanged
    {
        add => Events.AddHandler(s_rightToLeftLayoutEvent, value);
        remove => Events.RemoveHandler(s_rightToLeftLayoutEvent, value);
    }

    private static readonly object s_rightToLeftLayoutEvent = new();

    /// <summary>The bounds the window had before its last maximize or minimize (see the WindowState setter in Form.cs).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Rectangle RestoreBounds => _restoreBounds.IsEmpty ? Bounds : _restoreBounds;
}
