using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>Splitter, SplitContainer and SplitterPanel: resizing a sibling or a panel by dragging or stepping a bar.</summary>
public class SplitterTests
{
    [Theory]
    [InlineData(FixedPanel.None, 102)]
    [InlineData(FixedPanel.Panel1, 52)]
    [InlineData(FixedPanel.Panel2, 152)]
    public void Resizing_the_container_moves_the_distance_according_to_FixedPanel(FixedPanel fixedPanel, int expectedDistance)
    {
        var container = new SplitContainer { Size = new Size(104, 100) };
        container.SplitterDistance = 52; // exactly half of the 104-wide client area
        container.FixedPanel = fixedPanel;

        container.Width = 204; // doubling the extent makes every case land on a whole number

        Assert.Equal(expectedDistance, container.SplitterDistance);
    }

    [Fact]
    public void SplitterDistance_is_clamped_between_the_two_panels_minimum_sizes()
    {
        var container = new SplitContainer { Size = new Size(100, 100), Panel1MinSize = 40, Panel2MinSize = 40 };

        container.SplitterDistance = 10;
        Assert.Equal(40, container.SplitterDistance);

        container.SplitterDistance = 90;
        Assert.Equal(56, container.SplitterDistance); // 100 - SplitterWidth(4) - Panel2MinSize(40)
    }

    [Fact]
    public void Collapsing_a_panel_gives_the_other_the_whole_client_area()
    {
        var container = new SplitContainer { Size = new Size(200, 100) };

        container.Panel1Collapsed = true;

        Assert.False(container.Panel1.Visible);
        Assert.True(container.Panel2.Visible);
        Assert.Equal(new Size(200, 100), container.Panel2.Size);

        container.Panel1Collapsed = false;
        container.Panel2Collapsed = true;

        Assert.False(container.Panel2.Visible);
        Assert.True(container.Panel1.Visible);
        Assert.Equal(new Size(200, 100), container.Panel1.Size);
    }

    [Fact]
    public void Collapsing_both_panels_leaves_only_the_most_recent_one_collapsed()
    {
        var container = new SplitContainer { Size = new Size(200, 100) };

        container.Panel1Collapsed = true;
        container.Panel2Collapsed = true;

        Assert.False(container.Panel1Collapsed);
        Assert.True(container.Panel2Collapsed);
    }

    [AvaloniaFact]
    public void Dragging_the_SplitContainers_splitter_raises_SplitterMoved_with_the_new_distance()
    {
        var container = new SplitContainer { Size = new Size(200, 100), SplitterDistance = 50 };
        var form = new Form { ClientSize = new Size(220, 120) };
        form.Controls.Add(container);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        var reportedDistance = -1;
        container.SplitterMoved += (_, _) => reportedDistance = container.SplitterDistance;

        // The splitter bar sits at x in [50, 54) within the container.
        form.Host!.MouseDown(new Avalonia.Point(52, 50), MouseButton.Left);
        form.Host!.MouseMove(new Avalonia.Point(82, 50));
        form.Host!.MouseUp(new Avalonia.Point(82, 50), MouseButton.Left);

        Assert.Equal(80, reportedDistance);
        Assert.Equal(80, container.SplitterDistance);

        form.Close();
    }

    [AvaloniaFact]
    public void A_cancelled_SplitterMoving_leaves_the_SplitContainers_distance_untouched()
    {
        var container = new SplitContainer { Size = new Size(200, 100), SplitterDistance = 50 };
        container.SplitterMoving += (_, e) => e.Cancel = true;
        var form = new Form { ClientSize = new Size(220, 120) };
        form.Controls.Add(container);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        form.Host!.MouseDown(new Avalonia.Point(52, 50), MouseButton.Left);
        form.Host!.MouseMove(new Avalonia.Point(82, 50));
        form.Host!.MouseUp(new Avalonia.Point(82, 50), MouseButton.Left);

        Assert.Equal(50, container.SplitterDistance);

        form.Close();
    }

