using System.Windows.Forms;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Accessibility;

public class FormsAutomationPeerTests
{
    [AvaloniaFact]
    public void Text_and_checkbox_providers_change_controls_and_protect_readonly_or_password_values()
    {
        using var form = new Form();
        var text = new TextBox { Name = "editable", Text = "before" };
        var password = new TextBox { Name = "password", PasswordChar = '*', Text = "secret" };
        var checkBox = new CheckBox { Name = "check", ThreeState = true };
        form.Controls.Add(text);
        form.Controls.Add(password);
        form.Controls.Add(checkBox);
        form.Show();
        try
        {
            var children = ControlAutomationPeer.CreatePeerForElement(form.Canvas!)!.GetChildren();
            var value = children.Single(peer => peer.GetAutomationId() == "editable").GetProvider<IValueProvider>()!;
            Assert.Equal("before", value.Value);
            value.SetValue("after");
            Assert.Equal("after", text.Text);
            text.ReadOnly = true;
            Assert.True(value.IsReadOnly);
            Assert.Throws<InvalidOperationException>(() => value.SetValue("forbidden"));
            Assert.Null(children.Single(peer => peer.GetAutomationId() == "password").GetProvider<IValueProvider>());
            var toggle = children.Single(peer => peer.GetAutomationId() == "check").GetProvider<IToggleProvider>()!;
            Assert.Equal(ToggleState.Off, toggle.ToggleState);
            toggle.Toggle();
            Assert.True(checkBox.Checked);
            Assert.Equal(ToggleState.On, toggle.ToggleState);
            checkBox.CheckState = CheckState.Indeterminate;
            Assert.Equal(ToggleState.Indeterminate, toggle.ToggleState);
            checkBox.Enabled = false;
            Assert.Throws<InvalidOperationException>(() => toggle.Toggle());
        }
        finally { form.Close(); }
    }

    [AvaloniaFact]
    public void Canvas_exposes_named_children_and_button_action()
    {
        using var form = new Form();
        var button = new Button { Text = "&Open", Name = "openButton" };
        var label = new Label { Text = "Status" };
        form.Controls.Add(button);
        form.Controls.Add(label);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        var canvas = form.Canvas!;
        var peer = ControlAutomationPeer.CreatePeerForElement(canvas)!;
        var children = peer.GetChildren();
        Assert.Equal(2, children.Count);
        var buttonPeer = children.Single(child => child.GetAutomationId() == "openButton");
        Assert.Equal("Open", buttonPeer.GetName());
        Assert.Equal(AutomationControlType.Button, buttonPeer.GetAutomationControlType());
        Assert.Same(peer, buttonPeer.GetParent());
        var clicked = false;
        button.Click += (_, _) => clicked = true;
        buttonPeer.GetProvider<IInvokeProvider>()!.Invoke();
        Assert.True(clicked);
        Assert.Null(children.Single(child => child.GetClassName() == "Label").GetProvider<IInvokeProvider>());
    }

    [AvaloniaFact]
    public void Changing_children_invalidates_the_exposed_tree()
    {
        using var form = new Form();
        form.Show();
        var peer = ControlAutomationPeer.CreatePeerForElement(form.Canvas!)!;
        Assert.Empty(peer.GetChildren());
        var button = new Button { Text = "Save" };
        form.Controls.Add(button);
        var first = Assert.Single(peer.GetChildren());
        Assert.Same(first, Assert.Single(peer.GetChildren()));
        form.Controls.Remove(button);
        Assert.Empty(peer.GetChildren());
        button.Dispose();
    }

    [AvaloniaFact]
    public void Child_peer_maps_real_host_coordinates_and_tracks_moves()
    {
        using var form = new Form();
        var button = new Button { Bounds = new System.Drawing.Rectangle(20, 30, 80, 25) };
        form.Controls.Add(button);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        var peer = ControlAutomationPeer.CreatePeerForElement(form.Canvas!)!;
        var child = Assert.Single(peer.GetChildren());
        var before = child.GetBoundingRectangle();
        var screen = child.ToScreen(before);
        Assert.NotNull(screen);
        var expected = button.PointToScreen(System.Drawing.Point.Empty);
        var scaling = Avalonia.Controls.TopLevel.GetTopLevel(form.Canvas!)!.RenderScaling;
        Assert.Equal(expected.X * scaling, screen.Value.X, 0);
        Assert.Equal(expected.Y * scaling, screen.Value.Y, 0);
        button.Left += 15;
        button.Width += 10;
        var after = child.GetBoundingRectangle();
        Assert.Equal(before.X + 15, after.X);
        Assert.Equal(before.Width + 10, after.Width);
        Assert.NotNull(child.ToScreen(after));
    }

    [AvaloniaFact]
    public void Hidden_child_reappears_and_notifies_the_accessibility_client()
    {
        using var form = new Form();
        var button = new Button();
        form.Controls.Add(button);
        form.Show();
        var peer = ControlAutomationPeer.CreatePeerForElement(form.Canvas!)!;
        Assert.Single(peer.GetChildren());
        var changes = 0;
        peer.ChildrenChanged += (_, _) => changes++;
        button.Visible = false;
        Assert.Empty(peer.GetChildren());
        var beforeShow = changes;
        button.Visible = true;
        Assert.True(changes > beforeShow);
        Assert.Single(peer.GetChildren());
    }

    [AvaloniaFact]
    public void Removed_child_peer_is_collectible_while_canvas_and_control_remain_alive()
    {
        using var form = new Form();
        using var button = new Button();
        form.Show();
        var peer = ControlAutomationPeer.CreatePeerForElement(form.Canvas!)!;
        var removed = AddAndRemove(form, button, peer);
        // Avalonia posts visual-tree detach work to the UI dispatcher. The
        // removed owner remains rooted until that queued work has run.
        Dispatcher.UIThread.RunJobs();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(removed.IsAlive);
        GC.KeepAlive(peer);
        GC.KeepAlive(button);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference AddAndRemove(Form form, Button button, AutomationPeer peer)
    {
        form.Controls.Add(button);
        var child = Assert.Single(peer.GetChildren());
        var reference = new WeakReference(child);
        form.Controls.Remove(button);
        Assert.Empty(peer.GetChildren());
        return reference;
    }
}
