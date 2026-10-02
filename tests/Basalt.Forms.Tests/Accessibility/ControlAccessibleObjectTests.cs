using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Accessibility;

/// <summary>
/// Control.AccessibilityObject and the ControlAccessibleObject it is built
/// from: the lazily cached instance, the Accessible* properties a form
/// author sets by hand, the Label-and-mnemonic fallbacks WinForms applies
/// to Name and KeyboardShortcut, and the parts that only make sense once
/// the control is shown (Bounds, State, HitTest, GetFocused).
/// </summary>
public class ControlAccessibleObjectTests
{
    [Fact]
    public void AccessibilityObject_is_built_once_and_cached()
    {
        var control = new Panel();

        Assert.Same(control.AccessibilityObject, control.AccessibilityObject);
    }

    [Fact]
    public void The_accessible_objects_Owner_is_the_control_that_created_it()
    {
        var control = new Panel();
        var accessible = (Control.ControlAccessibleObject)control.AccessibilityObject;

        Assert.Same(control, accessible.Owner);
    }

    [Fact]
    public void AccessibleRole_rejects_a_value_the_enum_does_not_have()
    {
        var control = new Panel();

        Assert.Throws<InvalidEnumArgumentException>(() => control.AccessibleRole = (AccessibleRole)12345);
    }

    [Fact]
    public void DefaultAction_and_Description_mirror_the_owners_accessible_properties()
    {
        var control = new Panel
        {
            AccessibleDefaultActionDescription = "Press",
            AccessibleDescription = "A panel",
        };

        Assert.Equal("Press", control.AccessibilityObject.DefaultAction);
        Assert.Equal("A panel", control.AccessibilityObject.Description);
    }

    [Fact]
    public void Role_is_Client_unless_the_control_sets_its_own()
    {
        var plain = new Panel();
        var withRole = new Panel { AccessibleRole = AccessibleRole.Grouping };

        Assert.Equal(AccessibleRole.Client, plain.AccessibilityObject.Role);
        Assert.Equal(AccessibleRole.Grouping, withRole.AccessibilityObject.Role);
    }

    [Fact]
    public void Parent_is_the_parent_controls_accessible_object()
    {
        var parent = new Panel();
        var child = new Label();
        parent.Controls.Add(child);

        Assert.Same(parent.AccessibilityObject, child.AccessibilityObject.Parent);
        Assert.Null(parent.AccessibilityObject.Parent);
    }

    [Fact]
    public void Name_uses_AccessibleName_with_its_mnemonic_stripped()
    {
        var textBox = new TextBox { AccessibleName = "&User name" };

        Assert.Equal("User name", textBox.AccessibilityObject.Name);
    }

    [Fact]
    public void Name_falls_back_to_the_preceding_labels_text_in_tab_order()
    {
        var container = new Panel();
        var label = new Label { Text = "&Name:" };
        var textBox = new TextBox();
        container.Controls.Add(label);
        container.Controls.Add(textBox);

        Assert.Equal("Name:", textBox.AccessibilityObject.Name);
    }

    [Fact]
    public void Name_ignores_a_preceding_sibling_that_is_not_a_Label()
    {
        var container = new Panel();
        var notALabel = new TextBox();
        var textBox = new TextBox();
        container.Controls.Add(notALabel);
        container.Controls.Add(textBox);

        Assert.Null(textBox.AccessibilityObject.Name);
    }

    [Fact]
    public void Setting_Name_writes_through_to_AccessibleName()
    {
        var control = new Panel();

        control.AccessibilityObject.Name = "Caption";

        Assert.Equal("Caption", control.AccessibleName);
    }

    [Fact]
    public void KeyboardShortcut_comes_from_the_owners_own_mnemonic()
    {
        var button = new Button { Text = "&Save" };

        Assert.Equal("Alt+S", button.AccessibilityObject.KeyboardShortcut);
    }

    [Fact]
    public void KeyboardShortcut_falls_back_to_the_preceding_labels_mnemonic()
    {
        var container = new Panel();
        var label = new Label { Text = "&Name:" };
        var textBox = new TextBox();
        container.Controls.Add(label);
        container.Controls.Add(textBox);

        Assert.Equal("Alt+N", textBox.AccessibilityObject.KeyboardShortcut);
    }

    [Fact]
    public void KeyboardShortcut_is_null_without_any_mnemonic()
    {
        var button = new Button { Text = "Save" };

        Assert.Null(button.AccessibilityObject.KeyboardShortcut);
    }

    [Fact]
    public void Help_comes_from_the_QueryAccessibilityHelp_event()
    {
        var control = new Panel();
        control.QueryAccessibilityHelp += (_, e) => e.HelpString = "Press F1";

        Assert.Equal("Press F1", control.AccessibilityObject.Help);
    }

    [Fact]
    public void Help_is_null_when_nothing_answers_the_query()
    {
        var control = new Panel();

        Assert.Null(control.AccessibilityObject.Help);
    }

