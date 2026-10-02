using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.ToolStrips;

/// <summary>
/// The real behaviour ToolStripDropDown's forwarding members add: Visible
/// routed through Show/Close, Tab closing the popup, a mnemonic working
/// through ProcessDialogChar, an open popup resizing when its items change,
/// a press-drag-release from the control that opened it still clicking, and
/// the accessible object ToolStripDropDown.CreateAccessibilityInstance builds.
/// </summary>
public class ToolStripDropDownBehaviorTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static void Invoke(Control control, string method, params object[] args) =>
        typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, args);

    private static bool InvokeProcessDialogKey(ToolStripDropDown dropDown, Keys keys) =>
        (bool)typeof(Control).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dropDown, [keys])!;

    private static bool InvokeProcessDialogChar(ToolStripDropDown dropDown, char character) =>
        (bool)typeof(Control).GetMethod("ProcessDialogChar", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dropDown, [character])!;

    /// <summary>
    /// Every drop-down needs a real owner window (not a bare, unowned
    /// popup): see MenuTests.ShowWith's own comment -- an unowned popup's
    /// Avalonia visual otherwise outlives the test, for ToolStripManager's
    /// weak list (and another test's window search) to trip over.
    /// </summary>
    private static (Form Form, Button Owner) ShowOwner()
    {
        var owner = new Button { Bounds = new Rectangle(10, 10, 80, 23) };
        var form = new Form { ClientSize = new Size(400, 200), Font = Font };
        form.Controls.Add(owner);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return (form, owner);
    }

    private static void CleanUp(ToolStripDropDown dropDown, Form form)
    {
        dropDown.Dispose();
        form.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Setting_Visible_true_opens_it_and_false_closes_it()
    {
        var (form, owner) = ShowOwner();
        var dropDown = new ToolStripDropDown { OwnerItem = null };
        dropDown.Items.Add(new ToolStripMenuItem("One"));

        try
        {
            dropDown.Show(owner, new Point(0, owner.Height));
            Dispatcher.UIThread.RunJobs();
            Assert.True(dropDown.Visible);

            dropDown.Visible = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(dropDown.Visible);

            dropDown.Visible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(dropDown.Visible);
        }
        finally
        {
            CleanUp(dropDown, form);
        }
    }

    [AvaloniaFact]
    public void Tab_closes_the_open_drop_down()
    {
        var (form, owner) = ShowOwner();
        var dropDown = new ToolStripDropDown();
        dropDown.Items.Add(new ToolStripMenuItem("One"));

        try
        {
            dropDown.Show(owner, new Point(0, owner.Height));
            Dispatcher.UIThread.RunJobs();

            var handled = InvokeProcessDialogKey(dropDown, Keys.Tab);

            Assert.True(handled);
            Assert.False(dropDown.Visible);
        }
        finally
        {
            CleanUp(dropDown, form);
        }
    }

    [AvaloniaFact]
    public void A_mnemonic_character_activates_the_matching_item_through_ProcessDialogChar()
    {
        var (form, owner) = ShowOwner();
        var dropDown = new ToolStripDropDown();
        var clicked = 0;
        var item = new ToolStripMenuItem("&Open");
        item.Click += (_, _) => clicked++;
        dropDown.Items.Add(item);

        try
        {
            dropDown.Show(owner, new Point(0, owner.Height));
            Dispatcher.UIThread.RunJobs();

            var handled = InvokeProcessDialogChar(dropDown, 'o');

            Assert.True(handled);
            Assert.Equal(1, clicked);
        }
        finally
        {
            CleanUp(dropDown, form);
        }
    }

    [AvaloniaFact]
    public void An_open_drop_down_resizes_when_an_item_is_added()
    {
        var (form, owner) = ShowOwner();
        var dropDown = new ToolStripDropDown();
        dropDown.Items.Add(new ToolStripMenuItem("One"));

        try
        {
            dropDown.Show(owner, new Point(0, owner.Height));
            Dispatcher.UIThread.RunJobs();
            var heightWithOneItem = dropDown.Height;

            dropDown.Items.Add(new ToolStripMenuItem("Two"));
            Dispatcher.UIThread.RunJobs();

            Assert.True(dropDown.Height > heightWithOneItem);
        }
        finally
        {
            CleanUp(dropDown, form);
        }
    }

    [AvaloniaFact]
    public void Releasing_the_mouse_over_an_item_clicks_it_even_without_a_matching_press_here()
    {
        var (form, owner) = ShowOwner();
        var dropDown = new ToolStripDropDown();
        var item = new ToolStripMenuItem("One") { AutoSize = false, Size = new Size(80, 20) };
        dropDown.Items.Add(item);

        try
        {
            dropDown.Show(owner, new Point(0, owner.Height));
            Dispatcher.UIThread.RunJobs();

            var clicked = 0;
            item.Click += (_, _) => clicked++;

            // No OnMouseDown on this drop-down first: the press is simulated as having happened
            // on whatever opened it (a combo box's button, a menu item), as a real drag-release does.
            Invoke(dropDown, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, item.Bounds.Left + 1, item.Bounds.Top + 1, 0));

            Assert.Equal(1, clicked);
        }
        finally
        {
            CleanUp(dropDown, form);
        }
    }

    [AvaloniaFact]
    public void Setting_a_ContextMenuStrips_Visible_true_opens_it_at_the_cursor_position()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("One"));

        try
        {
            menu.Visible = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(menu.Visible);
            Assert.Equal(Cursor.Position, menu.Location);
        }
        finally
        {
            menu.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void The_accessible_object_is_a_menu_popup_with_a_settable_name()
    {
        var dropDown = new ToolStripDropDown();

        var accessible = (ToolStripDropDown.ToolStripDropDownAccessibleObject)dropDown.AccessibilityObject;
        accessible.Name = "File menu";

        Assert.Equal(AccessibleRole.MenuPopup, accessible.Role);
        Assert.Equal("File menu", accessible.Name);

        dropDown.Dispose();
    }

    [AvaloniaFact]
    public void A_closed_drop_down_reports_Invisible_through_Controls_own_Visible_state()
    {
        var (form, owner) = ShowOwner();
        var dropDown = new ToolStripDropDown();
        dropDown.Items.Add(new ToolStripMenuItem("One"));

        try
        {
            dropDown.Show(owner, new Point(0, owner.Height));
            Dispatcher.UIThread.RunJobs();
            Assert.True(((Control)dropDown).Visible);
            Assert.False(dropDown.AccessibilityObject.State.HasFlag(AccessibleStates.Invisible));

            dropDown.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.False(((Control)dropDown).Visible);
            Assert.True(dropDown.AccessibilityObject.State.HasFlag(AccessibleStates.Invisible));
        }
        finally
        {
            CleanUp(dropDown, form);
        }
    }
}
