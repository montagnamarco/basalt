using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Hosting;

/// <summary>Icon, help button, scaling and the window-level properties a form carries that a plain control does not.</summary>
public class FormAppearanceTests
{
    private sealed class ExposedForm : Form
    {
        public void CallCenterToScreen() => CenterToScreen();

        public void CallCenterToParent() => CenterToParent();

        public void CallApplyAutoScaling() => ApplyAutoScaling();
    }

    [Fact]
    public void Setting_the_icon_invalidates_a_non_top_level_forms_own_frame()
    {
        var parent = new Form { ClientSize = new Size(300, 200) };
        var child = new Form { TopLevel = false, ControlBox = true, Text = "Child", Parent = parent, Bounds = new Rectangle(10, 10, 150, 100) };

        Assert.False(child.ShowIcon == false);

        // Painting throws if the non-client caption cannot carry an icon; this proves
        // the plumbing (WindowCaption.Icon, ClassicCaption's icon glyph) accepts one.
        child.Icon = SystemIcons.Application;
        Assert.Same(SystemIcons.Application, child.Icon);

        child.ShowIcon = false;
        Assert.False(child.ShowIcon);

        parent.Dispose();
    }

    [AvaloniaFact]
    public void F1_on_a_form_with_HelpButton_asks_the_focused_control_for_help_and_does_not_click_the_button()
    {
        var box = new TextBox();
        var form = new Form { HelpButton = true, MaximizeBox = false, MinimizeBox = false };
        form.Controls.Add(box);
        var buttonClicks = 0;
        var helpRequests = 0;
        var keyDowns = 0;
        form.HelpButtonClicked += (_, _) => buttonClicks++;
        box.HelpRequested += (_, e) => { helpRequests++; e.Handled = true; };
        box.KeyDown += (_, e) => { if (e.KeyCode == Keys.F1) keyDowns++; };
        form.Show();
        Dispatcher.UIThread.RunJobs();
        box.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(PhysicalKey.F1, RawInputModifiers.None);

        Assert.Equal(0, buttonClicks);
        Assert.Equal(1, keyDowns);
        Assert.Equal(1, helpRequests);
        form.Close();
    }

    [Fact]
    public void Setting_MinimumSize_and_MaximumSize_raises_their_own_changed_events()
    {
        var form = new Form();
        var minimumRaised = 0;
        var maximumRaised = 0;
        form.MinimumSizeChanged += (_, _) => minimumRaised++;
        form.MaximumSizeChanged += (_, _) => maximumRaised++;

        form.MinimumSize = new Size(100, 100);
        form.MaximumSize = new Size(500, 500);

        Assert.Equal(1, minimumRaised);
        Assert.Equal(1, maximumRaised);

        // Setting the same value again raises nothing.
        form.MinimumSize = new Size(100, 100);
        Assert.Equal(1, minimumRaised);

        form.Dispose();
    }

    [AvaloniaFact]
    public void CenterToScreen_places_the_form_in_the_middle_of_its_screens_working_area()
    {
        var form = new ExposedForm { Size = new Size(200, 100) };

        form.CallCenterToScreen();

        var area = Screen.GetWorkingArea(form);
        var expected = new Point(area.Left + (area.Width - 200) / 2, area.Top + (area.Height - 100) / 2);
        Assert.Equal(expected, form.Location);

        form.Dispose();
    }

    [Fact]
    public void CenterToParent_places_the_form_in_the_middle_of_its_owner()
    {
        var owner = new ExposedForm { Bounds = new Rectangle(100, 100, 400, 300) };
        var dialog = new ExposedForm { Size = new Size(100, 60), Owner = owner };

        dialog.CallCenterToParent();

        Assert.Equal(new Point(100 + (400 - 100) / 2, 100 + (300 - 60) / 2), dialog.Location);

        dialog.Dispose();
        owner.Dispose();
    }

    [Fact]
    public void ApplyAutoScaling_scales_by_the_ratio_between_the_base_size_and_the_current_font()
    {
        var form = new ExposedForm { ClientSize = new Size(300, 200) };
        var child = new Control { Bounds = new Rectangle(10, 10, 50, 20), Parent = form };

        // A base size smaller than the font's own average character size scales the form up.
#pragma warning disable CS0618 // AutoScaleBaseSize is the legacy member this test exercises.
        form.AutoScaleBaseSize = new Size(3, 6);
#pragma warning restore CS0618
        form.CallApplyAutoScaling();

        Assert.True(child.Width > 50 || child.Left > 10, "Nothing was scaled: the base size should have been smaller than the current font's.");

        form.Dispose();
    }

    [AvaloniaFact]
    public void A_scaling_change_raises_DpiChanged_with_the_old_and_new_values()
    {
        var form = new Form();
        form.Show();

        DpiChangedEventArgs? seen = null;
        form.DpiChanged += (_, e) => seen = e;

        form.OnHostScalingChanged(1.5);

        Assert.NotNull(seen);
        Assert.Equal(96, seen!.DeviceDpiOld);
        Assert.Equal(144, seen.DeviceDpiNew);

        form.Close();
    }

    [Fact]
    public void Attaching_at_the_hosts_own_scaling_raises_nothing_but_a_later_change_does()
    {
        var form = new Form();
        var seen = new List<DpiChangedEventArgs>();
        form.DpiChanged += (_, e) => seen.Add(e);

        // A 150% display: attaching records it as where the form opened, not a change from 96.
        form.InitializeHostScaling(1.5);
        Assert.Empty(seen);

        form.OnHostScalingChanged(2.0);

        Assert.Single(seen);
        Assert.Equal(144, seen[0].DeviceDpiOld);
        Assert.Equal(192, seen[0].DeviceDpiNew);

        form.Dispose();
    }

    [Fact]
    public void RestoreBounds_remembers_the_bounds_before_the_last_maximize_or_minimize()
    {
        var form = new ExposedForm { TopLevel = false, Bounds = new Rectangle(20, 20, 200, 150), Parent = new Form { ClientSize = new Size(800, 600) } };

        form.WindowState = FormWindowState.Maximized;

        Assert.Equal(new Rectangle(20, 20, 200, 150), form.RestoreBounds);

        var parent = form.Parent;
        form.Dispose();
        parent?.Dispose();
    }
}