    [Fact]
    public void NotifyClients_overloads_are_callable_without_a_platform_bridge()
    {
        var accessible = (Control.ControlAccessibleObject)new Panel().AccessibilityObject;

        accessible.NotifyClients(AccessibleEvents.NameChange);
        accessible.NotifyClients(AccessibleEvents.NameChange, 1);
        accessible.NotifyClients(AccessibleEvents.NameChange, 1, 2);
    }

    [Fact]
    public void ToString_identifies_the_owner()
    {
        var control = new Panel();

        Assert.StartsWith("ControlAccessibleObject: Owner = ", control.AccessibilityObject.ToString()!);
    }

    [Fact]
    public void Bounds_are_empty_before_the_control_has_a_handle()
    {
        var control = new Panel { Bounds = new Rectangle(10, 20, 80, 40) };

        Assert.Equal(Rectangle.Empty, control.AccessibilityObject.Bounds);
    }

    [AvaloniaFact]
    public void Bounds_are_the_owners_RectangleToScreen_once_shown()
    {
        var form = ShowWith(new Panel { Location = new Point(10, 20), Size = new Size(80, 40) }, out var control);

        var expected = control.RectangleToScreen(new Rectangle(Point.Empty, control.Size));

        Assert.NotEqual(Rectangle.Empty, expected);
        Assert.Equal(expected, control.AccessibilityObject.Bounds);

        form.Close();
    }

    [AvaloniaFact]
    public void Bounds_move_by_the_same_amount_the_owner_moves()
    {
        var form = ShowWith(new Panel { Location = new Point(10, 20), Size = new Size(80, 40) }, out var control);

        var before = control.AccessibilityObject.Bounds;
        control.Location = new Point(control.Location.X + 50, control.Location.Y);
        var after = control.AccessibilityObject.Bounds;

        Assert.Equal(50, after.X - before.X);
        Assert.Equal(before.Y, after.Y);
        Assert.Equal(before.Size, after.Size);

        form.Close();
    }

    [AvaloniaFact]
    public void State_reports_Unavailable_Invisible_and_Focused_as_the_control_changes()
    {
        var form = new Form { ClientSize = new Size(300, 100) };
        var other = new TextBox();
        var control = new TextBox();
        form.Controls.Add(other);
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(control.AccessibilityObject.State.HasFlag(AccessibleStates.Focusable));

        // The form auto-focuses its first control, not this one.
        other.Focus();
        Assert.False(control.AccessibilityObject.State.HasFlag(AccessibleStates.Focused));

        control.Focus();
        Assert.True(control.AccessibilityObject.State.HasFlag(AccessibleStates.Focused));

        control.Enabled = false;
        Assert.True(control.AccessibilityObject.State.HasFlag(AccessibleStates.Unavailable));
        control.Enabled = true;

        control.Visible = false;
        Assert.True(control.AccessibilityObject.State.HasFlag(AccessibleStates.Invisible));

        form.Close();
    }

    [AvaloniaFact]
    public void GetFocused_finds_the_descendant_that_has_the_focus()
    {
        var form = new Form { ClientSize = new Size(300, 200) };
        var first = new TextBox();
        var second = new TextBox();
        form.Controls.Add(first);
        form.Controls.Add(second);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        second.Focus();

        Assert.Same(second.AccessibilityObject, form.AccessibilityObject.GetFocused());

        form.Close();
    }

    [AvaloniaFact]
    public void HitTest_finds_the_child_under_a_screen_point_and_nothing_far_away()
    {
        var form = ShowWith(new Panel { Location = new Point(10, 10), Size = new Size(50, 50) }, out var control, formSize: new Size(300, 200));

        var inside = control.RectangleToScreen(new Rectangle(Point.Empty, control.Size)).Location;
        inside.Offset(5, 5);

        Assert.Same(control.AccessibilityObject, form.AccessibilityObject.HitTest(inside.X, inside.Y));
        Assert.Null(form.AccessibilityObject.HitTest(-1000, -1000));

        form.Close();
    }

    private static Form ShowWith(Control control, out Control shown, Size? formSize = null)
    {
        var form = new Form { ClientSize = formSize ?? new Size(200, 100) };
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        shown = control;
        return form;
    }

    [Fact]
    public void A_button_is_named_by_its_own_caption_not_by_the_label_before_it()
    {
        var panel = new Panel();
        var label = new Label { Text = "&Unrelated", TabIndex = 0 };
        var button = new Button { Text = "&Save", TabIndex = 1 };
        panel.Controls.AddRange([label, button]);

        Assert.Equal("Save", button.AccessibilityObject.Name);
        Assert.Equal("Alt+S", button.AccessibilityObject.KeyboardShortcut);
    }

    [Fact]
    public void A_tab_page_is_named_by_its_tab_caption()
    {
        var page = new TabPage { Text = "&General" };

        Assert.Equal("General", page.AccessibilityObject.Name);
    }
}
