using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Shows a small blinking icon beside a control that has a problem, and the
/// problem's text as a tooltip over the icon.
/// </summary>
/// <remarks>
/// Microsoft's ErrorProvider paints its icon in a native sibling window (its
/// "ErrorWindow") positioned next to the control, outside the control's own
/// bounds, so the control never has to make room for it or paint it itself.
/// We do the same with an ordinary Control: <see cref="ErrorIconControl"/> is
/// added to the target's parent, next to the target, and is kept in sync with
/// it on every move, resize, visibility and parent change.
/// </remarks>
[ProvideProperty("IconPadding", typeof(Control))]
[ProvideProperty("IconAlignment", typeof(Control))]
[ProvideProperty("Error", typeof(Control))]
[ComplexBindingProperties("DataSource", "DataMember")]
public class ErrorProvider : Component, IExtenderProvider, ISupportInitialize
{
    /// <summary>
    /// How many times the icon toggles before settling visible: an even
    /// count, so the last toggle always leaves it shown.
    /// </summary>
    private const int BlinkPhaseCount = 6;

    private readonly Dictionary<Control, ErrorItem> _items = [];
    private readonly Timer _blinkTimer = new();
    private readonly ToolTip _toolTip = new();

    private int _blinkRate = 250;
    private ErrorBlinkStyle _blinkStyle = ErrorBlinkStyle.BlinkIfDifferentError;
    private ContainerControl? _containerControl;
    private Icon _icon = new(Drawing.SystemIcons.Error, 16, 16);
    private bool _rightToLeft;
    private object? _dataSource;
    private string? _dataMember;
    private BindingManagerBase? _currentManager;
    private bool _initializing;

    public ErrorProvider()
    {
        _blinkTimer.Tick += OnBlinkTick;
    }

    public ErrorProvider(ContainerControl parentControl)
        : this()
    {
        ArgumentNullException.ThrowIfNull(parentControl);

        ContainerControl = parentControl;
    }

    public ErrorProvider(IContainer container)
        : this()
    {
        ArgumentNullException.ThrowIfNull(container);

        container.Add(this);
    }

