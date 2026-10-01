using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>
/// Small yellow windows that explain a control when the pointer rests on it.
/// </summary>
/// <remarks>
/// The timing is WinForms': the tip appears after InitialDelay of rest,
/// disappears after AutoPopDelay, on a press or when the pointer leaves, and
/// moving to another control with a tip shows that one after the shorter
/// ReshowDelay. The tip is a popup window, so it may extend beyond the form.
/// </remarks>
[ProvideProperty("ToolTip", typeof(Control))]
[DefaultEvent("Popup")]
[ToolboxItemFilter("System.Windows.Forms")]
public class ToolTip : Component, IExtenderProvider
{
    private const int DefaultAutomaticDelay = 500;
    private const int TextPadding = 3;

    private readonly Dictionary<Control, string> _texts = [];
    private readonly Timer _showTimer = new();
    private readonly Timer _hideTimer = new();

    private int _initialDelay = DefaultAutomaticDelay;
    private int _autoPopDelay = DefaultAutomaticDelay * 10;
    private int _reshowDelay = DefaultAutomaticDelay / 5;
    private int _automaticDelay = DefaultAutomaticDelay;
    private bool _active = true;
    private Color _backColor = SystemColors.Info;
    private Color _foreColor = SystemColors.InfoText;
    private string _title = string.Empty;

    private TipWindow? _window;
    private Control? _pending;
    private DateTime _lastHidden = DateTime.MinValue;

    public ToolTip()
    {
        _showTimer.Tick += (_, _) => ShowPending();
        _hideTimer.Tick += (_, _) => HideTip();
    }

    public ToolTip(IContainer cont)
        : this()
    {
        ArgumentNullException.ThrowIfNull(cont);

        cont.Add(this);
    }

    ~ToolTip() => Dispose(false);

