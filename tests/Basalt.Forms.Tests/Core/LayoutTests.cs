using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Core;

/// <summary>
/// The default layout: docking in reverse z-order, anchoring by the
/// distances measured when bounds were set, growing auto-sized children.
/// </summary>
public class LayoutTests
{
    private static Control Container(int width, int height) => new() { Size = new Size(width, height) };

    [Fact]
    public void Docked_children_take_their_edges_from_the_back_of_the_z_order()
    {
        var container = Container(200, 100);
        var top = new Control { Height = 10, Dock = DockStyle.Top };
        var left = new Control { Width = 30, Dock = DockStyle.Left };
        var fill = new Control { Dock = DockStyle.Fill };

        // Added first means at the back, so docked first: fill is added first to be laid out last.
        container.Controls.Add(fill);
        container.Controls.Add(left);
        container.Controls.Add(top);

        Assert.Equal(new Rectangle(0, 0, 200, 10), top.Bounds);
        Assert.Equal(new Rectangle(0, 10, 30, 90), left.Bounds);
        Assert.Equal(new Rectangle(30, 10, 170, 90), fill.Bounds);
    }

    [Fact]
    public void Bringing_a_docked_child_to_the_front_lays_it_out_last()
    {
        var container = Container(200, 100);
        var top = new Control { Height = 10, Dock = DockStyle.Top };
        var left = new Control { Width = 30, Dock = DockStyle.Left };
        container.Controls.Add(top);
        container.Controls.Add(left);

        Assert.Equal(new Rectangle(0, 0, 30, 100), left.Bounds);
        Assert.Equal(new Rectangle(30, 0, 170, 10), top.Bounds);

        left.BringToFront();

        Assert.Equal(new Rectangle(0, 0, 200, 10), top.Bounds);
        Assert.Equal(new Rectangle(0, 10, 30, 90), left.Bounds);
    }

    [Fact]
    public void Docked_children_follow_the_container_when_it_resizes()
    {
        var container = Container(200, 100);
        var bottom = new Control { Height = 20, Dock = DockStyle.Bottom, Parent = container };

        container.Size = new Size(300, 150);

        Assert.Equal(new Rectangle(0, 130, 300, 20), bottom.Bounds);
    }

    [Fact]
    public void Undocking_returns_to_the_bounds_the_user_set()
    {
        var container = Container(200, 100);
        var child = new Control { Bounds = new Rectangle(5, 6, 40, 30), Parent = container };

        child.Dock = DockStyle.Fill;
        Assert.Equal(new Rectangle(0, 0, 200, 100), child.Bounds);

        child.Dock = DockStyle.None;
        Assert.Equal(new Rectangle(5, 6, 40, 30), child.Bounds);
    }

    [Fact]
    public void A_child_anchored_right_and_bottom_keeps_its_distance_from_those_edges()
    {
        var container = Container(200, 100);
        var child = new Control { Bounds = new Rectangle(150, 70, 40, 20), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
        container.Controls.Add(child);

        container.Size = new Size(300, 160);

        Assert.Equal(new Rectangle(250, 130, 40, 20), child.Bounds);
    }

    [Fact]
    public void A_child_anchored_on_opposite_sides_stretches()
    {
        var container = Container(200, 100);
        var child = new Control { Bounds = new Rectangle(10, 10, 180, 20), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
        container.Controls.Add(child);

        container.Width = 260;

        Assert.Equal(new Rectangle(10, 10, 240, 20), child.Bounds);
    }

    [Fact]
    public void A_child_anchored_on_no_side_of_an_axis_stays_centred_on_it()
    {
        var container = Container(200, 100);
        var child = new Control { Bounds = new Rectangle(80, 10, 40, 20), Anchor = AnchorStyles.Top };
        container.Controls.Add(child);

        container.Width = 300;

        Assert.Equal(new Rectangle(130, 10, 40, 20), child.Bounds);
    }

    [Fact]
    public void Shrinking_past_a_stretched_child_collapses_it_without_inverting_it()
    {
        var container = Container(200, 100);
        var child = new Control { Bounds = new Rectangle(10, 10, 180, 20), Anchor = AnchorStyles.Left | AnchorStyles.Right };
        container.Controls.Add(child);

        container.Width = 5;

        Assert.Equal(0, child.Width);
    }

    [Fact]
    public void Anchors_measured_while_suspended_count_from_the_bounds_at_resume()
    {
        // As a designer file does: children placed, then the container sized, then layout resumed.
        var container = new Control();
        container.SuspendLayout();
        var child = new Control { Bounds = new Rectangle(250, 10, 40, 20), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        container.Controls.Add(child);
        container.Size = new Size(300, 100);
        container.ResumeLayout(false);
        container.PerformLayout();

        container.Width = 400;

        Assert.Equal(new Rectangle(350, 10, 40, 20), child.Bounds);
    }

    [Fact]
    public void A_suspended_container_lays_out_once_when_resumed()
    {
        var container = Container(200, 100);
        var layouts = 0;
        container.Layout += (_, _) => layouts++;

        container.SuspendLayout();
        container.Controls.Add(new Control());
        container.Controls.Add(new Control());
        container.Size = new Size(300, 300);
        Assert.Equal(0, layouts);

        container.ResumeLayout();
        Assert.Equal(1, layouts);
    }

    [Fact]
    public void The_first_cause_of_a_deferred_layout_is_the_one_reported()
    {
        var container = Container(200, 100);
        var child = new Control();
        string? property = null;
        container.Layout += (_, e) => property = e.AffectedProperty;

        container.SuspendLayout();
        container.Controls.Add(child);
        container.Size = new Size(300, 300);
        container.ResumeLayout();

        Assert.Equal("Parent", property);
    }

    [Fact]
    public void A_hidden_docked_child_gives_its_space_back()
    {
        var container = Container(200, 100);
        var fill = new Control { Dock = DockStyle.Fill, Parent = container };
        var top = new Control { Height = 30, Dock = DockStyle.Top, Parent = container };

        Assert.Equal(new Rectangle(0, 30, 200, 70), fill.Bounds);

        top.Visible = false;

        Assert.Equal(new Rectangle(0, 0, 200, 100), fill.Bounds);
    }

    [Fact]
    public void A_container_with_padding_docks_inside_it()
    {
        var container = new ScrollableControl { Size = new Size(200, 100), Padding = new Padding(5) };
        var fill = new Control { Dock = DockStyle.Fill, Parent = container };

        Assert.Equal(new Rectangle(5, 5, 190, 90), fill.Bounds);
    }
}
