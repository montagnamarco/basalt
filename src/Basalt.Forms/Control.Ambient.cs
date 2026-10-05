using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>
/// The values a control takes from its parent unless it has its own:
/// colours, font, reading order, and whether it is visible and enabled.
/// </summary>
public partial class Control
{
    private Color _backColor;
    private Color _foreColor;
    private Font? _font;
    private RightToLeft _rightToLeft = RightToLeft.Inherit;

    /// <summary>The control's background colour: its own, else its parent's, else the system's.</summary>
    public virtual Color BackColor
    {
        get
        {
            if (!_backColor.IsEmpty) return _backColor;

            return _parent is not null && ShouldInheritParentBackColor() ? _parent.BackColor
                : PortableVisualStylesEnabled ? Internal.Rendering.PortableVisualStyles.Palette(PortableVisualStylePlatform).Surface
                : DefaultBackColor;
        }
        set
        {
            if (!value.IsEmpty && value.A < 255 && !GetStyle(ControlStyles.SupportsTransparentBackColor))
            {
                throw new ArgumentException("Control does not support transparent background colors.", nameof(value));
            }

            var old = BackColor;
            _backColor = value;
            if (old != BackColor) OnBackColorChanged(EventArgs.Empty);
        }
    }

    /// <summary>A child takes its parent's background unless the parent is transparent and the child cannot be.</summary>
    private bool ShouldInheritParentBackColor() => _parent!.BackColor.A == 255 || GetStyle(ControlStyles.SupportsTransparentBackColor);

    public virtual void ResetBackColor() => BackColor = Color.Empty;

    internal bool ShouldSerializeBackColor() => !_backColor.IsEmpty;

    public virtual Color ForeColor
    {
        get
        {
            if (!_foreColor.IsEmpty) return _foreColor;

            return _parent?.ForeColor ?? (PortableVisualStylesEnabled
                ? Internal.Rendering.PortableVisualStyles.Palette(PortableVisualStylePlatform).Text : DefaultForeColor);
        }
        set
        {
            var old = ForeColor;
            _foreColor = value;
            if (old != ForeColor) OnForeColorChanged(EventArgs.Empty);
        }
    }

    public virtual void ResetForeColor() => ForeColor = Color.Empty;

    internal bool ShouldSerializeForeColor() => !_foreColor.IsEmpty;

    /// <summary>The font: the control's own, else its parent's, else the designer's ambient one, else the default.</summary>
    [Localizable(true)]
    public virtual Font Font
    {
        get
        {
            if (_font is not null) return _font;
            if (_parent is not null) return _parent.Font;

            return AmbientFont() ?? Application.DefaultFontOverride
                ?? (PortableVisualStylesEnabled
                    ? Internal.Rendering.PortableVisualStyles.DefaultFont(PortableVisualStylePlatform)
                    : SystemFonts.DefaultFont);
        }
        set
        {
            var old = Font;
            _font = value;
            if (!old.Equals(Font)) OnFontChanged(EventArgs.Empty);
        }
    }

    public virtual void ResetFont() => Font = null!;

    internal bool ShouldSerializeFont() => _font is not null;

    internal bool IsFontSet() => _font is not null;

    /// <summary>The font a host such as the designer offers through the site.</summary>
    private Font? AmbientFont() => (Site?.GetService(typeof(AmbientProperties)) as AmbientProperties)?.Font;

    private int _fontHeight = -1;

    /// <summary>The height of a line of the font, cached until the font changes.</summary>
    protected int FontHeight
    {
        get
        {
            if (_fontHeight == -1) _fontHeight = Font.Height;

            return _fontHeight;
        }
        set => _fontHeight = value;
    }

    [Localizable(true)]
    [AmbientValue(RightToLeft.Inherit)]
    public virtual RightToLeft RightToLeft
    {
        get
        {
            if (_rightToLeft != RightToLeft.Inherit) return _rightToLeft;

            return _parent?.RightToLeft ?? RightToLeft.No;
        }
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(RightToLeft));