    /// <summary>Whether tips are shown at all.</summary>
    [DefaultValue(true)]
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            if (!value) HideTip();
        }
    }

    /// <summary>Sets the three delays at once from one value, the way WinForms derives them.</summary>
    [DefaultValue(DefaultAutomaticDelay)]
    [RefreshProperties(RefreshProperties.All)]
    public int AutomaticDelay
    {
        get => _automaticDelay;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            _automaticDelay = value;
            _initialDelay = value;
            _autoPopDelay = value * 10;
            _reshowDelay = value / 5;
        }
    }

    [RefreshProperties(RefreshProperties.All)]
    public int InitialDelay
    {
        get => _initialDelay;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            _initialDelay = value;
        }
    }

    [RefreshProperties(RefreshProperties.All)]
    public int AutoPopDelay
    {
        get => _autoPopDelay;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            _autoPopDelay = value;
        }
    }

    [RefreshProperties(RefreshProperties.All)]
    public int ReshowDelay
    {
        get => _reshowDelay;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            _reshowDelay = value;
        }
    }

    public Color BackColor
    {
        get => _backColor;
        set => _backColor = value;
    }

    public Color ForeColor
    {
        get => _foreColor;
        set
        {
            if (value.IsEmpty) throw new ArgumentException("Color.Empty is not a valid ForeColor for a ToolTip.", nameof(value));

            _foreColor = value;
        }
    }

    /// <summary>A balloon is drawn as the classic rectangle; the shape of the bubble is not reproduced.</summary>
    [DefaultValue(false)]
    public bool IsBalloon { get; set; }

    [DefaultValue(false)]
    public bool OwnerDraw { get; set; }

    /// <summary>Shows tips even when the control's form is not the active window.</summary>
    [DefaultValue(false)]
    public bool ShowAlways { get; set; }

    [DefaultValue(false)]
    public bool StripAmpersands { get; set; }

    [DefaultValue(null)]
    [TypeConverter(typeof(StringConverter))]
    public object? Tag { get; set; }

    [DefaultValue(ToolTipIcon.None)]
    public ToolTipIcon ToolTipIcon { get; set; }

    [DefaultValue("")]
    public string ToolTipTitle
    {
        get => _title;
        set => _title = value ?? string.Empty;
    }

    /// <summary>Accepted without effect: tips appear at once, as with animations turned off.</summary>
    [DefaultValue(true)]
    public bool UseAnimation { get; set; } = true;

    [DefaultValue(true)]
    public bool UseFading { get; set; } = true;

    /// <summary>The window style of the tip; there is no native tip window, so nothing reads them.</summary>
    protected virtual CreateParams CreateParams => new() { ClassName = "tooltips_class32" };

    public event PopupEventHandler? Popup;

    public event DrawToolTipEventHandler? Draw;

    public bool CanExtend(object target) => target is Control;

    [DefaultValue("")]
    [Localizable(true)]
    public string GetToolTip(Control? control) => control is not null && _texts.TryGetValue(control, out var text) ? text : string.Empty;

    /// <summary>Gives the control a tip; an empty caption removes it.</summary>
    public void SetToolTip(Control control, string? caption)
    {
        ArgumentNullException.ThrowIfNull(control);

        var had = _texts.ContainsKey(control);

        if (string.IsNullOrEmpty(caption))
        {
            if (had) Unhook(control);

            // The tip on screen belonged to it: with its handlers gone nothing else would hide it.
            if (_window?.AssociatedControl == control || _pending == control)
            {
                _pending = null;
                _showTimer.Stop();
                HideTip();
            }

            _texts.Remove(control);
            return;
        }

        _texts[control] = caption;
        if (!had) Hook(control);
    }

    public void RemoveAll()
    {
        foreach (var control in _texts.Keys.ToArray()) Unhook(control);

        _texts.Clear();
        HideTip();
    }

    public void Show(string? text, IWin32Window window) => Show(text, window, 0);

    public void Show(string? text, IWin32Window window, int duration) =>
        ShowAt(text, window, PointBelowPointer(window), duration);

    public void Show(string? text, IWin32Window window, Point point) => Show(text, window, point, 0);

    /// <summary>Shows the tip at a point in the window's client coordinates, for the duration (zero: until hidden).</summary>
    public void Show(string? text, IWin32Window window, Point point, int duration)
    {
        ArgumentNullException.ThrowIfNull(window);

        var control = window as Control ?? Control.FromHandle(window.Handle);
        var screen = control is null ? point : control.PointToScreen(point);
        ShowAt(text, window, screen, duration);
    }

    public void Show(string? text, IWin32Window window, int x, int y) => Show(text, window, new Point(x, y), 0);

    public void Show(string? text, IWin32Window window, int x, int y, int duration) => Show(text, window, new Point(x, y), duration);

    /// <summary>Hides the tip shown for the window, if the one on screen is for it.</summary>
    public void Hide(IWin32Window win)
    {
        ArgumentNullException.ThrowIfNull(win);

        if (_window is { } window && (window.AssociatedWindow == win || window.AssociatedControl == win)) HideTip();
    }

    protected void StopTimer()
    {
        _showTimer.Stop();
        _hideTimer.Stop();
    }

    public override string ToString() => $"{base.ToString()} InitialDelay: {InitialDelay}, ShowAlways: {ShowAlways}";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RemoveAll();
            _showTimer.Dispose();
            _hideTimer.Dispose();
            _window?.Dispose();
            _window = null;
        }

        base.Dispose(disposing);
    }

    private void Hook(Control control)
    {
        control.MouseEnter += OnMouseEnter;
        control.MouseLeave += OnMouseLeave;
        control.MouseDown += OnMouseDown;
        control.HandleDestroyed += OnControlGone;
    }

    private void Unhook(Control control)
    {
        control.MouseEnter -= OnMouseEnter;
        control.MouseLeave -= OnMouseLeave;
        control.MouseDown -= OnMouseDown;
        control.HandleDestroyed -= OnControlGone;
    }

    /// <summary>Resting on a control starts the wait; right after another tip, the shorter one.</summary>
    private void OnMouseEnter(object? sender, EventArgs e)
    {
        if (sender is not Control control || !_active) return;

        _pending = control;
        // Moving from a shown tip to another control shows the next one sooner, as the tool tip control does.
        var justShown = _window?.IsShowing == true || (DateTime.UtcNow - _lastHidden).TotalMilliseconds < _reshowDelay;
        if (_window?.IsShowing == true) HideTip();

        _showTimer.Stop();
        _showTimer.Interval = Math.Max(justShown ? _reshowDelay : _initialDelay, 1);
        _showTimer.Start();
    }

    private void OnMouseLeave(object? sender, EventArgs e)
    {
        if (sender == _pending) _pending = null;

        _showTimer.Stop();
        HideTip();
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        _showTimer.Stop();
        HideTip();
    }

    private void OnControlGone(object? sender, EventArgs e)
    {
        if (sender == _pending) HideTip();
    }

    private void ShowPending()
    {
        _showTimer.Stop();

        if (_pending is not { } control || !_texts.TryGetValue(control, out var text)) return;

        // A tip shows over the active window only, unless asked to always show.
        if (!ShowAlways && control.FindForm() is { } form && Form.ActiveForm != form) return;

        ShowAt(text, control, PointBelowPointer(control), _autoPopDelay);
    }

    /// <summary>Below the pointer, as Windows places a tip: the height of the cursor down.</summary>
    private static Point PointBelowPointer(IWin32Window window)
    {
        var pointer = Control.MousePosition;

        return new Point(pointer.X, pointer.Y + SystemInformation.CursorSize.Height * 2 / 3);
    }

    private void ShowAt(string? text, IWin32Window window, Point screen, int duration)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (StripAmpersands) text = text.Replace("&&", "\u0001").Replace("&", string.Empty).Replace("\u0001", "&");

        var control = window as Control ?? Control.FromHandle(window.Handle);
        var font = SystemFonts.StatusFont ?? Control.DefaultFont;
        var size = Measure(text, font);

        var popup = new PopupEventArgs(window, control, IsBalloon, size);
        Popup?.Invoke(this, popup);
        if (popup.Cancel) return;

        _window ??= new TipWindow(this);
        _window.Configure(text, font, window, control);
        Popups.Show(_window, control, new Rectangle(screen, popup.ToolTipSize));

        _hideTimer.Stop();
        if (duration > 0)
        {
            _hideTimer.Interval = duration;
            _hideTimer.Start();
        }
    }

    private Size Measure(string text, Font font)
    {
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var textSize = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), flags);
        var width = textSize.Width;
        var height = textSize.Height;

        if (_title.Length > 0)
        {
            using var bold = new Font(font, FontStyle.Bold);
            var titleSize = TextRenderer.MeasureText(_title, bold, new Size(int.MaxValue, int.MaxValue), flags);
            width = Math.Max(width, titleSize.Width + (ToolTipIcon == ToolTipIcon.None ? 0 : 20));
            height += titleSize.Height + TextPadding;
        }

        return new Size(width + 2 * TextPadding + 2, height + 2 * TextPadding + 2);
    }

    private void HideTip()
    {
        _hideTimer.Stop();

        if (_window?.IsShowing != true) return;

        Popups.Close(_window);
        _lastHidden = DateTime.UtcNow;
    }

    /// <summary>The tip's popup: the Info colour, a one-pixel frame, the title and the text.</summary>
    private sealed class TipWindow : Control
    {
        private readonly ToolTip _owner;
        private string _text = string.Empty;
        private Font _font = Control.DefaultFont;
        private IWin32Window? _associatedWindow;
        private Control? _associatedControl;

        public TipWindow(ToolTip owner)
        {
            _owner = owner;
            SetTopLevel(true);
        }

        public bool IsShowing => Popups.All.Contains(this);

        public IWin32Window? AssociatedWindow => _associatedWindow;

        public Control? AssociatedControl => _associatedControl;

        internal override bool ClosesOnOutsideClick => false;

        public void Configure(string text, Font font, IWin32Window window, Control? control)
        {
            _text = text;
            _font = font;
            _associatedWindow = window;
            _associatedControl = control;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var bounds = ClientRectangle;

            if (_owner.OwnerDraw)
            {
                _owner.Draw?.Invoke(_owner, new DrawToolTipEventArgs(e.Graphics, _associatedWindow, _associatedControl, bounds, _text, _owner.BackColor, _owner.ForeColor, _font));
                return;
            }

            var arguments = new DrawToolTipEventArgs(e.Graphics, _associatedWindow, _associatedControl, bounds, _text, _owner.BackColor, _owner.ForeColor, _font);
            arguments.DrawBackground();
            arguments.DrawBorder();

            var textBounds = Rectangle.Inflate(bounds, -TextPadding - 1, -TextPadding - 1);

            if (_owner.ToolTipTitle.Length > 0)
            {
                using var bold = new Font(_font, FontStyle.Bold);
                var titleHeight = TextRenderer.MeasureText(_owner.ToolTipTitle, bold).Height;
                TextRenderer.DrawText(e.Graphics, _owner.ToolTipTitle, bold, new Rectangle(textBounds.Location, new Size(textBounds.Width, titleHeight)), _owner.ForeColor, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                textBounds = Rectangle.FromLTRB(textBounds.Left, textBounds.Top + titleHeight + TextPadding, textBounds.Right, textBounds.Bottom);
            }

            TextRenderer.DrawText(e.Graphics, _text, _font, textBounds, _owner.ForeColor, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }
}