    [DefaultValue(250)]
    public int BlinkRate
    {
        get => _blinkRate;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            _blinkRate = value;

            // A rate of zero means the icon never blinks, as WinForms turns it into NeverBlink.
            if (value == 0) BlinkStyle = ErrorBlinkStyle.NeverBlink;
            if (_blinkTimer.Enabled) _blinkTimer.Interval = Math.Max(value, 1);
        }
    }

    [DefaultValue(ErrorBlinkStyle.BlinkIfDifferentError)]
    public ErrorBlinkStyle BlinkStyle
    {
        get => _blinkStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ErrorBlinkStyle));

            // With a zero rate there is nothing to blink at.
            if (_blinkRate == 0) value = ErrorBlinkStyle.NeverBlink;
            if (_blinkStyle == value) return;

            _blinkStyle = value;

            // Never blinking settles every icon on screen at once, as switching the style away from blinking does.
            if (value != ErrorBlinkStyle.NeverBlink) return;

            foreach (var item in _items.Values)
            {
                item.BlinkPhasesRemaining = 0;
                if (item.IconControl is { } icon) icon.Visible = true;
            }
        }
    }

    [DefaultValue(null)]
    public ContainerControl? ContainerControl
    {
        get => _containerControl;
        set
        {
            if (_containerControl == value) return;

            if (_containerControl is { } previous) previous.BindingContextChanged -= OnContainerBindingContextChanged;

            _containerControl = value;
            if (value is { } current) current.BindingContextChanged += OnContainerBindingContextChanged;

            UpdateBinding();
        }
    }

    [DefaultValue(null)]
    public string? DataMember
    {
        get => _dataMember;
        set => BindToDataAndErrors(_dataSource, value);
    }

    [DefaultValue(null)]
    public object? DataSource
    {
        get => _dataSource;
        set => BindToDataAndErrors(value, _dataMember);
    }

    public bool HasErrors => _items.Values.Any(item => item.Error.Length > 0);

    [Localizable(true)]
    public Icon Icon
    {
        get => _icon;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_icon == value) return;

            _icon = value;
            foreach (var (control, item) in _items)
            {
                if (item.IconControl is not { } icon) continue;

                icon.Icon = value;
                icon.Size = value.Size;
                RepositionIcon(control, item);
            }
        }
    }

    [DefaultValue(false)]
    public virtual bool RightToLeft
    {
        get => _rightToLeft;
        set
        {
            if (_rightToLeft == value) return;

            _rightToLeft = value;
            OnRightToLeftChanged(EventArgs.Empty);

            foreach (var (control, item) in _items) RepositionIcon(control, item);
        }
    }

    public override ISite? Site
    {
        set => base.Site = value;
    }

    [DefaultValue(null)]
    public object? Tag { get; set; }

    public event EventHandler? RightToLeftChanged;

    public void BindToDataAndErrors(object? newDataSource, string? newDataMember)
    {
        if (ReferenceEquals(_dataSource, newDataSource) && string.Equals(_dataMember, newDataMember, StringComparison.Ordinal)) return;

        _dataSource = newDataSource;
        _dataMember = newDataMember;
        UpdateBinding();
    }

    public bool CanExtend(object? extendee) => extendee is Control;

    /// <summary>Removes every error, icon and extender setting this provider is tracking.</summary>
    public void Clear()
    {
        foreach (var (control, item) in _items.ToArray())
        {
            RemoveIconControl(item);
            UnhookControl(control);
        }

        _items.Clear();
    }

    [DefaultValue("")]
    [Localizable(true)]
    public string GetError(Control control) => _items.TryGetValue(NotNull(control), out var item) ? item.Error : string.Empty;

    /// <summary>Whether the icon beside the control is on screen right now: tests read the blink state through this, not through a real timer.</summary>
    internal bool IsIconVisible(Control control) => _items.TryGetValue(control, out var item) && item.IconControl is { Visible: true };

    /// <summary>Runs one blink step without waiting for the real timer, so tests stay deterministic.</summary>
    internal void SimulateBlinkTick() => OnBlinkTick(this, EventArgs.Empty);

    [DefaultValue(ErrorIconAlignment.MiddleRight)]
    [Localizable(true)]
    public ErrorIconAlignment GetIconAlignment(Control control) => _items.TryGetValue(NotNull(control), out var item) ? item.Alignment : ErrorIconAlignment.MiddleRight;

    [DefaultValue(0)]
    [Localizable(true)]
    public int GetIconPadding(Control control) => _items.TryGetValue(NotNull(control), out var item) ? item.Padding : 0;

    protected virtual void OnRightToLeftChanged(EventArgs e) => RightToLeftChanged?.Invoke(this, e);

    public void SetError(Control control, string? value)
    {
        var item = EnsureItem(control);
        value ??= string.Empty;

        var changed = !string.Equals(item.Error, value, StringComparison.Ordinal);
        item.Error = value;

        if (value.Length == 0)
        {
            RemoveIconControl(item);
            return;
        }

        var icon = EnsureIconControl(control, item);
        icon.Visible = control.Visible;
        _toolTip.SetToolTip(icon, value);
        RepositionIcon(control, item);

        var shouldBlink = _blinkStyle == ErrorBlinkStyle.AlwaysBlink || (_blinkStyle == ErrorBlinkStyle.BlinkIfDifferentError && changed);
        if (shouldBlink) StartBlink(item);
    }

    public void SetIconAlignment(Control control, ErrorIconAlignment value)
    {
        if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ErrorIconAlignment));

        var item = EnsureItem(control);
        if (item.Alignment == value) return;

        item.Alignment = value;
        RepositionIcon(control, item);
    }

    public void SetIconPadding(Control control, int padding)
    {
        var item = EnsureItem(control);
        if (item.Padding == padding) return;

        item.Padding = padding;
        RepositionIcon(control, item);
    }

    /// <summary>
    /// Re-reads the current item's errors through <see cref="IDataErrorInfo"/>
    /// and sets them on every control bound to one of its properties.
    /// </summary>
    public void UpdateBinding()
    {
        if (_initializing) return;

        if (_currentManager is { } previous)
        {
            previous.CurrentChanged -= OnCurrentChanged;
            previous.CurrentItemChanged -= OnCurrentChanged;
            _currentManager = null;
        }

        if (_dataSource is null || _containerControl is null) return;

        // ContainerControl.BindingContext never returns null: it creates one the first time it is asked.
        var context = _containerControl.BindingContext!;
        var manager = context[_dataSource, _dataMember ?? string.Empty];
        _currentManager = manager;
        manager.CurrentChanged += OnCurrentChanged;
        manager.CurrentItemChanged += OnCurrentChanged;

        UpdateErrorsFromCurrentItem();
    }

    void ISupportInitialize.BeginInit() => _initializing = true;

    void ISupportInitialize.EndInit()
    {
        _initializing = false;
        UpdateBinding();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Clear();

            if (_currentManager is { } manager)
            {
                manager.CurrentChanged -= OnCurrentChanged;
                manager.CurrentItemChanged -= OnCurrentChanged;
            }

            if (_containerControl is { } container) container.BindingContextChanged -= OnContainerBindingContextChanged;

            _blinkTimer.Dispose();
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Control NotNull(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return control;
    }

    private ErrorItem EnsureItem(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (_items.TryGetValue(control, out var existing)) return existing;

        var item = new ErrorItem();
        _items[control] = item;
        HookControl(control);

        return item;
    }

    private void HookControl(Control control)
    {
        control.LocationChanged += OnControlMovedOrResized;
        control.SizeChanged += OnControlMovedOrResized;
        control.VisibleChanged += OnControlVisibleChanged;
        control.ParentChanged += OnControlParentChanged;
        control.Disposed += OnControlDisposed;
    }

    private void UnhookControl(Control control)
    {
        control.LocationChanged -= OnControlMovedOrResized;
        control.SizeChanged -= OnControlMovedOrResized;
        control.VisibleChanged -= OnControlVisibleChanged;
        control.ParentChanged -= OnControlParentChanged;
        control.Disposed -= OnControlDisposed;
    }

    private void OnControlMovedOrResized(object? sender, EventArgs e)
    {
        if (sender is Control control && _items.TryGetValue(control, out var item)) RepositionIcon(control, item);
    }

    private void OnControlVisibleChanged(object? sender, EventArgs e)
    {
        if (sender is not Control control || !_items.TryGetValue(control, out var item) || item.IconControl is not { } icon) return;

        icon.Visible = control.Visible && item.Error.Length > 0;
    }

    /// <summary>The icon follows the control to its new parent, or disappears if it has none.</summary>
    private void OnControlParentChanged(object? sender, EventArgs e)
    {
        if (sender is not Control control || !_items.TryGetValue(control, out var item)) return;

        if (item.IconControl is not { } icon)
        {
            if (item.Error.Length > 0) EnsureIconControl(control, item);
            return;
        }

        icon.Parent?.Controls.Remove(icon);

        if (control.Parent is { } parent)
        {
            parent.Controls.Add(icon);
            icon.BringToFront();
            RepositionIcon(control, item);
        }
    }

    private void OnControlDisposed(object? sender, EventArgs e)
    {
        if (sender is not Control control) return;

        if (_items.TryGetValue(control, out var item))
        {
            RemoveIconControl(item);
            _items.Remove(control);
        }

        UnhookControl(control);
    }

    private ErrorIconControl EnsureIconControl(Control control, ErrorItem item)
    {
        if (item.IconControl is { } existing) return existing;

        var icon = new ErrorIconControl(_icon) { Visible = control.Visible };
        item.IconControl = icon;

        if (control.Parent is { } parent)
        {
            parent.Controls.Add(icon);
            icon.BringToFront();
        }

        RepositionIcon(control, item);

        return icon;
    }

    private void RemoveIconControl(ErrorItem item)
    {
        if (item.IconControl is not { } icon) return;

        _toolTip.SetToolTip(icon, null);
        icon.Parent?.Controls.Remove(icon);
        icon.Dispose();
        item.IconControl = null;
        item.BlinkPhasesRemaining = 0;
    }

    private void RepositionIcon(Control control, ErrorItem item)
    {
        if (item.IconControl is not { } icon) return;

        icon.Bounds = IconBounds(control, item);
    }

    /// <summary>Where the icon sits in the parent's coordinates: beside the control, by alignment and padding.</summary>
    private Rectangle IconBounds(Control control, ErrorItem item)
    {
        var size = item.IconControl?.Size ?? _icon.Size;
        var alignment = EffectiveAlignment(item.Alignment);

        var x = alignment is ErrorIconAlignment.TopLeft or ErrorIconAlignment.MiddleLeft or ErrorIconAlignment.BottomLeft
            ? control.Left - size.Width - item.Padding
            : control.Right + item.Padding;

        var y = alignment switch
        {
            ErrorIconAlignment.TopLeft or ErrorIconAlignment.TopRight => control.Top + item.Padding,
            ErrorIconAlignment.BottomLeft or ErrorIconAlignment.BottomRight => control.Bottom - size.Height - item.Padding,
            _ => control.Top + (control.Height - size.Height) / 2,
        };

        return new Rectangle(x, y, size.Width, size.Height);
    }

    /// <summary>RightToLeft mirrors Left and Right, as it mirrors every other side-by-side layout.</summary>
    private ErrorIconAlignment EffectiveAlignment(ErrorIconAlignment alignment)
    {
        if (!_rightToLeft) return alignment;

        return alignment switch
        {
            ErrorIconAlignment.TopLeft => ErrorIconAlignment.TopRight,
            ErrorIconAlignment.TopRight => ErrorIconAlignment.TopLeft,
            ErrorIconAlignment.MiddleLeft => ErrorIconAlignment.MiddleRight,
            ErrorIconAlignment.MiddleRight => ErrorIconAlignment.MiddleLeft,
            ErrorIconAlignment.BottomLeft => ErrorIconAlignment.BottomRight,
            ErrorIconAlignment.BottomRight => ErrorIconAlignment.BottomLeft,
            _ => alignment,
        };
    }

    private void StartBlink(ErrorItem item)
    {
        if (_blinkStyle == ErrorBlinkStyle.NeverBlink || item.IconControl is not { } icon) return;

        item.BlinkPhasesRemaining = BlinkPhaseCount;
        icon.Visible = true;

        if (_blinkTimer.Enabled) return;

        _blinkTimer.Interval = Math.Max(_blinkRate, 1);
        _blinkTimer.Start();
    }

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        var anyBlinking = false;

        foreach (var item in _items.Values)
        {
            if (item.BlinkPhasesRemaining <= 0 || item.IconControl is not { } icon) continue;

            item.BlinkPhasesRemaining--;
            icon.Visible = item.BlinkPhasesRemaining % 2 == 0;

            if (item.BlinkPhasesRemaining > 0) anyBlinking = true;
        }

        if (!anyBlinking) _blinkTimer.Stop();
    }

    private void OnContainerBindingContextChanged(object? sender, EventArgs e) => UpdateBinding();

    private void OnCurrentChanged(object? sender, EventArgs e) => UpdateErrorsFromCurrentItem();

    private void UpdateErrorsFromCurrentItem()
    {
        if (_currentManager is not { } manager || _containerControl is null) return;

        var current = manager.Current;

        foreach (var control in Descendants(_containerControl))
        {
            foreach (Binding binding in control.DataBindings)
            {
                if (binding.BindingManagerBase != manager) continue;

                var field = binding.BindingMemberInfo.BindingField;
                if (field.Length == 0) continue;

                var error = current is IDataErrorInfo errorInfo ? errorInfo[field] ?? string.Empty : string.Empty;
                SetError(control, error);
            }
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }

    /// <summary>What this provider tracks for one extended control.</summary>
    private sealed class ErrorItem
    {
        public string Error = string.Empty;
        public ErrorIconAlignment Alignment = ErrorIconAlignment.MiddleRight;
        public int Padding;
        public ErrorIconControl? IconControl;
        public int BlinkPhasesRemaining;
    }

    /// <summary>
    /// The sibling control that paints the error icon next to its target.
    /// Never selectable: it only shows the icon and, through the owner's
    /// ToolTip, the error text on hover.
    /// </summary>
    private sealed class ErrorIconControl : Control
    {
        private Icon _icon;

        // Painting happens on a Skia picture that is replayed later, not drawn
        // at once: DrawImage's SKImage reads this bitmap's pixels by reference,
        // not by copy, so the bitmap has to outlive the OnPaint call that drew
        // it, not just last until it returns. A per-paint bitmap, disposed as
        // soon as OnPaint is done, left the icon reading freed memory on the
        // later replay: this field, disposed only when the icon control itself
        // is, is what keeps the picture's pixels valid.
        private Bitmap _bitmap;

        public ErrorIconControl(Icon icon)
        {
            _icon = icon;
            _bitmap = icon.ToBitmap();

            SetStyle(ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
            base.TabStop = false;
            BackColor = Color.Transparent;
            Size = icon.Size;
        }

        public Icon Icon
        {
            get => _icon;
            set
            {
                _icon = value;

                var previous = _bitmap;
                _bitmap = value.ToBitmap();
                previous.Dispose();

                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImage(_bitmap, new Rectangle(Point.Empty, Size));

            base.OnPaint(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _bitmap.Dispose();

            base.Dispose(disposing);
        }
    }
}