            var old = RightToLeft;
            _rightToLeft = value;
            if (old != RightToLeft) OnRightToLeftChanged(EventArgs.Empty);
        }
    }

    public virtual void ResetRightToLeft() => RightToLeft = RightToLeft.Inherit;

    internal bool ShouldSerializeRightToLeft() => _rightToLeft != RightToLeft.Inherit;

    /// <summary>The system's control colour, which the default skin paints with.</summary>
    public static Color DefaultBackColor => SystemColors.Control;

    public static Color DefaultForeColor => SystemColors.ControlText;

    /// <summary>
    /// The font of controls that set none: the application's, set with
    /// Application.SetDefaultFont, else the portable platform font when visual
    /// styles are enabled, else the classic Microsoft Sans Serif default.
    /// The existing font resolver supplies an installed fallback when needed.
    /// </summary>
    public static Font DefaultFont => Application.DefaultFontOverride
        ?? (Application.UseVisualStyles
            ? Internal.Rendering.PortableVisualStyles.DefaultFont(Internal.Rendering.PortableVisualStyles.CurrentPlatform)
            : SystemFonts.DefaultFont);

    protected virtual void OnBackColorChanged(EventArgs e)
    {
        if (IsDisposingInHierarchy()) return;

        Invalidate();
        ((EventHandler?)Events[s_backColorEvent])?.Invoke(this, e);

        foreach (var child in ChildrenSnapshot()) child.OnParentBackColorChanged(e);
    }

    protected virtual void OnForeColorChanged(EventArgs e)
    {
        if (IsDisposingInHierarchy()) return;

        Invalidate();
        ((EventHandler?)Events[s_foreColorEvent])?.Invoke(this, e);

        foreach (var child in ChildrenSnapshot()) child.OnParentForeColorChanged(e);
    }

    /// <summary>A new font repaints, may change the children's sizes, and lays out.</summary>
    protected virtual void OnFontChanged(EventArgs e)
    {
        if (IsDisposingInHierarchy()) return;

        Invalidate();
        _fontHeight = -1;
        ((EventHandler?)Events[s_fontEvent])?.Invoke(this, e);

        if (_controls is not null)
        {
            SuspendLayout();

            try
            {
                foreach (var child in ChildrenSnapshot()) child.OnParentFontChanged(e);
            }
            finally
            {
                ResumeLayout(false);
            }
        }

        DoLayout(this, PropertyNames.Font);

        // A control that sizes itself to its content has a new preferred size: its parent lays it out again.
        if (AutoSize) _parent?.DoLayout(this, PropertyNames.Font);
    }

    protected virtual void OnRightToLeftChanged(EventArgs e)
    {
        if (IsDisposingInHierarchy()) return;

        ((EventHandler?)Events[s_rightToLeftEvent])?.Invoke(this, e);

        foreach (var child in ChildrenSnapshot()) child.OnParentRightToLeftChanged(e);
    }

    protected virtual void OnParentBackColorChanged(EventArgs e)
    {
        if (_backColor.IsEmpty) OnBackColorChanged(e);
    }

    protected virtual void OnParentForeColorChanged(EventArgs e)
    {
        if (_foreColor.IsEmpty) OnForeColorChanged(e);
    }

    protected virtual void OnParentFontChanged(EventArgs e)
    {
        if (_font is null) OnFontChanged(e);
    }

    protected virtual void OnParentRightToLeftChanged(EventArgs e)
    {
        if (_rightToLeft == RightToLeft.Inherit) OnRightToLeftChanged(e);
    }

    public event EventHandler? BackColorChanged
    {
        add => Events.AddHandler(s_backColorEvent, value);
        remove => Events.RemoveHandler(s_backColorEvent, value);
    }

    public event EventHandler? ForeColorChanged
    {
        add => Events.AddHandler(s_foreColorEvent, value);
        remove => Events.RemoveHandler(s_foreColorEvent, value);
    }

    public event EventHandler? FontChanged
    {
        add => Events.AddHandler(s_fontEvent, value);
        remove => Events.RemoveHandler(s_fontEvent, value);
    }

    public event EventHandler? RightToLeftChanged
    {
        add => Events.AddHandler(s_rightToLeftEvent, value);
        remove => Events.RemoveHandler(s_rightToLeftEvent, value);
    }

    private static readonly object s_backColorEvent = new();
    private static readonly object s_foreColorEvent = new();
    private static readonly object s_fontEvent = new();
    private static readonly object s_rightToLeftEvent = new();

    private Image? _backgroundImage;
    private ImageLayout _backgroundImageLayout = ImageLayout.Tile;

    [DefaultValue(null)]
    [Localizable(true)]
    public virtual Image? BackgroundImage
    {
        get => _backgroundImage;
        set
        {
            if (value == _backgroundImage) return;

            _backgroundImage = value;
            OnBackgroundImageChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(ImageLayout.Tile)]
    [Localizable(true)]
    public virtual ImageLayout BackgroundImageLayout
    {
        get => _backgroundImageLayout;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ImageLayout));

            if (value == _backgroundImageLayout) return;

            _backgroundImageLayout = value;

            // Only a zoomed or stretched image depends on the size.
            if (value is ImageLayout.Zoom or ImageLayout.Stretch or ImageLayout.Center) SetStyle(ControlStyles.ResizeRedraw, true);

            OnBackgroundImageLayoutChanged(EventArgs.Empty);
        }
    }

    protected virtual void OnBackgroundImageChanged(EventArgs e)
    {
        if (IsDisposingInHierarchy()) return;

        Invalidate();
        ((EventHandler?)Events[s_backgroundImageEvent])?.Invoke(this, e);

        foreach (var child in ChildrenSnapshot()) child.OnParentBackgroundImageChanged(e);
    }

    protected virtual void OnBackgroundImageLayoutChanged(EventArgs e)
    {
        if (IsDisposingInHierarchy()) return;

        Invalidate();
        ((EventHandler?)Events[s_backgroundImageLayoutEvent])?.Invoke(this, e);
    }

    /// <summary>A child with a transparent background shows the parent's image through it.</summary>
    protected virtual void OnParentBackgroundImageChanged(EventArgs e)
    {
        if (BackColor.A < 255) Invalidate();
    }

    public event EventHandler? BackgroundImageChanged
    {
        add => Events.AddHandler(s_backgroundImageEvent, value);
        remove => Events.RemoveHandler(s_backgroundImageEvent, value);
    }

    public event EventHandler? BackgroundImageLayoutChanged
    {
        add => Events.AddHandler(s_backgroundImageLayoutEvent, value);
        remove => Events.RemoveHandler(s_backgroundImageLayoutEvent, value);
    }

    private static readonly object s_backgroundImageEvent = new();
    private static readonly object s_backgroundImageLayoutEvent = new();

    /// <summary>Visible when the control and every ancestor are; a top-level control answers for itself.</summary>
    [Localizable(true)]
    public bool Visible
    {
        get => GetState(States.Visible) && (_parent is null || GetTopLevel() || _parent.Visible);
        set => SetVisibleCore(value);
    }

    public void Show() => Visible = true;

    public void Hide() => Visible = false;

    /// <summary>Starts hidden without raising anything: for constructors, where virtual calls are unsafe.</summary>
    internal void InitializeHidden() => SetState(States.Visible, false);

    /// <summary>The control's own visibility, whatever its parent's.</summary>
    internal bool DesiredVisibility => GetState(States.Visible);

    /// <summary>
    /// Shows or hides the control. VisibleChanged is raised when what the
    /// user sees changes; a hidden child of a hidden parent changes nothing
    /// visible, but its parent still lays out without it.
    /// </summary>
    protected virtual void SetVisibleCore(bool value)
    {
        if (value == GetState(States.Visible)) return;

        // A control being hidden passes the focus on first, as Windows takes it from a hidden window.
        if (!value) SelectNextIfFocused();

        var wasVisible = Visible;
        SetState(States.Visible, value);

        if (value && _parent?.Created == true) CreateControl();

        if (wasVisible != Visible) OnVisibleChanged(EventArgs.Empty);

        _parent?.DoLayout(this, PropertyNames.Visible);
        _parent?.Invalidate(Bounds, true);
    }

    protected virtual void OnVisibleChanged(EventArgs e)
    {
        var visible = Visible;

        if (visible && _parent is not null && !Created && !IsDisposingInHierarchy()) CreateControl();

        ((EventHandler?)Events[s_visibleEvent])?.Invoke(this, e);

        foreach (var child in ChildrenSnapshot())
        {
            if (child.DesiredVisibility) child.OnParentVisibleChanged(e);
        }
    }

    protected virtual void OnParentVisibleChanged(EventArgs e)
    {
        if (DesiredVisibility) OnVisibleChanged(e);
    }

    public event EventHandler? VisibleChanged
    {
        add => Events.AddHandler(s_visibleEvent, value);
        remove => Events.RemoveHandler(s_visibleEvent, value);
    }

    /// <summary>Enabled when the control and every ancestor are.</summary>
    [Localizable(true)]
    [DefaultValue(true)]
    public bool Enabled
    {
        get => GetState(States.Enabled) && (_parent is null || _parent.Enabled);
        set
        {
            var old = Enabled;
            if (!value && old) SelectNextIfFocused();

            SetState(States.Enabled, value);
            if (old == Enabled) return;

            OnEnabledChanged(EventArgs.Empty);
        }
    }

    protected virtual void OnEnabledChanged(EventArgs e)
    {
        if (IsDisposingInHierarchy()) return;

        // Native-style controls are also painted into our managed canvas.
        // Their cached picture must change when their enabled state changes.
        Invalidate();

        ((EventHandler?)Events[s_enabledEvent])?.Invoke(this, e);

        foreach (var child in ChildrenSnapshot()) child.OnParentEnabledChanged(e);
    }

    protected virtual void OnParentEnabledChanged(EventArgs e)
    {
        if (GetState(States.Enabled)) OnEnabledChanged(e);
    }

    public event EventHandler? EnabledChanged
    {
        add => Events.AddHandler(s_enabledEvent, value);
        remove => Events.RemoveHandler(s_enabledEvent, value);
    }

    private static readonly object s_visibleEvent = new();
    private static readonly object s_enabledEvent = new();

    private Control[] ChildrenSnapshot() => _controls?.Snapshot() ?? [];

    protected virtual void OnTextChanged(EventArgs e) => ((EventHandler?)Events[s_textEvent])?.Invoke(this, e);

    public event EventHandler? TextChanged
    {
        add => Events.AddHandler(s_textEvent, value);
        remove => Events.RemoveHandler(s_textEvent, value);
    }

    private static readonly object s_textEvent = new();

    public virtual void ResetText() => Text = string.Empty;

    protected virtual void OnStyleChanged(EventArgs e) => ((EventHandler?)Events[s_styleEvent])?.Invoke(this, e);

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public event EventHandler? StyleChanged
    {
        add => Events.AddHandler(s_styleEvent, value);
        remove => Events.RemoveHandler(s_styleEvent, value);
    }

    private static readonly object s_styleEvent = new();
}
