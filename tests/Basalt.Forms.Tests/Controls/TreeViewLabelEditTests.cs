using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>F2, a click on the selected label, and Enter/Escape driving a node's in-place edit.</summary>
public class TreeViewLabelEditTests
{
    private static Form ShowWith(TreeView tree)
    {
        var form = new Form { ClientSize = new Size(200, 150) };
        form.Controls.Add(tree);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    [AvaloniaFact]
    public void F2_opens_the_edit_box_on_the_selected_node()
    {
        var tree = new TreeView { LabelEdit = true };
        var node = tree.Nodes.Add("Alpha");
        tree.SelectedNode = node;
        var form = ShowWith(tree);
        tree.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);

        Assert.True(node.IsEditing);

        form.Close();
    }

    [Fact]
    public void F2_without_LabelEdit_does_not_start_editing()
    {
        var tree = new TreeView { LabelEdit = false };
        var node = tree.Nodes.Add("Alpha");
        tree.SelectedNode = node;

        typeof(TreeView).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(tree, [new System.Windows.Forms.KeyEventArgs(Keys.F2)]);

        Assert.False(node.IsEditing);
    }

    [Fact]
    public void BeforeLabelEdit_can_cancel_the_edit()
    {
        var tree = new TreeView { LabelEdit = true };
        var node = tree.Nodes.Add("Alpha");
        tree.BeforeLabelEdit += (_, e) => e.CancelEdit = true;

        node.BeginEdit();

        Assert.False(node.IsEditing);
    }

    [Fact]
    public void Typing_then_Enter_commits_the_new_text()
    {
        var tree = new TreeView { LabelEdit = true };
        var node = tree.Nodes.Add("Alpha");
        NodeLabelEditEventArgs? afterArgs = null;
        tree.AfterLabelEdit += (_, e) => afterArgs = e;

        node.BeginEdit();
        TypeChar(tree, 'X');
        PressKey(tree, Keys.Return);

        Assert.Equal("AlphaX", node.Text);
        Assert.False(node.IsEditing);
        Assert.Equal("AlphaX", afterArgs!.Label);
    }

    [Fact]
    public void Escape_cancels_the_edit_and_keeps_the_original_text()
    {
        var tree = new TreeView { LabelEdit = true };
        var node = tree.Nodes.Add("Alpha");

        node.BeginEdit();
        TypeChar(tree, 'X');
        PressKey(tree, Keys.Escape);

        Assert.Equal("Alpha", node.Text);
        Assert.False(node.IsEditing);
    }

    [Fact]
    public void Backspace_removes_the_last_typed_character()
    {
        var tree = new TreeView { LabelEdit = true };
        var node = tree.Nodes.Add("Alpha");

        node.BeginEdit();
        TypeChar(tree, 'X');
        TypeChar(tree, (char)8);
        PressKey(tree, Keys.Return);

        Assert.Equal("Alpha", node.Text);
    }

    private static void TypeChar(TreeView tree, char c)
    {
        var method = typeof(TreeView).GetMethod("OnKeyPress", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        method.Invoke(tree, [new KeyPressEventArgs(c)]);
    }

    private static void PressKey(TreeView tree, Keys key)
    {
        var method = typeof(TreeView).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        method.Invoke(tree, [new System.Windows.Forms.KeyEventArgs(key)]);
    }

    [AvaloniaFact]
    public void Moving_the_focus_away_commits_the_edit()
    {
        var tree = new TreeView { LabelEdit = true };
        var other = new TextBox { Top = 120 };
        var node = tree.Nodes.Add("Alpha");
        var form = ShowWith(tree);
        form.Controls.Add(other);
        tree.Focus();
        node.BeginEdit();
        TypeChar(tree, 'X');

        other.Focus();

        Assert.False(node.IsEditing);
        Assert.Equal("AlphaX", node.Text);

        form.Close();
    }

    [Fact]
    public void Selecting_another_node_commits_the_edit_first()
    {
        var tree = new TreeView { LabelEdit = true };
        var alpha = tree.Nodes.Add("Alpha");
        var beta = tree.Nodes.Add("Beta");
        tree.SelectedNode = alpha;
        alpha.BeginEdit();
        TypeChar(tree, 'X');

        tree.SelectedNode = beta;

        Assert.False(alpha.IsEditing);
        Assert.Null(tree.EditingNode);
        Assert.Equal("AlphaX", alpha.Text);
    }

    [Fact]
    public void Removing_the_edited_node_drops_the_edit_without_an_event()
    {
        var tree = new TreeView { LabelEdit = true };
        var parent = tree.Nodes.Add("Parent");
        var child = parent.Nodes.Add("Child");
        var afterEdits = 0;
        tree.AfterLabelEdit += (_, _) => afterEdits++;
        child.BeginEdit();

        parent.Remove();
        PressKey(tree, Keys.Return);

        Assert.Null(tree.EditingNode);
        Assert.Equal(0, afterEdits);
        Assert.Equal("Child", child.Text);
    }
}
