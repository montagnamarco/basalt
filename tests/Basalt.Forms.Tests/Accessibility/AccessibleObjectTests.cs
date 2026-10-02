using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Accessibility;

/// <summary>
/// AccessibleObject on its own: WinForms' defaults for an object nobody has
/// hooked up to a control, and the children-based logic (Navigate, HitTest,
/// GetFocused, GetSelected) that a Control's own accessible object inherits
/// without overriding any of it itself.
/// </summary>
public class AccessibleObjectTests
{
    [Fact]
    public void A_plain_accessible_object_answers_with_WinForms_defaults()
    {
        var accessible = new AccessibleObject();

        Assert.Equal(Rectangle.Empty, accessible.Bounds);
        Assert.Null(accessible.Name);
        Assert.Null(accessible.Value);
        Assert.Null(accessible.Description);
        Assert.Null(accessible.DefaultAction);
        Assert.Null(accessible.Help);
        Assert.Null(accessible.KeyboardShortcut);
        Assert.Null(accessible.Parent);
        Assert.Equal(AccessibleRole.None, accessible.Role);
        Assert.Equal(AccessibleStates.None, accessible.State);
        Assert.Equal(-1, accessible.GetChildCount());
    }

    [Fact]
    public void Name_and_Value_are_plain_settable_properties()
    {
        var accessible = new AccessibleObject { Name = "Caption", Value = "42" };

        Assert.Equal("Caption", accessible.Name);
        Assert.Equal("42", accessible.Value);
    }

    [Fact]
    public void DoDefaultAction_and_Select_are_callable_no_ops_without_an_owner()
    {
        var accessible = new AccessibleObject();

        // Nothing to assert beyond "did not throw": a plain AccessibleObject
        // has no control behind it to act on.
        accessible.DoDefaultAction();
        accessible.Select(AccessibleSelection.TakeFocus);
    }

    [Fact]
    public void GetChild_out_of_range_throws_instead_of_returning_a_sentinel()
    {
        var accessible = new AccessibleObject();

        Assert.Throws<ArgumentOutOfRangeException>(() => accessible.GetChild(0));
    }

    [Fact]
    public void A_controls_accessible_object_counts_only_its_visible_children()
    {
        var parent = new Panel();
        var first = new Label();
        var hidden = new Label { Visible = false };
        var second = new Label();
        parent.Controls.Add(first);
        parent.Controls.Add(hidden);
        parent.Controls.Add(second);

        var accessible = parent.AccessibilityObject;

        Assert.Equal(2, accessible.GetChildCount());
        Assert.Same(first.AccessibilityObject, accessible.GetChild(0));
        Assert.Same(second.AccessibilityObject, accessible.GetChild(1));
    }

    [Fact]
    public void Navigate_FirstChild_and_LastChild_use_the_visible_children()
    {
        var parent = new Panel();
        var first = new Label();
        var last = new Label();
        parent.Controls.Add(first);
        parent.Controls.Add(last);

        var accessible = parent.AccessibilityObject;

        Assert.Same(first.AccessibilityObject, accessible.Navigate(AccessibleNavigation.FirstChild));
        Assert.Same(last.AccessibilityObject, accessible.Navigate(AccessibleNavigation.LastChild));
    }

    [Fact]
    public void Navigate_on_a_childless_object_finds_nothing()
    {
        var accessible = new Label().AccessibilityObject;

        Assert.Null(accessible.Navigate(AccessibleNavigation.FirstChild));
    }

    private sealed class FocusedSelectedLeaf : AccessibleObject
    {
        public override AccessibleStates State => AccessibleStates.Focused | AccessibleStates.Selected;
    }

    [Fact]
    public void A_leaf_that_has_the_focus_and_the_selection_answers_GetFocused_and_GetSelected_with_itself()
    {
        var leaf = new FocusedSelectedLeaf();

        Assert.Same(leaf, leaf.GetFocused());
        Assert.Same(leaf, leaf.GetSelected());
    }
}
