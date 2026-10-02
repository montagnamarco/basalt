using System.Drawing;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms.Internal.Tray;

/// <summary>
/// The balloon tip shown for a NotifyIcon. Avalonia has no balloon of its
/// own, so this is a small classic tooltip-style top-level window near the
/// bottom-right of the primary screen's working area, closed by its timeout
/// or by a click on it.
/// </summary>
internal sealed class BalloonWindow : Control
{
    private const int BalloonPadding = 6;
    private const int BalloonWidth = 220;
    private const int IconSize = 16;

    private readonly NotifyIcon _owner;
    private readonly Timer _closeTimer = new();
    private string _title = string.Empty;
    private string _text = string.Empty;
    private ToolTipIcon _icon;

    public BalloonWindow(NotifyIcon owner)
    {
        _owner = owner;
        SetTopLevel(true);
        _closeTimer.Tick += (_, _) => CloseBalloon();
    }

    internal override bool ClosesOnOutsideClick => false;

    public void ShowBalloon(string title, string text, ToolTipIcon icon, int timeout)
    {
        _title = title ?? string.Empty;
        _text = text;
        _icon = icon;

        var size = Measure();
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
        var location = new Point(workingArea.Right - size.Width - BalloonPadding, workingArea.Bottom - size.Height - BalloonPadding);

        Popups.Show(this, null, new Rectangle(location, size));

        _closeTimer.Stop();
        if (timeout > 0)
        {
            _closeTimer.Interval = timeout;
            _closeTimer.Start();
        }
    }

    public void CloseBalloon()
    {
        _closeTimer.Stop();

        if (Popups.All.Contains(this))
        {
            Popups.Close(this);
            _owner.RaiseBalloonTipClosed();
        }

        // Hiding keeps the native window alive; a balloon is never shown again, so
        // it is disposed. Posted, because a click on the balloon itself closes it
        // while its own mouse handling is still running.
        Avalonia.Threading.Dispatcher.UIThread.Post(Dispose);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _closeTimer.Dispose();

        base.Dispose(disposing);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        _owner.RaiseBalloonTipClicked();
        CloseBalloon();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = ClientRectangle;
        using var back = new SolidBrush(SystemColors.Info);
        e.Graphics.FillRectangle(back, bounds);
        ControlPaint.DrawBorder(e.Graphics, bounds, Color.Black, ButtonBorderStyle.Solid);

        var textBounds = Rectangle.Inflate(bounds, -BalloonPadding, -BalloonPadding);
        if (IconFor(_icon) is { } icon)
        {
            e.Graphics.DrawIcon(icon, new Rectangle(textBounds.Left, textBounds.Top, IconSize, IconSize));
            textBounds = Rectangle.FromLTRB(textBounds.Left + IconSize + BalloonPadding, textBounds.Top, textBounds.Right, textBounds.Bottom);
        }

        var top = textBounds.Top;
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

        if (_title.Length > 0)
        {
            using var bold = new Font(Font, FontStyle.Bold);
            var titleHeight = TextRenderer.MeasureText(_title, bold, textBounds.Size, flags).Height;
            TextRenderer.DrawText(e.Graphics, _title, bold, new Rectangle(textBounds.Left, top, textBounds.Width, titleHeight), SystemColors.InfoText, flags);
            top += titleHeight + 2;
        }

        TextRenderer.DrawText(e.Graphics, _text, Font, new Rectangle(textBounds.Left, top, textBounds.Width, Math.Max(textBounds.Bottom - top, 0)), SystemColors.InfoText, flags);
    }

    private static Icon? IconFor(ToolTipIcon icon) => icon switch
    {
        ToolTipIcon.Info => SystemIcons.Information,
        ToolTipIcon.Warning => SystemIcons.Warning,
        ToolTipIcon.Error => SystemIcons.Error,
        _ => null,
    };

    private Size Measure()
    {
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
        var textWidth = BalloonWidth - BalloonPadding * 2 - IconSize - BalloonPadding;
        var textHeight = TextRenderer.MeasureText(_text, Font, new Size(textWidth, int.MaxValue), flags).Height;

        if (_title.Length > 0)
        {
            using var bold = new Font(Font, FontStyle.Bold);
            textHeight += TextRenderer.MeasureText(_title, bold, new Size(textWidth, int.MaxValue), flags).Height + 2;
        }

        return new Size(BalloonWidth, Math.Max(textHeight, IconSize) + BalloonPadding * 2);
    }
}
