using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Hosting;

/// <summary>Borderless popup windows for drop-downs, menus and tool tips.</summary>
public class PopupTests
{
    /// <summary>A top-level control, as a drop-down list or a menu is.</summary>
    private sealed class PopupContent : Control
    {
        public int Dismissals { get; private set; }

        public PopupContent()
        {
            SetTopLevel(true);
            BackColor = Color.Yellow;
        }

        internal override void DismissPopup()
        {
            Dismissals++;
            base.DismissPopup();
        }

        public bool WantsKeyboard { get; init; }

        internal override bool TakesKeyboardWhileOpen => WantsKeyboard;

        public List<Keys> Keys { get; } = [];

        protected override void OnKeyDown(System.Windows.Forms.KeyEventArgs e)
        {
            Keys.Add(e.KeyCode);
            base.OnKeyDown(e);
        }
    }

    private static Form ShowForm(params Control[] controls)
    {
        var form = new Form { ClientSize = new Size(200, 150) };
        form.Controls.AddRange(controls);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    [AvaloniaFact]
    public void A_popup_opens_where_asked_and_shows_its_control()
    {
        var anchor = new Control { Bounds = new Rectangle(20, 30, 80, 20) };
        var form = ShowForm(anchor);
        var popup = new PopupContent();
        var below = anchor.RectangleToScreen(new Rectangle(0, anchor.Height, 80, 60));

        Popups.Show(popup, anchor, below);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(popup.PopupWindow);
        Assert.True(popup.PopupWindow!.IsVisible);
        Assert.Contains(popup, Popups.All);

        using var frame = popup.PopupWindow.CaptureRenderedFrame()!;
        Assert.True(frame.PixelSize.Width > 0);

        Popups.Close(popup);
        form.Close();
    }

    [AvaloniaFact]
    public void A_press_in_the_owner_closes_the_popup_and_does_nothing_else()
    {
        var target = new Control { Bounds = new Rectangle(0, 0, 200, 150) };
        var pressed = 0;
        target.MouseDown += (_, _) => pressed++;
        var form = ShowForm(target);
        var popup = new PopupContent();
        Popups.Show(popup, target, new Rectangle(form.PointToScreen(new Point(10, 10)), new Size(50, 50)));

        form.Host!.MouseDown(new Avalonia.Point(150, 120), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(150, 120), MouseButton.Left);

        Assert.Equal(1, popup.Dismissals);
        Assert.DoesNotContain(popup, Popups.All);
        Assert.Equal(0, pressed);

        form.Close();
    }

    [AvaloniaFact]
    public void Clicks_inside_the_popup_reach_its_controls()
    {
        var form = ShowForm();
        var popup = new PopupContent();
        var item = new Control { Bounds = new Rectangle(5, 5, 40, 20), Parent = popup };
        var clicks = 0;
        item.MouseUp += (_, _) => clicks++;
        Popups.Show(popup, form, new Rectangle(form.PointToScreen(new Point(10, 10)), new Size(60, 40)));

        popup.PopupWindow!.MouseDown(new Avalonia.Point(10, 10), MouseButton.Left);
        popup.PopupWindow!.MouseUp(new Avalonia.Point(10, 10), MouseButton.Left);

        Assert.Equal(1, clicks);
        Assert.Contains(popup, Popups.All);

        Popups.Close(popup);
        form.Close();
    }

    [AvaloniaFact]
    public void Closing_the_owner_form_closes_its_popups()
    {
        var form = ShowForm();
        var popup = new PopupContent();
        Popups.Show(popup, form, new Rectangle(form.PointToScreen(new Point(10, 10)), new Size(60, 40)));

        form.Close();

        Assert.DoesNotContain(popup, Popups.All);
        Assert.False(popup.PopupWindow?.IsVisible ?? false);
    }

    [AvaloniaFact]
    public void A_menu_like_popup_takes_the_keys_of_its_window_while_open()
    {
        var focused = new TextBox();
        var form = ShowForm(focused);
        focused.Focus();
        var popup = new PopupContent { WantsKeyboard = true };
        Popups.Show(popup, form, new Rectangle(form.PointToScreen(new Point(10, 10)), new Size(60, 40)));

        form.Host!.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

        Assert.Equal([Keys.Down], popup.Keys);
        Assert.Equal(string.Empty, focused.Text);

        Popups.Close(popup);
        form.Close();
    }

    [AvaloniaFact]
    public void A_press_in_one_form_leaves_another_forms_tool_tip_open()
    {
        var first = ShowForm();
        var second = ShowForm();
        var tip = new NotDismissedByClicks();
        Popups.Show(tip, second, new Rectangle(second.PointToScreen(new Point(10, 10)), new Size(60, 20)));

        first.Host!.MouseDown(new Avalonia.Point(50, 50), MouseButton.Left);
        first.Host!.MouseUp(new Avalonia.Point(50, 50), MouseButton.Left);

        Assert.Contains(tip, Popups.All);

        Popups.Close(tip);
        first.Close();
        second.Close();
    }

    private sealed class NotDismissedByClicks : Control
    {
        public NotDismissedByClicks() => SetTopLevel(true);

        internal override bool ClosesOnOutsideClick => false;
    }
}
