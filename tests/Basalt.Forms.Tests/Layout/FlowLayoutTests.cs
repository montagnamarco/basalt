using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Layout;

/// <summary>
/// FlowLayoutPanel and the FlowLayout engine: flowing children in order,
/// wrapping, flow breaks, margins, stretching and auto-sizing.
/// </summary>
public class FlowLayoutTests
{
    [Fact]
    public void Children_flow_one_after_another_with_their_own_margins()
    {
        var panel = new FlowLayoutPanel { Size = new Size(300, 100) };
        var a = new Control { Size = new Size(50, 20), Margin = new Padding(2) };
        var b = new Control { Size = new Size(60, 30), Margin = new Padding(4) };

        panel.Controls.Add(a);
        panel.Controls.Add(b);

        Assert.Equal(new Rectangle(2, 2, 50, 20), a.Bounds);
        Assert.Equal(new Rectangle(58, 4, 60, 30), b.Bounds);
    }

    [Fact]
    public void A_row_wraps_when_the_next_child_would_not_fit()
    {
        var panel = new FlowLayoutPanel { Size = new Size(220, 200) };
        var a = new Control { Size = new Size(100, 20) };
        var b = new Control { Size = new Size(100, 20) };
        var c = new Control { Size = new Size(100, 20) };

        panel.Controls.AddRange(a, b, c);

        Assert.Equal(new Rectangle(3, 3, 100, 20), a.Bounds);
        Assert.Equal(new Rectangle(109, 3, 100, 20), b.Bounds);
        Assert.Equal(new Rectangle(3, 29, 100, 20), c.Bounds);
    }

    [Fact]
    public void WrapContents_false_keeps_every_child_on_the_one_row()
    {
        var panel = new FlowLayoutPanel { Size = new Size(220, 200), WrapContents = false };
        var a = new Control { Size = new Size(100, 20) };
        var b = new Control { Size = new Size(100, 20) };
        var c = new Control { Size = new Size(100, 20) };

        panel.Controls.AddRange(a, b, c);

        Assert.Equal(new Rectangle(3, 3, 100, 20), a.Bounds);
        Assert.Equal(new Rectangle(109, 3, 100, 20), b.Bounds);
        Assert.Equal(new Rectangle(215, 3, 100, 20), c.Bounds);
    }

    [Fact]
    public void A_flow_break_ends_a_row_even_when_more_would_still_fit()
    {
        var panel = new FlowLayoutPanel { Size = new Size(400, 200) };
        var a = new Control { Size = new Size(50, 20) };
        var b = new Control { Size = new Size(50, 20) };
        var c = new Control { Size = new Size(50, 20) };

        panel.Controls.AddRange(a, b, c);
        panel.SetFlowBreak(a, true);

        Assert.Equal(new Rectangle(3, 3, 50, 20), a.Bounds);
        Assert.Equal(new Rectangle(3, 29, 50, 20), b.Bounds);
        Assert.Equal(new Rectangle(59, 29, 50, 20), c.Bounds);
    }

    [Fact]
    public void SetFlowBreak_is_read_back_by_GetFlowBreak()
    {
        var panel = new FlowLayoutPanel();
        var control = new Control();
        panel.Controls.Add(control);

        Assert.False(panel.GetFlowBreak(control));

        panel.SetFlowBreak(control, true);

        Assert.True(panel.GetFlowBreak(control));
    }

    [Fact]
    public void TopDown_flows_into_columns_that_wrap_on_height()
    {
        var panel = new FlowLayoutPanel { Size = new Size(200, 120), FlowDirection = FlowDirection.TopDown };
        var a = new Control { Size = new Size(40, 50) };
        var b = new Control { Size = new Size(40, 50) };
        var c = new Control { Size = new Size(40, 50) };

        panel.Controls.AddRange(a, b, c);

        Assert.Equal(new Rectangle(3, 3, 40, 50), a.Bounds);
        Assert.Equal(new Rectangle(3, 59, 40, 50), b.Bounds);
        Assert.Equal(new Rectangle(49, 3, 40, 50), c.Bounds);
    }

    [Fact]
    public void RightToLeft_direction_fills_each_row_from_the_far_edge()
    {
        var panel = new FlowLayoutPanel { Size = new Size(200, 50), FlowDirection = FlowDirection.RightToLeft };
        var a = new Control { Size = new Size(40, 20) };

        panel.Controls.Add(a);

        Assert.Equal(new Rectangle(157, 3, 40, 20), a.Bounds);
    }

    [Fact]
    public void A_right_to_left_container_mirrors_a_left_to_right_flow_the_same_way()
    {
        var panel = new FlowLayoutPanel { Size = new Size(200, 50), RightToLeft = RightToLeft.Yes };
        var a = new Control { Size = new Size(40, 20) };

        panel.Controls.Add(a);

        Assert.Equal(new Rectangle(157, 3, 40, 20), a.Bounds);
    }

    [Fact]
    public void RightToLeft_direction_and_a_right_to_left_container_cancel_each_other()
    {
        var panel = new FlowLayoutPanel
        {
            Size = new Size(200, 50),
            FlowDirection = FlowDirection.RightToLeft,
            RightToLeft = RightToLeft.Yes,
        };
        var a = new Control { Size = new Size(40, 20) };

        panel.Controls.Add(a);

        Assert.Equal(new Rectangle(3, 3, 40, 20), a.Bounds);
    }

    [Fact]
    public void A_child_anchored_top_and_bottom_stretches_to_its_rows_height()
    {
        var panel = new FlowLayoutPanel { Size = new Size(300, 100) };
        var tall = new Control { Size = new Size(40, 80), Margin = Padding.Empty };
        var stretchy = new Control
        {
            Size = new Size(40, 20),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom,
            Margin = Padding.Empty,
        };

        panel.Controls.AddRange(tall, stretchy);

        Assert.Equal(new Rectangle(0, 0, 40, 80), tall.Bounds);
        Assert.Equal(new Rectangle(40, 0, 40, 80), stretchy.Bounds);
    }

    [Fact]
    public void An_auto_sized_panel_measures_every_child_on_the_one_unconstrained_row()
    {
        var panel = new FlowLayoutPanel { AutoSize = true };
        var a = new Control { Size = new Size(50, 20) };
        var b = new Control { Size = new Size(60, 30) };

        panel.Controls.AddRange(a, b);

        Assert.Equal(new Size(122, 36), panel.PreferredSize);
    }

    [Fact]
    public void FlowDirection_defaults_to_LeftToRight_and_WrapContents_to_true()
    {
        var panel = new FlowLayoutPanel();

        Assert.Equal(FlowDirection.LeftToRight, panel.FlowDirection);
        Assert.True(panel.WrapContents);
    }
}
