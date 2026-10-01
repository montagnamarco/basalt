using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Hosting;

/// <summary>Mouse and keyboard reaching controls through the window, on the headless platform.</summary>
public class InputTests
{
    private static Form ShowForm(params Control[] controls)
    {
        var form = new Form { ClientSize = new Size(200, 100) };
        form.Controls.AddRange(controls);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    [AvaloniaFact]
    public void A_click_raises_down_click_and_up_in_the_WinForms_order_in_client_coordinates()
    {
        var target = new Control { Bounds = new Rectangle(50, 20, 40, 30) };
        var log = new List<string>();
        target.MouseDown += (_, e) => log.Add($"MouseDown {e.Location}");
        target.Click += (_, _) => log.Add("Click");
        target.MouseClick += (_, _) => log.Add("MouseClick");
        target.MouseUp += (_, e) => log.Add($"MouseUp {e.Location}");
        var form = ShowForm(target);

        form.Host!.MouseDown(new Avalonia.Point(55, 25), MouseButton.Left);
        form.Host.MouseUp(new Avalonia.Point(55, 25), MouseButton.Left);

        Assert.Equal(["MouseDown {X=5,Y=5}", "Click", "MouseClick", "MouseUp {X=5,Y=5}"], log);

        form.Close();
    }

    [AvaloniaFact]
    public void Releasing_outside_the_control_raises_no_click_but_the_control_keeps_the_mouse()
    {
        var target = new Control { Bounds = new Rectangle(0, 0, 40, 30) };
        var log = new List<string>();
        target.Click += (_, _) => log.Add("Click");
        target.MouseUp += (_, e) => log.Add($"MouseUp {e.Location}");
        var form = ShowForm(target);

        form.Host!.MouseDown(new Avalonia.Point(10, 10), MouseButton.Left);
        form.Host.MouseMove(new Avalonia.Point(150, 80));
        form.Host.MouseUp(new Avalonia.Point(150, 80), MouseButton.Left);

        Assert.Equal(["MouseUp {X=150,Y=80}"], log);

        form.Close();
    }

    [AvaloniaFact]
    public void Moving_over_controls_raises_enter_and_leave()
    {
        var first = new Control { Bounds = new Rectangle(0, 0, 50, 50) };
        var second = new Control { Bounds = new Rectangle(100, 0, 50, 50) };
        var log = new List<string>();
        first.MouseEnter += (_, _) => log.Add("first enter");
        first.MouseLeave += (_, _) => log.Add("first leave");
        second.MouseEnter += (_, _) => log.Add("second enter");
        var form = ShowForm(first, second);

        form.Host!.MouseMove(new Avalonia.Point(10, 10));
        form.Host.MouseMove(new Avalonia.Point(110, 10));

        Assert.Equal(["first enter", "first leave", "second enter"], log);

        form.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_selectable_control_focuses_it_and_keys_go_to_it()
    {
        var target = new Control { Bounds = new Rectangle(0, 0, 50, 50) };
        var keys = new List<string>();
        target.KeyDown += (_, e) => keys.Add($"KeyDown {e.KeyCode}");
        target.KeyPress += (_, e) => keys.Add($"KeyPress {e.KeyChar}");
        target.KeyUp += (_, e) => keys.Add($"KeyUp {e.KeyCode}");
        var form = ShowForm(target);

        form.Host!.MouseDown(new Avalonia.Point(10, 10), MouseButton.Left);
        form.Host.MouseUp(new Avalonia.Point(10, 10), MouseButton.Left);
        Assert.True(target.Focused);

        form.Host.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        form.Host.KeyTextInput("a");
        form.Host.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.None);

        Assert.Equal(["KeyDown A", "KeyPress a", "KeyUp A"], keys);

        form.Close();
    }

    [AvaloniaFact]
    public void Tab_moves_the_focus_in_tab_order_and_wraps_on_the_form()
    {
        var first = new Control { TabIndex = 1 };
        var second = new Control { TabIndex = 0, Location = new Point(0, 30) };
        var form = ShowForm(first, second);
        second.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Assert.True(first.Focused);

        form.Host.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Assert.True(second.Focused);

        form.Close();
    }

    [AvaloniaFact]
    public void Moving_the_focus_raises_leave_validation_and_enter()
    {
        var first = new Control { Name = "first" };
        var second = new Control { Name = "second", Location = new Point(0, 30) };
        var log = new List<string>();

        foreach (var control in new[] { first, second })
        {
            control.Enter += (_, _) => log.Add($"{control.Name}.Enter");
            control.Leave += (_, _) => log.Add($"{control.Name}.Leave");
            control.GotFocus += (_, _) => log.Add($"{control.Name}.GotFocus");
            control.LostFocus += (_, _) => log.Add($"{control.Name}.LostFocus");
            control.Validating += (_, _) => log.Add($"{control.Name}.Validating");
            control.Validated += (_, _) => log.Add($"{control.Name}.Validated");
        }

        var form = ShowForm(first, second);
        first.Focus();
        log.Clear();

        second.Focus();

        Assert.Equal(["first.LostFocus", "first.Leave", "first.Validating", "first.Validated", "second.Enter", "second.GotFocus"], log);

        log.Clear();
        first.Select();

        Assert.Equal(["second.Leave", "second.Validating", "second.Validated", "first.Enter", "second.LostFocus", "first.GotFocus"], log);

        form.Close();
    }

    [AvaloniaFact]
    public void A_cancelled_validation_keeps_the_focus()
    {
        var first = new Control();
        var second = new Control { Location = new Point(0, 30) };
        first.Validating += (_, e) => e.Cancel = true;
        var form = ShowForm(first, second);
        first.Focus();

        second.Focus();

        Assert.True(first.Focused);

        form.Close();
    }

    [AvaloniaFact]
    public void Enter_presses_the_accept_button_and_Escape_the_cancel_button()
    {
        var accept = new FakeButton();
        var cancel = new FakeButton();
        var form = ShowForm();
        form.AcceptButton = accept;
        form.CancelButton = cancel;

        form.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        form.Host.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Equal(1, accept.Clicks);
        Assert.Equal(1, cancel.Clicks);

        form.Close();
    }

    [AvaloniaFact]
    public void Enter_reaches_KeyPress_as_a_carriage_return()
    {
        var target = new Control();
        var characters = new List<char>();
        target.KeyPress += (_, e) => characters.Add(e.KeyChar);
        var form = ShowForm(target);
        target.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal(['\r'], characters);

        form.Close();
    }

    [AvaloniaFact]
    public void A_form_with_KeyPreview_sees_keys_before_its_controls()
    {
        var target = new Control();
        var log = new List<string>();
        target.KeyDown += (_, _) => log.Add("control");
        var form = ShowForm(target);
        form.KeyPreview = true;
        form.KeyDown += (_, e) =>
        {
            log.Add("form");
            e.Handled = true;
        };
        target.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);

        Assert.Equal(["form"], log);

        form.Close();
    }

    private sealed class FakeButton : Control, IButtonControl
    {
        public int Clicks { get; private set; }

        public DialogResult DialogResult { get; set; }

        public void NotifyDefault(bool value)
        {
        }

        public void PerformClick() => Clicks++;
    }

    [AvaloniaFact]
    public void Removing_the_focused_control_leaves_nothing_focused_in_the_window()
    {
        var first = new Control();
        var second = new Control { Location = new Point(0, 30) };
        var form = ShowForm(first, second);
        first.Focus();

        form.Controls.Remove(first);

        Assert.False(first.Focused);
        Assert.Null(form.ActiveControl);
        Assert.False(form.ContainsFocus && form.ActiveControl == first);

        form.Close();
    }

    [AvaloniaFact]
    public void Hiding_the_focused_control_moves_the_focus_to_the_next_one()
    {
        var first = new Control();
        var second = new Control { Location = new Point(0, 30) };
        var form = ShowForm(first, second);
        first.Focus();

        first.Visible = false;

        Assert.True(second.Focused);

        form.Close();
    }
}
