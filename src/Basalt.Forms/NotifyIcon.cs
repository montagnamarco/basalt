using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal.Tray;

namespace System.Windows.Forms;

/// <summary>
/// An icon in the notification area (the system tray), with an optional
/// context menu and balloon tips.
/// </summary>
/// <remarks>
/// Avalonia's tray icon only reports a plain click, with no button or
/// position: Click and MouseClick(Left) are the only events a real tray icon
/// raises here. DoubleClick, MouseDoubleClick, MouseDown, MouseMove and
/// MouseUp exist because WinForms declares them, and a fake host can raise
/// them in tests, but a real tray icon on this platform never does.
/// </remarks>
[DefaultProperty("Text")]
[DefaultEvent("MouseDoubleClick")]
[ToolboxItemFilter("System.Windows.Forms")]
public sealed class NotifyIcon : Component
{
    private const int MaxTextLength = 127;

    private ITrayIconHost? _host;
    private Icon? _icon;
    private string _text = string.Empty;
    private bool _visible;
    private ContextMenuStrip? _contextMenuStrip;
    private object? _tag;
    private ToolTipIcon _balloonTipIcon;
    private string _balloonTipText = string.Empty;
    private string _balloonTipTitle = string.Empty;
    private BalloonWindow? _balloon;

    public NotifyIcon()
    {
    }

    public NotifyIcon(IContainer container)
        : this()
    {
        ArgumentNullException.ThrowIfNull(container);

        container.Add(this);
    }

    [DefaultValue(ToolTipIcon.None)]
    public ToolTipIcon BalloonTipIcon
    {
        get => _balloonTipIcon;
        set => _balloonTipIcon = value;
    }

    [DefaultValue("")]
    public string BalloonTipText
    {
        get => _balloonTipText;
        set => _balloonTipText = value ?? string.Empty;
    }

    [DefaultValue("")]
    public string BalloonTipTitle
    {
        get => _balloonTipTitle;
        set => _balloonTipTitle = value ?? string.Empty;
    }

    [DefaultValue(null)]
    public ContextMenuStrip? ContextMenuStrip
    {
        get => _contextMenuStrip;
        set => _contextMenuStrip = value;
    }

    [DefaultValue(null)]
    public Icon? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            _host?.SetIcon(_icon);
        }
    }

    [DefaultValue(null)]
    [TypeConverter(typeof(StringConverter))]
    public object? Tag
    {
        get => _tag;
        set => _tag = value;
    }

    [DefaultValue("")]
    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (value.Length > MaxTextLength) throw new ArgumentOutOfRangeException(nameof(value), value, $"Text cannot exceed {MaxTextLength} characters.");

            _text = value;
            _host?.SetToolTipText(_text);
        }
    }

    [DefaultValue(false)]
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;

            _visible = value;
            if (value) CreateHost();
            else DestroyHost();
        }
    }

    public event EventHandler? BalloonTipClicked;

    public event EventHandler? BalloonTipClosed;

    public event EventHandler? BalloonTipShown;

    public event EventHandler? Click;

    public event EventHandler? DoubleClick;

    public event MouseEventHandler? MouseClick;

    public event MouseEventHandler? MouseDoubleClick;

    public event MouseEventHandler? MouseDown;

    public event MouseEventHandler? MouseMove;

    public event MouseEventHandler? MouseUp;

    public void ShowBalloonTip(int timeout) => ShowBalloonTip(timeout, _balloonTipTitle, _balloonTipText, _balloonTipIcon);

    public void ShowBalloonTip(int timeout, string tipTitle, string tipText, ToolTipIcon tipIcon)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeout);
        ArgumentException.ThrowIfNullOrEmpty(tipText);

        if (!_visible) return;

        _balloon?.CloseBalloon();
        _balloon = new BalloonWindow(this);
        _balloon.ShowBalloon(tipTitle, tipText, tipIcon, timeout);

        BalloonTipShown?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DestroyHost();
            _balloon?.CloseBalloon();
            _balloon = null;
        }

        base.Dispose(disposing);
    }

    internal void RaiseBalloonTipClicked() => BalloonTipClicked?.Invoke(this, EventArgs.Empty);

    internal void RaiseBalloonTipClosed() => BalloonTipClosed?.Invoke(this, EventArgs.Empty);

    private void CreateHost()
    {
        if (_host is not null) return;

        _host = TrayIconHosts.Factory();
        _host.SetIcon(_icon);
        _host.SetToolTipText(_text);
        _host.Click += OnHostClick;
        _host.DoubleClick += OnHostDoubleClick;
        _host.MouseDown += OnHostMouseDown;
        _host.MouseMove += OnHostMouseMove;
        _host.MouseUp += OnHostMouseUp;
    }

    private void DestroyHost()
    {
        if (_host is not { } host) return;

        host.Click -= OnHostClick;
        host.DoubleClick -= OnHostDoubleClick;
        host.MouseDown -= OnHostMouseDown;
        host.MouseMove -= OnHostMouseMove;
        host.MouseUp -= OnHostMouseUp;
        host.Dispose();
        _host = null;
    }

    private void OnHostClick(object? sender, TrayIconMouseEventArgs e)
    {
        Click?.Invoke(this, EventArgs.Empty);
        MouseClick?.Invoke(this, ToMouseEventArgs(e, clicks: 1));
        if (e.Button == MouseButtons.Right) _contextMenuStrip?.Show(e.Location);
    }

    private void OnHostDoubleClick(object? sender, TrayIconMouseEventArgs e)
    {
        DoubleClick?.Invoke(this, EventArgs.Empty);
        MouseDoubleClick?.Invoke(this, ToMouseEventArgs(e, clicks: 2));
    }

    private void OnHostMouseDown(object? sender, TrayIconMouseEventArgs e) => MouseDown?.Invoke(this, ToMouseEventArgs(e, clicks: 1));

    private void OnHostMouseMove(object? sender, TrayIconMouseEventArgs e) => MouseMove?.Invoke(this, ToMouseEventArgs(e, clicks: 0));

    private void OnHostMouseUp(object? sender, TrayIconMouseEventArgs e)
    {
        MouseUp?.Invoke(this, ToMouseEventArgs(e, clicks: 1));
        if (e.Button == MouseButtons.Right) _contextMenuStrip?.Show(e.Location);
    }

    private static MouseEventArgs ToMouseEventArgs(TrayIconMouseEventArgs e, int clicks) =>
        new(e.Button, clicks, e.Location.X, e.Location.Y, 0);
}
