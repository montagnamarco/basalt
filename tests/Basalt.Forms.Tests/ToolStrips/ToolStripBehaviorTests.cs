using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.ToolStrips;

/// <summary>
/// The real behaviour added for the ToolStrip's forwarding members: mutually
/// exclusive AllowDrop/AllowItemReorder, dragging an item to reorder it,
/// dragging the grip to raise BeginDrag/EndDrag, and the accessible object
/// ToolStrip.CreateAccessibilityInstance builds.
/// </summary>
public class ToolStripBehaviorTests
{
    private static void Invoke(Control control, string method, params object[] args) =>
        typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, args);

    [Fact]
    public void AllowItemReorder_and_AllowDrop_cannot_both_be_true()
    {
        var strip = new ToolStrip { AllowItemReorder = true };

        Assert.Throws<ArgumentException>(() => strip.AllowDrop = true);

        var other = new ToolStrip { AllowDrop = true };
        Assert.Throws<ArgumentException>(() => other.AllowItemReorder = true);
    }

    [Fact]
    public void Dragging_a_pressed_item_onto_another_swaps_their_order()
    {
        var strip = new ToolStrip { AllowItemReorder = true, AutoSize = false, Size = new Size(200, 25), GripStyle = ToolStripGripStyle.Hidden };
        var first = new ToolStripButton("First") { AutoSize = false, Size = new Size(50, 23) };
        var second = new ToolStripButton("Second") { AutoSize = false, Size = new Size(50, 23) };
        strip.Items.AddRange([first, second]);
        strip.PerformLayout();

        Invoke(strip, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, first.Bounds.Left + 1, first.Bounds.Top + 1, 0));
        Invoke(strip, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, second.Bounds.Left + 1, second.Bounds.Top + 1, 0));

        Assert.Equal(0, strip.Items.IndexOf(second));
        Assert.Equal(1, strip.Items.IndexOf(first));
    }

    [Fact]
    public void Reorder_does_not_happen_when_AllowItemReorder_is_false()
    {
        var strip = new ToolStrip { AutoSize = false, Size = new Size(200, 25), GripStyle = ToolStripGripStyle.Hidden };
        var first = new ToolStripButton("First") { AutoSize = false, Size = new Size(50, 23) };
        var second = new ToolStripButton("Second") { AutoSize = false, Size = new Size(50, 23) };
        strip.Items.AddRange([first, second]);
        strip.PerformLayout();

        Invoke(strip, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, first.Bounds.Left + 1, first.Bounds.Top + 1, 0));
        Invoke(strip, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, second.Bounds.Left + 1, second.Bounds.Top + 1, 0));

        Assert.Equal(0, strip.Items.IndexOf(first));
        Assert.Equal(1, strip.Items.IndexOf(second));
    }

    [Fact]
    public void Dragging_the_grip_raises_BeginDrag_and_EndDrag_and_sets_IsCurrentlyDragging()
    {
        var strip = new ToolStrip { GripStyle = ToolStripGripStyle.Visible, AutoSize = false, Size = new Size(200, 25) };
        var began = 0;
        var ended = 0;
        strip.BeginDrag += (_, _) => began++;
        strip.EndDrag += (_, _) => ended++;
        var gripPoint = strip.GripRectangle.Location + new Size(1, 1);

        Invoke(strip, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, gripPoint.X, gripPoint.Y, 0));

        Assert.True(strip.IsCurrentlyDragging);
        Assert.Equal(1, began);

        Invoke(strip, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, gripPoint.X, gripPoint.Y, 0));

        Assert.False(strip.IsCurrentlyDragging);
        Assert.Equal(1, ended);
    }

    [Fact]
    public void A_strips_accessible_object_counts_its_items_and_has_the_toolbar_role()
    {
        var strip = new ToolStrip();
        strip.Items.AddRange([new ToolStripButton("One"), new ToolStripButton("Two")]);

        var accessible = (ToolStrip.ToolStripAccessibleObject)strip.AccessibilityObject;

        Assert.Equal(AccessibleRole.ToolBar, accessible.Role);
        Assert.Equal(2, accessible.GetChildCount());
        Assert.Equal("Two", accessible.GetChild(1).Name);
    }

    [Fact]
    public void IsDropDown_is_true_only_for_a_ToolStripDropDown()
    {
        Assert.False(new ToolStrip().IsDropDown);
        Assert.True(new ToolStripDropDown().IsDropDown);
    }
}
