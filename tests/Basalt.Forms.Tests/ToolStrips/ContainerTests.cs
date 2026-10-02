using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.ToolStrips;

/// <summary>
/// ToolStripContainer's four panels docked around its content panel, and
/// ToolStripPanel/ToolStripPanelRow laying strips out in rows and sizing
/// themselves to fit.
/// </summary>
public class ContainerTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static ToolStrip Strip(int width, int height) => new() { Font = Font, AutoSize = false, Size = new Size(width, height), GripStyle = ToolStripGripStyle.Hidden };

    // --- ToolStripContainer ---------------------------------------------------

    [Fact]
    public void The_four_panels_and_the_content_panel_are_distinct_children()
    {
        var container = new ToolStripContainer { Size = new Size(400, 300) };

        Assert.Contains(container.TopToolStripPanel, container.Controls.Cast<Control>());
        Assert.Contains(container.BottomToolStripPanel, container.Controls.Cast<Control>());
        Assert.Contains(container.LeftToolStripPanel, container.Controls.Cast<Control>());
        Assert.Contains(container.RightToolStripPanel, container.Controls.Cast<Control>());
        Assert.Contains(container.ContentPanel, container.Controls.Cast<Control>());

        Assert.Equal(DockStyle.Top, container.TopToolStripPanel.Dock);
        Assert.Equal(DockStyle.Bottom, container.BottomToolStripPanel.Dock);
        Assert.Equal(DockStyle.Left, container.LeftToolStripPanel.Dock);
        Assert.Equal(DockStyle.Right, container.RightToolStripPanel.Dock);
        Assert.Equal(DockStyle.Fill, container.ContentPanel.Dock);
    }

    [Fact]
    public void The_top_and_bottom_panels_span_the_full_width_and_the_content_panel_fills_the_middle()
    {
        var container = new ToolStripContainer { Size = new Size(400, 300) };
        container.TopToolStripPanel.Join(Strip(100, 24));
        container.BottomToolStripPanel.Join(Strip(100, 22));
        container.PerformLayout();

        Assert.Equal(0, container.TopToolStripPanel.Bounds.Top);
        Assert.Equal(400, container.TopToolStripPanel.Bounds.Width);
        Assert.Equal(400, container.BottomToolStripPanel.Bounds.Width);
        Assert.Equal(container.ClientSize.Height, container.BottomToolStripPanel.Bounds.Bottom);

        // The content panel starts below the top panel and ends above the bottom one.
        Assert.True(container.ContentPanel.Bounds.Top >= container.TopToolStripPanel.Bounds.Bottom);
        Assert.True(container.ContentPanel.Bounds.Bottom <= container.BottomToolStripPanel.Bounds.Top);
    }

    [Fact]
    public void Hiding_a_panel_leaves_more_room_for_the_content_panel()
    {
        var container = new ToolStripContainer { Size = new Size(400, 300) };
        container.TopToolStripPanel.Join(Strip(100, 24));
        container.PerformLayout();
        var shrunkHeight = container.ContentPanel.Height;

        container.TopToolStripPanelVisible = false;
        container.PerformLayout();

        Assert.False(container.TopToolStripPanel.Visible);
        Assert.True(container.ContentPanel.Height > shrunkHeight);
    }

    // --- ToolStripPanel / ToolStripPanelRow -----------------------------------

    [Fact]
    public void Joining_a_strip_creates_a_row_that_holds_it()
    {
        var panel = new ToolStripPanel { Dock = DockStyle.None, Size = new Size(300, 100) };
        var strip = Strip(120, 24);

        panel.Join(strip);

        Assert.Single(panel.Rows);
        Assert.Contains(strip, panel.Rows[0].Controls);
        Assert.Same(panel, strip.Parent);
    }

    [Fact]
    public void Joining_two_strips_without_a_row_number_shares_the_same_row()
    {
        var panel = new ToolStripPanel { Dock = DockStyle.None, Size = new Size(300, 100) };
        var first = Strip(100, 24);
        var second = Strip(80, 22);

        panel.Join(first);
        panel.Join(second);

        Assert.Single(panel.Rows);
        Assert.Equal(2, panel.Rows[0].Controls.Length);

        // Side by side along the row's horizontal axis: the second starts where the first ends.
        Assert.Equal(first.Bounds.Right, second.Bounds.Left);
    }

    [Fact]
    public void Joining_a_strip_at_an_explicit_row_number_starts_a_new_row()
    {
        var panel = new ToolStripPanel { Dock = DockStyle.None, Size = new Size(300, 100) };
        var first = Strip(100, 24);
        var second = Strip(80, 22);

        panel.Join(first, 0);
        panel.Join(second, 1);

        Assert.Equal(2, panel.Rows.Length);
        Assert.Contains(first, panel.Rows[0].Controls);
        Assert.Contains(second, panel.Rows[1].Controls);

        // Rows stack vertically (the panel's default orientation is horizontal): the second row is below the first.
        Assert.True(panel.Rows[1].Bounds.Top >= panel.Rows[0].Bounds.Bottom);
    }

    [Fact]
    public void A_horizontal_panel_autosizes_to_the_height_of_its_rows()
    {
        var panel = new ToolStripPanel { Dock = DockStyle.None, AutoSize = true, Size = new Size(300, 1) };
        panel.Join(Strip(100, 30));

        panel.PerformLayout();

        Assert.Equal(30, panel.Height);
    }

    [Fact]
    public void Docking_left_or_right_switches_the_panel_to_vertical()
    {
        var panel = new ToolStripPanel();

        panel.Dock = DockStyle.Left;
        Assert.Equal(Orientation.Vertical, panel.Orientation);

        panel.Dock = DockStyle.Top;
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
    }

    [Fact]
    public void Removing_the_only_strip_in_a_row_drops_the_row()
    {
        var panel = new ToolStripPanel { Dock = DockStyle.None, Size = new Size(300, 100) };
        var strip = Strip(100, 24);
        panel.Join(strip);
        Assert.Single(panel.Rows);

        panel.Controls.Remove(strip);

        Assert.Empty(panel.Rows);
    }
}
