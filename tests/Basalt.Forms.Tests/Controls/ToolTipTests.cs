using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>Tool tips: their delays, their text and when they go away.</summary>
public class ToolTipTests
{
    [Fact]
    public void AutomaticDelay_sets_the_three_delays_as_WinForms_derives_them()
    {
        using var tip = new ToolTip { AutomaticDelay = 300 };

        Assert.Equal(300, tip.InitialDelay);
        Assert.Equal(3000, tip.AutoPopDelay);
        Assert.Equal(60, tip.ReshowDelay);
    }

    [Fact]
    public void A_control_keeps_its_tip_until_it_is_cleared()
    {
        using var tip = new ToolTip();
        var button = new Button();

        tip.SetToolTip(button, "Saves the file");
        Assert.Equal("Saves the file", tip.GetToolTip(button));

        tip.SetToolTip(button, null);
        Assert.Equal(string.Empty, tip.GetToolTip(button));
    }

    [AvaloniaFact]
    public void Resting_on_a_control_shows_its_tip_and_a_press_hides_it()
    {
        var button = new Button { Bounds = new Rectangle(10, 10, 75, 23) };
        var form = new Form { ClientSize = new Size(200, 100) };
        form.Controls.Add(button);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        using var tip = new ToolTip { InitialDelay = 10, ShowAlways = true };
        tip.SetToolTip(button, "Saves the file");
        var popups = 0;
        tip.Popup += (_, _) => popups++;

        form.Host!.MouseMove(new Avalonia.Point(20, 20));
        using (var wait = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
        {
            Dispatcher.UIThread.MainLoop(wait.Token);
        }

        Assert.Equal(1, popups);
        Assert.Contains(Popups.All, popup => popup is not Form);

        form.Host!.MouseDown(new Avalonia.Point(20, 20), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(20, 20), MouseButton.Left);

        Assert.DoesNotContain(Popups.All, popup => popup is not Form);

        form.Close();
    }

    [AvaloniaFact]
    public void A_cancelled_Popup_shows_nothing()
    {
        var form = new Form { ClientSize = new Size(200, 100) };
        form.Show();
        using var tip = new ToolTip();
        tip.Popup += (_, e) => e.Cancel = true;

        tip.Show("Hello", form, new Point(10, 10));

        Assert.Empty(Popups.All);

        form.Close();
    }

    [AvaloniaFact]
    public void Clearing_the_tip_on_screen_hides_it()
    {
        var button = new Button();
        var form = new Form { ClientSize = new Size(200, 100) };
        form.Controls.Add(button);
        form.Show();
        using var tip = new ToolTip();
        tip.SetToolTip(button, "Saves");
        tip.Show("Saves", button, new Point(0, 30));

        tip.SetToolTip(button, null);

        Assert.DoesNotContain(Popups.All, popup => popup is not Form);

        form.Close();
    }

    [AvaloniaFact]
    public void Hide_leaves_a_tip_shown_for_another_window()
    {
        var first = new Button();
        var second = new Button { Left = 100 };
        var form = new Form { ClientSize = new Size(200, 100) };
        form.Controls.AddRange([first, second]);
        form.Show();
        using var tip = new ToolTip();
        tip.Show("First", first, new Point(0, 30));

        tip.Hide(second);
        Assert.Contains(Popups.All, popup => popup is not Form);

        tip.Hide(first);
        Assert.DoesNotContain(Popups.All, popup => popup is not Form);

        form.Close();
    }
}
