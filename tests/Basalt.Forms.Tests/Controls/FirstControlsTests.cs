using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>Label, Button, Panel and Timer: the behaviour forms rely on most.</summary>
public class FirstControlsTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    [Fact]
    public void An_auto_sized_label_takes_its_text_size_at_once()
    {
        var label = new Label { Font = Font, AutoSize = true, Text = "Hello" };
        var first = label.Width;

        label.Text = "Hello, longer text";

        Assert.Equal(TextRenderer.MeasureText("Hello", Font).Width, first);
        Assert.Equal(TextRenderer.MeasureText("Hello, longer text", Font).Width, label.Width);
        Assert.Equal(Font.Height, label.Height);
    }

    [Fact]
    public void A_label_wraps_when_asked_for_its_size_within_a_width()
    {
        var label = new Label { Font = Font, Text = "one two three four five six" };
        var oneLine = label.GetPreferredSize(Size.Empty);

        var wrapped = label.GetPreferredSize(new Size(oneLine.Width / 2, 0));

        Assert.True(wrapped.Height >= 2 * Font.Height, $"Expected wrapped lines, got {wrapped}.");
    }

    [Fact]
    public void Label_borders_are_the_static_edge_and_the_plain_border()
    {
        var sunken = new Label { BorderStyle = BorderStyle.Fixed3D, Size = new Size(50, 20) };
        var single = new Label { BorderStyle = BorderStyle.FixedSingle, Size = new Size(50, 20) };

        Assert.Equal(new Size(48, 18), sunken.ClientSize);
        Assert.Equal(new Size(48, 18), single.ClientSize);
    }

    [Fact]
    public void Panel_borders_shrink_the_client_area_by_their_width()
    {
        var sunken = new Panel { BorderStyle = BorderStyle.Fixed3D, Size = new Size(100, 50) };
        var single = new Panel { BorderStyle = BorderStyle.FixedSingle, Size = new Size(100, 50) };

        Assert.Equal(new Size(96, 46), sunken.ClientSize);
        Assert.Equal(new Size(98, 48), single.ClientSize);
        Assert.False(new Panel().TabStop);
    }

    [AvaloniaFact]
    public void A_button_with_a_result_sets_the_forms_result_before_its_click_handlers()
    {
        var button = new Button { DialogResult = DialogResult.OK };
        var form = ShowWith(button);
        var seen = DialogResult.None;
        button.Click += (_, _) => seen = form.DialogResult;

        button.PerformClick();

        Assert.Equal(DialogResult.OK, seen);

        form.Close();
    }

    [Fact]
    public void A_button_that_cannot_be_selected_ignores_PerformClick()
    {
        var button = new Button { Visible = false };
        var clicks = 0;
        button.Click += (_, _) => clicks++;

        button.PerformClick();

        Assert.Equal(0, clicks);
    }

    [Fact]
    public void A_buttons_mnemonic_clicks_it()
    {
        var button = new Button { Text = "&Save" };
        var clicks = 0;
        button.Click += (_, _) => clicks++;

        Assert.True(InvokeMnemonic(button, 's'));
        Assert.False(InvokeMnemonic(button, 'x'));
        Assert.Equal(1, clicks);
    }

    private static bool InvokeMnemonic(Control control, char character) =>
        (bool)typeof(Control).GetMethod("ProcessMnemonic", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(control, [character])!;

    private static Form ShowWith(Control control)
    {
        var form = new Form { ClientSize = new Size(200, 100) };
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    [AvaloniaFact]
    public void A_button_clicks_once_on_release_over_it_and_not_when_released_outside()
    {
        var button = new Button { Bounds = new Rectangle(10, 10, 75, 23) };
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        var form = ShowWith(button);

        form.Host!.MouseDown(new Avalonia.Point(20, 20), MouseButton.Left);
        form.Host.MouseUp(new Avalonia.Point(20, 20), MouseButton.Left);
        Assert.Equal(1, clicks);

        form.Host.MouseDown(new Avalonia.Point(20, 20), MouseButton.Left);
        form.Host.MouseMove(new Avalonia.Point(150, 80));
        form.Host.MouseUp(new Avalonia.Point(150, 80), MouseButton.Left);
        Assert.Equal(1, clicks);

        form.Close();
    }

    [AvaloniaFact]
    public void Space_clicks_a_focused_button_on_release()
    {
        var button = new Button();
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        var form = ShowWith(button);
        button.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.Equal(0, clicks);

        form.Host.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.Equal(1, clicks);

        form.Close();
    }

    [AvaloniaFact]
    public void Space_held_through_a_mouse_click_clicks_only_once()
    {
        var button = new Button { Bounds = new Rectangle(10, 10, 75, 23) };
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        var form = ShowWith(button);
        button.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        form.Host.MouseDown(new Avalonia.Point(20, 20), MouseButton.Left);
        form.Host.MouseUp(new Avalonia.Point(20, 20), MouseButton.Left);
        form.Host.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

        Assert.Equal(1, clicks);

        form.Close();
    }

    [AvaloniaFact]
    public void A_timer_ticks_on_the_UI_thread_until_stopped()
    {
        using var timer = new System.Windows.Forms.Timer { Interval = 10 };
        var ticks = 0;
        timer.Tick += (_, _) =>
        {
            ticks++;
            if (ticks == 3) timer.Stop();
        };

        // Timers need the message loop, not only the queued jobs.
        using var loop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        timer.Tick += (_, _) =>
        {
            if (ticks == 3) loop.Cancel();
        };

        timer.Start();
        Dispatcher.UIThread.MainLoop(loop.Token);

        Assert.Equal(3, ticks);
        Assert.False(timer.Enabled);
    }

    [Fact]
    public void Standard_cursors_convert_to_their_names_and_back()
    {
        var converter = new CursorConverter();

        Assert.Equal("Hand", converter.ConvertToString(Cursors.Hand));
        Assert.Same(Cursors.IBeam, converter.ConvertFromString("IBeam"));
        Assert.Same(Cursors.Hand, new Control { Cursor = Cursors.Hand }.Cursor);
    }
}
