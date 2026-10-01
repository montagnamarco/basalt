using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Core;

/// <summary>Parents, children and z-order, as WinForms keeps them.</summary>
public class ControlTreeTests
{
    [Fact]
    public void Added_controls_go_to_the_back_and_index_zero_is_the_front()
    {
        var parent = new Control();
        var first = new Control { Name = "first" };
        var second = new Control { Name = "second" };

        parent.Controls.Add(first);
        parent.Controls.Add(second);

        Assert.Same(first, parent.Controls[0]);
        Assert.Same(second, parent.Controls[1]);

        second.BringToFront();
        Assert.Same(second, parent.Controls[0]);

        second.SendToBack();
        Assert.Same(second, parent.Controls[1]);
    }

    [Fact]
    public void Adding_a_control_to_another_parent_takes_it_from_the_first()
    {
        var one = new Control();
        var two = new Control();
        var child = new Control();
        var removed = 0;
        one.ControlRemoved += (_, _) => removed++;

        one.Controls.Add(child);
        two.Controls.Add(child);

        Assert.Empty(one.Controls.Cast<Control>());
        Assert.Same(two, child.Parent);
        Assert.Equal(1, removed);
    }

    [Fact]
    public void A_control_cannot_contain_its_ancestor()
    {
        var parent = new Control();
        var child = new Control { Parent = parent };

        Assert.Throws<ArgumentException>(() => child.Controls.Add(parent));
        Assert.Throws<ArgumentException>(() => parent.Controls.Add(parent));
    }

    [Fact]
    public void A_form_cannot_be_added_to_a_control()
    {
        Assert.Throws<ArgumentException>(() => new Control().Controls.Add(new Form()));
    }

    [Fact]
    public void Children_are_found_by_name_ignoring_case_and_deep_when_asked()
    {
        var root = new Control();
        var panel = new Control { Name = "panel", Parent = root };
        var button = new Control { Name = "OkButton", Parent = panel };

        Assert.Same(panel, root.Controls["PANEL"]);
        Assert.Null(root.Controls["okbutton"]);
        Assert.Equal([button], root.Controls.Find("okbutton", searchAllChildren: true));
        Assert.Empty(root.Controls.Find("okbutton", searchAllChildren: false));
    }

    [Fact]
    public void Tab_indexes_follow_the_order_of_adding()
    {
        var parent = new Control();
        var first = new Control { Parent = parent };
        var second = new Control { Parent = parent };
        var preset = new Control { TabIndex = 7 };
        parent.Controls.Add(preset);

        Assert.Equal(0, first.TabIndex);
        Assert.Equal(1, second.TabIndex);
        Assert.Equal(7, preset.TabIndex);
    }

    [Fact]
    public void Disposing_a_parent_disposes_its_children_without_ControlRemoved()
    {
        var parent = new Control();
        var child = new Control { Parent = parent };
        var removed = 0;
        parent.ControlRemoved += (_, _) => removed++;

        parent.Dispose();

        Assert.True(child.IsDisposed);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void Disposing_a_child_removes_it_from_its_parent()
    {
        var parent = new Control();
        var child = new Control { Parent = parent };

        child.Dispose();

        Assert.False(parent.HasChildren);
        Assert.Null(child.Parent);
    }

    [Fact]
    public void FindForm_and_TopLevelControl_walk_up_to_the_form()
    {
        var form = new Form();
        var panel = new Control { Parent = form };
        var button = new Control { Parent = panel };

        Assert.Same(form, button.FindForm());
        Assert.Same(form, button.TopLevelControl);
        Assert.True(form.Contains(button));
        Assert.False(button.Contains(form));
    }

    [Fact]
    public void A_child_takes_its_parents_colours_and_font_until_it_sets_its_own()
    {
        var parent = new Control { BackColor = Color.Red, ForeColor = Color.Blue };
        var child = new Control { Parent = parent };
        var changes = 0;
        child.BackColorChanged += (_, _) => changes++;

        Assert.Equal(Color.Red, child.BackColor);
        Assert.Equal(Color.Blue, child.ForeColor);

        parent.BackColor = Color.Green;
        Assert.Equal(Color.Green, child.BackColor);
        Assert.Equal(1, changes);

        child.BackColor = Color.Yellow;
        parent.BackColor = Color.Black;
        Assert.Equal(Color.Yellow, child.BackColor);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void A_transparent_background_needs_the_control_to_support_it()
    {
        Assert.Throws<ArgumentException>(() => new Control { BackColor = Color.FromArgb(128, 255, 0, 0) });
    }

    [Fact]
    public void Visible_and_Enabled_depend_on_the_parent_but_remember_their_own()
    {
        var parent = new Control();
        var child = new Control { Parent = parent };
        var visibleChanges = 0;
        child.VisibleChanged += (_, _) => visibleChanges++;

        parent.Visible = false;
        Assert.False(child.Visible);
        Assert.Equal(1, visibleChanges);

        child.Visible = false;
        parent.Visible = true;
        Assert.False(child.Visible);
        Assert.Equal(1, visibleChanges);

        parent.Enabled = false;
        Assert.False(child.Enabled);
        parent.Enabled = true;
        Assert.True(child.Enabled);
    }

    [Fact]
    public void A_handle_is_created_on_demand_and_finds_its_control()
    {
        var control = new Control();
        var created = 0;
        control.HandleCreated += (_, _) => created++;

        Assert.False(control.IsHandleCreated);

        var handle = control.Handle;

        Assert.True(control.IsHandleCreated);
        Assert.NotEqual(IntPtr.Zero, handle);
        Assert.Same(control, Control.FromHandle(handle));
        Assert.Equal(1, created);

        control.Dispose();
        Assert.Null(Control.FromHandle(handle));
    }

    [Fact]
    public void Children_added_to_a_created_control_are_created_with_it()
    {
        var parent = new Control();
        parent.CreateControl();
        var child = new Control { Parent = parent };
        var hidden = new Control { Visible = false, Parent = parent };

        Assert.True(child.Created);
        Assert.True(child.IsHandleCreated);
        Assert.False(hidden.IsHandleCreated);
    }

    private sealed class FormWithFields : Form
    {
        private readonly List<string> _log = [];

        public IReadOnlyList<string> Log => _log;

        protected override void OnVisibleChanged(EventArgs e)
        {
            _log.Add("VisibleChanged");
            base.OnVisibleChanged(e);
        }
    }

    [Fact]
    public void A_new_form_starts_hidden_without_calling_overrides_before_its_fields_exist()
    {
        var form = new FormWithFields();

        Assert.False(form.Visible);
        Assert.Empty(form.Log);
    }
}
