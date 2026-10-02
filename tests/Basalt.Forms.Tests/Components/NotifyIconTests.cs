using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Hosting;
using System.Windows.Forms.Internal.Tray;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Components;

/// <summary>
/// NotifyIcon's properties, the mouse events a tray host reports, and its
/// balloon tip. A fake ITrayIconHost stands in for the platform so no real
/// tray icon ever shows; a real Avalonia TrayIcon only ever reports a plain
/// click (see AvaloniaTrayIconHost), so DoubleClick, MouseDown, MouseMove and
/// MouseUp are exercised here through the fake only and are not verified
/// against a real Windows tray icon.
/// </summary>
public class NotifyIconTests
{
    [Fact]
    public void Text_beyond_127_characters_throws()
    {
        using var icon = new NotifyIcon();
        var tooLong = new string('a', 128);

        Assert.Throws<ArgumentOutOfRangeException>(() => icon.Text = tooLong);

        icon.Text = new string('a', 127);
        Assert.Equal(127, icon.Text.Length);
    }

    [Fact]
    public void Visible_true_creates_the_tray_icon_and_false_removes_it()
    {
        using var swap = FakeTrayIcons.Swap();
        using var icon = new NotifyIcon();

        Assert.Empty(swap.Created);

        icon.Visible = true;
        Assert.Single(swap.Created);
        Assert.False(swap.Last.Disposed);

        icon.Visible = false;
        Assert.True(swap.Last.Disposed);
    }

    [Fact]
    public void Setting_Visible_true_twice_does_not_create_a_second_host()
    {
        using var swap = FakeTrayIcons.Swap();
        using var icon = new NotifyIcon { Visible = true };

        icon.Visible = true;

        Assert.Single(swap.Created);
    }

    [Fact]
    public void Icon_and_Text_reach_the_host_both_before_and_after_it_is_created()
    {
        using var swap = FakeTrayIcons.Swap();
        using var systemIcon = (Icon)SystemIcons.Information.Clone();
        using var icon = new NotifyIcon { Icon = systemIcon, Text = "Ready" };

        icon.Visible = true;
        Assert.Same(systemIcon, swap.Last.Icon);
        Assert.Equal("Ready", swap.Last.Text);

        icon.Text = "Busy";
        Assert.Equal("Busy", swap.Last.Text);
    }

    [Fact]
    public void A_click_from_the_host_raises_Click_and_MouseClick()
    {
        using var swap = FakeTrayIcons.Swap();
        using var icon = new NotifyIcon { Visible = true };
        var clicked = false;
        MouseEventArgs? mouseClick = null;
        icon.Click += (_, _) => clicked = true;
        icon.MouseClick += (_, e) => mouseClick = e;

        swap.Last.RaiseClick(MouseButtons.Left, new Point(5, 7));

        Assert.True(clicked);
        Assert.Equal(MouseButtons.Left, mouseClick!.Button);
        Assert.Equal(5, mouseClick.X);
        Assert.Equal(7, mouseClick.Y);
    }

    [Fact]
    public void A_double_click_from_the_host_raises_DoubleClick_and_MouseDoubleClick()
    {
        using var swap = FakeTrayIcons.Swap();
        using var icon = new NotifyIcon { Visible = true };
        var doubleClicked = false;
        icon.DoubleClick += (_, _) => doubleClicked = true;

        swap.Last.RaiseDoubleClick(MouseButtons.Left, new Point(1, 1));

        Assert.True(doubleClicked);
    }

    [Fact]
    public void MouseDown_MouseMove_and_MouseUp_forward_the_button_and_location()
    {
        using var swap = FakeTrayIcons.Swap();
        using var icon = new NotifyIcon { Visible = true };
        var downs = new List<MouseEventArgs>();
        var moves = new List<MouseEventArgs>();
        var ups = new List<MouseEventArgs>();
        icon.MouseDown += (_, e) => downs.Add(e);
        icon.MouseMove += (_, e) => moves.Add(e);
        icon.MouseUp += (_, e) => ups.Add(e);

        swap.Last.RaiseMouseDown(MouseButtons.Right, new Point(3, 4));
        swap.Last.RaiseMouseMove(MouseButtons.None, new Point(9, 9));
        swap.Last.RaiseMouseUp(MouseButtons.Right, new Point(3, 4));

        Assert.Equal(MouseButtons.Right, Assert.Single(downs).Button);
        Assert.Single(moves);
        Assert.Equal(MouseButtons.Right, Assert.Single(ups).Button);
    }

    [AvaloniaFact]
    public void A_right_mouse_up_opens_the_ContextMenuStrip_at_the_pointer()
    {
        using var swap = FakeTrayIcons.Swap();
        using var menu = new ContextMenuStrip();
        menu.Items.Add("Exit");
        using var icon = new NotifyIcon { Visible = true, ContextMenuStrip = menu };

        var opened = false;
        menu.Opening += (_, _) => opened = true;

        swap.Last.RaiseMouseUp(MouseButtons.Right, new Point(42, 24));

        Assert.True(opened);
    }

    [Fact]
    public void ShowBalloonTip_rejects_a_negative_timeout_and_empty_text()
    {
        using var swap = FakeTrayIcons.Swap();
        using var icon = new NotifyIcon { Visible = true };

        Assert.Throws<ArgumentOutOfRangeException>(() => icon.ShowBalloonTip(-1, "Title", "Text", ToolTipIcon.Info));
        Assert.Throws<ArgumentException>(() => icon.ShowBalloonTip(1000, "Title", "", ToolTipIcon.Info));
    }

