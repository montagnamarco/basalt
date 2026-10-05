using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// Scaling a form: the legacy AutoScale/AutoScaleBaseSize pair .NET 1.1
/// designers emitted, centering on the screen or the owner, minimum/maximum
/// size change notifications, and reacting to the window moving to a monitor
/// with another dpi.
/// </summary>
public partial class Form
{
    private bool _autoScale = true;
    private Size _autoScaleBaseSize;

    /// <summary>The dpi the window was last told about: unset until InitializeHostScaling records the host's real one.</summary>
    private int _lastDpi;

    /// <summary>Whether _lastDpi is the host's own scaling yet, rather than this field's default.</summary>
    private bool _dpiInitialized;

    /// <summary>
    /// Whether <see cref="ApplyAutoScaling"/> does anything: the .NET 1.1 way
    /// to scale a form, superseded by <see cref="ContainerControl.AutoScaleMode"/>
    /// but still read by old designer-generated InitializeComponent methods.
    /// </summary>
    [Obsolete("This property has been deprecated. Use the AutoScaleMode property instead.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AutoScale
    {
        get => _autoScale;
        set => _autoScale = value;
    }

    /// <summary>The average character size the form was designed at, for the same legacy scaling.</summary>
    [Obsolete("This property has been deprecated. Use the AutoScaleDimensions property instead.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public virtual Size AutoScaleBaseSize
    {
        get => _autoScaleBaseSize;
        set => _autoScaleBaseSize = value;
    }

    /// <summary>The size an average character of <paramref name="font"/> would need: what designed-at scaling compares against.</summary>
    public static SizeF GetAutoScaleSize(Font font)
    {
        ArgumentNullException.ThrowIfNull(font);

        return FontScaling.AverageCharacterSize(font);
    }

    /// <summary>Scales the form by the ratio between the base size it was designed at and the current font's.</summary>
    protected void ApplyAutoScaling()
    {
        if (!_autoScale || _autoScaleBaseSize.Width <= 0 || _autoScaleBaseSize.Height <= 0) return;

        var current = GetAutoScaleSize(Font);
        var factor = new SizeF(current.Width / _autoScaleBaseSize.Width, current.Height / _autoScaleBaseSize.Height);
        if (factor.Width != 1f || factor.Height != 1f) Scale(factor);

        _autoScaleBaseSize = Size.Round(current);
    }

    /// <summary>
    /// A top-level form does not scale its own position (Control.GetScaledBounds
    /// already keeps it when <see cref="Control.GetTopLevel"/> is true); a form
    /// otherwise scales exactly as any other control.
    /// </summary>
    protected override Rectangle GetScaledBounds(Rectangle bounds, SizeF factor, BoundsSpecified specified) =>
        base.GetScaledBounds(bounds, factor, specified);

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) => base.ScaleControl(factor, specified);

    [EditorBrowsable(EditorBrowsableState.Never)]
    protected override void ScaleCore(float dx, float dy) => base.ScaleCore(dx, dy);

    /// <summary>Moves the window to the middle of the screen it is (or would be) shown on.</summary>
    protected void CenterToScreen()
    {
        var area = Screen.GetWorkingArea(this);

        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
    }

    /// <summary>Moves the window to the middle of its owner (a top-level form) or its parent's client area.</summary>
    protected void CenterToParent()
    {
        if (GetTopLevel())
        {
            var ownerBounds = _owner?.Bounds ?? Screen.GetWorkingArea(this);
            Location = new Point(ownerBounds.Left + (ownerBounds.Width - Width) / 2, ownerBounds.Top + (ownerBounds.Height - Height) / 2);
            return;
        }

        var client = Parent?.ClientRectangle ?? new Rectangle(Point.Empty, Size);
        Location = new Point((client.Width - Width) / 2, (client.Height - Height) / 2);
    }

    /// <summary>The smallest size the window takes; raising it raises <see cref="MaximumSize"/> too (Control.MinimumSize already does that part).</summary>
    public override Size MinimumSize
    {
        get => base.MinimumSize;
        set
        {
            if (value == base.MinimumSize) return;

            base.MinimumSize = value;
            OnMinimumSizeChanged(EventArgs.Empty);
        }
    }

    /// <summary>The largest size the window takes, zero meaning none.</summary>
    public override Size MaximumSize
    {
        get => base.MaximumSize;
        set
        {
            if (value == base.MaximumSize) return;

            base.MaximumSize = value;
            OnMaximumSizeChanged(EventArgs.Empty);
        }
    }

    protected virtual void OnMinimumSizeChanged(EventArgs e) => ((EventHandler?)Events[s_minimumSizeEvent])?.Invoke(this, e);

    protected virtual void OnMaximumSizeChanged(EventArgs e) => ((EventHandler?)Events[s_maximumSizeEvent])?.Invoke(this, e);

    public event EventHandler? MinimumSizeChanged
    {
        add => Events.AddHandler(s_minimumSizeEvent, value);
        remove => Events.RemoveHandler(s_minimumSizeEvent, value);
    }

    public event EventHandler? MaximumSizeChanged
    {
        add => Events.AddHandler(s_maximumSizeEvent, value);
        remove => Events.RemoveHandler(s_maximumSizeEvent, value);
    }

    private static readonly object s_minimumSizeEvent = new();
    private static readonly object s_maximumSizeEvent = new();

    /// <summary>
    /// The window moved to a monitor with another dpi (or the first report of
    /// the one it opened on, which raises nothing: there is no old value to
    /// compare it with). With <see cref="AutoScaleMode.Dpi"/>, the children
    /// are rescaled by the ratio unless a handler cancels it.
    /// </summary>
    protected virtual void OnDpiChanged(DpiChangedEventArgs e) => ((DpiChangedEventHandler?)Events[s_dpiChangedEvent])?.Invoke(this, e);

    public event DpiChangedEventHandler? DpiChanged
    {
        add => Events.AddHandler(s_dpiChangedEvent, value);
        remove => Events.RemoveHandler(s_dpiChangedEvent, value);
    }

    private static readonly object s_dpiChangedEvent = new();

    /// <summary>
    /// Records the host's own scaling as the dpi the window opened at,
    /// without raising DpiChanged: there is no earlier value for a form that
    /// has only just been given a window to compare against, so this is not
    /// a change. Called once a top-level form creates its host.
    /// </summary>
    internal void InitializeHostScaling(double scaling)
    {
        _lastDpi = (int)Math.Round(scaling * 96.0);
        _dpiInitialized = true;
    }

    /// <summary>Called by the host window when Avalonia reports a new <c>DesktopScaling</c> for it.</summary>
    internal void OnHostScalingChanged(double scaling)
    {
        var newDpi = (int)Math.Round(scaling * 96.0);

        if (!_dpiInitialized)
        {
            InitializeHostScaling(scaling);
            return;
        }

        var oldDpi = _lastDpi;
        _lastDpi = newDpi;

        if (newDpi == oldDpi) return;

        // The host already scales the canvas. Bounds and fonts stay in 96-DPI
        // logical units; multiplying them again would scale the form twice.
        var suggested = Bounds;
        var e = new DpiChangedEventArgs(oldDpi, newDpi, suggested);

        OnDpiChanged(e);
    }
}