    [AvaloniaFact]
    public void The_right_arrow_key_moves_the_focused_SplitContainers_splitter_by_SplitterIncrement()
    {
        var container = new SplitContainer { Size = new Size(200, 100), SplitterDistance = 50, SplitterIncrement = 5 };
        var form = new Form { ClientSize = new Size(220, 120) };
        form.Controls.Add(container);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        container.Focus();

        var moved = InvokeProcessDialogKey(container, Keys.Right);

        Assert.True(moved);
        Assert.Equal(55, container.SplitterDistance);

        form.Close();
    }

    [AvaloniaFact]
    public void A_fixed_splitter_ignores_the_keyboard_even_while_focused()
    {
        var container = new SplitContainer { Size = new Size(200, 100), SplitterDistance = 50, IsSplitterFixed = true };
        var form = new Form { ClientSize = new Size(220, 120) };
        form.Controls.Add(container);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        container.Focus();

        InvokeProcessDialogKey(container, Keys.Right);

        Assert.Equal(50, container.SplitterDistance);

        form.Close();
    }

    [Fact]
    public void A_SplitContainer_refuses_a_control_added_directly_to_it()
    {
        var container = new SplitContainer();

        Assert.Throws<ArgumentException>(() => container.Controls.Add(new Panel()));
    }

    [Fact]
    public void Setting_SplitPosition_resizes_the_control_the_splitter_follows()
    {
        var form = BuildDockedTrio(out var left, out var splitter, out _);
        form.PerformLayout();

        Assert.Equal(60, left.Width);

        splitter.SplitPosition = 90;

        Assert.Equal(90, left.Width);
    }

    [Fact]
    public void A_Splitter_without_a_matching_docked_neighbour_has_no_SplitPosition()
    {
        var splitter = new Splitter();

        Assert.Equal(-1, splitter.SplitPosition);
    }

    [AvaloniaFact]
    public void Dragging_a_Splitter_resizes_the_control_it_follows_and_raises_SplitterMoved()
    {
        var form = BuildDockedTrio(out var left, out var splitter, out _);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(60, left.Width);

        var raised = false;
        splitter.SplitterMoved += (_, _) => raised = true;

        // The splitter is docked Left right after the 60-wide left panel, so it sits at x in [60, 63).
        form.Host!.MouseDown(new Avalonia.Point(61, 50), MouseButton.Left);
        form.Host!.MouseMove(new Avalonia.Point(91, 50));
        form.Host!.MouseUp(new Avalonia.Point(91, 50), MouseButton.Left);

        Assert.True(raised);
        Assert.Equal(90, left.Width);

        form.Close();
    }

    [AvaloniaFact]
    public void Escape_during_a_drag_puts_the_control_back_though_the_splitter_has_no_focus()
    {
        var form = BuildDockedTrio(out var left, out var splitter, out var fill);
        var focusable = new TextBox { Parent = fill };
        form.Show();
        Dispatcher.UIThread.RunJobs();
        focusable.Focus();

        form.Host!.MouseDown(new Avalonia.Point(61, 50), MouseButton.Left);
        form.Host!.MouseMove(new Avalonia.Point(91, 50));
        form.Host!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Equal(60, left.Width);
        Assert.False(splitter.Capture);

        form.Host!.MouseUp(new Avalonia.Point(91, 50), MouseButton.Left);
        form.Close();
    }

    /// <summary>A form with Fill, then a Splitter, then a 60-wide Left panel: the order real WinForms code docks them in, so the splitter follows the last one.</summary>
    private static Form BuildDockedTrio(out Panel left, out Splitter splitter, out Panel fill)
    {
        var form = new Form { ClientSize = new Size(300, 150) };

        fill = new Panel { Dock = DockStyle.Fill };
        splitter = new Splitter { Dock = DockStyle.Left };
        left = new Panel { Dock = DockStyle.Left, Width = 60 };

        form.Controls.Add(fill);
        form.Controls.Add(splitter);
        form.Controls.Add(left);

        return form;
    }

    private static bool InvokeProcessDialogKey(Control control, Keys keyData) =>
        (bool)typeof(Control).GetMethod("ProcessDialogKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(control, [keyData])!;
}