    [Fact]
    public void Dispose_removes_the_tray_icon()
    {
        using var swap = FakeTrayIcons.Swap();
        var icon = new NotifyIcon { Visible = true };

        icon.Dispose();

        Assert.True(swap.Last.Disposed);
    }

    [AvaloniaFact]
    public void ShowBalloonTip_shows_near_the_bottom_right_and_closes_after_its_timeout()
    {
        using var swap = FakeTrayIcons.Swap();
        using var icon = new NotifyIcon { Visible = true };
        var shown = false;
        var closed = false;
        icon.BalloonTipShown += (_, _) => shown = true;
        icon.BalloonTipClosed += (_, _) => closed = true;

        icon.ShowBalloonTip(20, "Title", "A balloon", ToolTipIcon.Info);

        Assert.True(shown);
        var balloon = Assert.Single(Popups.All.OfType<BalloonWindow>());
        var workingArea = Screen.PrimaryScreen!.WorkingArea;
        Assert.True(balloon.Right <= workingArea.Right);
        Assert.True(balloon.Bottom <= workingArea.Bottom);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!closed && DateTime.UtcNow < deadline)
        {
            using var slice = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
            Dispatcher.UIThread.MainLoop(slice.Token);
        }

        Assert.True(closed);
        Assert.Empty(Popups.All.OfType<BalloonWindow>());

        // Hidden is not enough: the balloon's native window must be released.
        Dispatcher.UIThread.RunJobs();
        Assert.True(balloon.IsDisposed);
    }

    [AvaloniaFact]
    public void Clicking_the_balloon_raises_BalloonTipClicked_and_closes_it()
    {
        using var swap = FakeTrayIcons.Swap();
        using var icon = new NotifyIcon { Visible = true };
        var clicked = false;
        icon.BalloonTipClicked += (_, _) => clicked = true;

        icon.ShowBalloonTip(0, "Title", "A balloon", ToolTipIcon.Info);
        var balloon = Assert.Single(Popups.All.OfType<BalloonWindow>());
        balloon.PopupWindow!.MouseDown(new Avalonia.Point(5, 5), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(clicked);
        Assert.Empty(Popups.All.OfType<BalloonWindow>());
        Assert.True(balloon.IsDisposed);
    }

    [AvaloniaFact]
    public void A_new_balloon_closes_and_releases_the_one_already_shown()
    {
        using var swap = FakeTrayIcons.Swap();
        using var icon = new NotifyIcon { Visible = true };
        var closings = 0;
        icon.BalloonTipClosed += (_, _) => closings++;

        icon.ShowBalloonTip(0, "First", "One", ToolTipIcon.Info);
        var first = Assert.Single(Popups.All.OfType<BalloonWindow>());
        icon.ShowBalloonTip(0, "Second", "Two", ToolTipIcon.Info);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, closings);
        Assert.True(first.IsDisposed);
        Assert.NotSame(first, Assert.Single(Popups.All.OfType<BalloonWindow>()));
    }

    private sealed class FakeTrayIcons : IDisposable
    {
        private readonly Func<ITrayIconHost> _previous;

        public List<FakeTrayIconHost> Created { get; } = [];

        private FakeTrayIcons()
        {
            _previous = TrayIconHosts.Factory;
            TrayIconHosts.Factory = () =>
            {
                var fake = new FakeTrayIconHost();
                Created.Add(fake);
                return fake;
            };
        }

        public FakeTrayIconHost Last => Created[^1];

        public static FakeTrayIcons Swap() => new();

        public void Dispose() => TrayIconHosts.Factory = _previous;
    }

    private sealed class FakeTrayIconHost : ITrayIconHost
    {
        public Icon? Icon { get; private set; }

        public string Text { get; private set; } = string.Empty;

        public bool Disposed { get; private set; }

        public event EventHandler<TrayIconMouseEventArgs>? MouseDown;

        public event EventHandler<TrayIconMouseEventArgs>? MouseUp;

        public event EventHandler<TrayIconMouseEventArgs>? MouseMove;

        public event EventHandler<TrayIconMouseEventArgs>? Click;

        public event EventHandler<TrayIconMouseEventArgs>? DoubleClick;

        public void SetIcon(Icon? icon) => Icon = icon;

        public void SetToolTipText(string text) => Text = text;

        public void Dispose() => Disposed = true;

        public void RaiseClick(MouseButtons button, Point location) => Click?.Invoke(this, new TrayIconMouseEventArgs(button, location));

        public void RaiseDoubleClick(MouseButtons button, Point location) => DoubleClick?.Invoke(this, new TrayIconMouseEventArgs(button, location));

        public void RaiseMouseDown(MouseButtons button, Point location) => MouseDown?.Invoke(this, new TrayIconMouseEventArgs(button, location));

        public void RaiseMouseUp(MouseButtons button, Point location) => MouseUp?.Invoke(this, new TrayIconMouseEventArgs(button, location));

        public void RaiseMouseMove(MouseButtons button, Point location) => MouseMove?.Invoke(this, new TrayIconMouseEventArgs(button, location));
    }
}
