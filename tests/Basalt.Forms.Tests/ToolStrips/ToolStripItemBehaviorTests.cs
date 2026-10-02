using System.Drawing;
using System.Windows.Forms;
using System.Windows.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DataObject = System.Windows.Forms.DataObject;
using DragDropEffects = System.Windows.Forms.DragDropEffects;

namespace Basalt.Forms.Tests.ToolStrips;

/// <summary>
/// The newer parts of ToolStripItem's own contract: Command binding, the
/// ambient RightToLeft it mirrors from its owner, ImageTransparentColor
/// actually recolouring the bitmap, drag and drop delegated to the owning
/// strip's own Control.DoDragDrop loop, and the accessible object it builds.
/// </summary>
public class ToolStripItemBehaviorTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private sealed class TestCommand : ICommand
    {
        private EventHandler? _canExecuteChanged;

        public bool CanExecuteValue { get; set; } = true;
        public int ExecuteCount { get; private set; }
        public object? LastParameter { get; private set; }

        /// <summary>How many handlers are currently subscribed - what a disposed item leaving its handler behind would leak.</summary>
        public int SubscriberCount { get; private set; }

        public event EventHandler? CanExecuteChanged
        {
            add
            {
                _canExecuteChanged += value;
                SubscriberCount++;
            }
            remove
            {
                _canExecuteChanged -= value;
                SubscriberCount--;
            }
        }

        public bool CanExecute(object? parameter) => CanExecuteValue;

        public void Execute(object? parameter)
        {
            ExecuteCount++;
            LastParameter = parameter;
        }

        public void RaiseCanExecuteChanged() => _canExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    // --- Command -------------------------------------------------------------

    [Fact]
    public void Clicking_an_item_executes_its_bound_command_with_CommandParameter()
    {
        var command = new TestCommand();
        var item = new ToolStripButton { Command = command, CommandParameter = "payload" };

        item.PerformClick();

        Assert.Equal(1, command.ExecuteCount);
        Assert.Equal("payload", command.LastParameter);
    }

    [Fact]
    public void A_command_that_cannot_execute_disables_the_item_so_a_click_never_reaches_it()
    {
        var command = new TestCommand { CanExecuteValue = false };
        var item = new ToolStripButton { Command = command };

        Assert.False(item.Enabled);

        item.PerformClick();

        Assert.Equal(0, command.ExecuteCount);
    }

    [Fact]
    public void Raising_CanExecuteChanged_updates_Enabled_without_any_click()
    {
        var command = new TestCommand();
        var item = new ToolStripButton { Command = command };
        Assert.True(item.Enabled);

        command.CanExecuteValue = false;
        command.RaiseCanExecuteChanged();

        Assert.False(item.Enabled);
    }

    [Fact]
    public void Disposing_an_item_detaches_it_from_its_commands_CanExecuteChanged()
    {
        var command = new TestCommand();
        var item = new ToolStripButton { Command = command };
        Assert.Equal(1, command.SubscriberCount);

        item.Dispose();

        Assert.Equal(0, command.SubscriberCount);
    }

    // --- RightToLeft -----------------------------------------------------------

    [Fact]
    public void An_items_RightToLeft_follows_its_owning_strip_until_set_explicitly()
    {
        var strip = new ToolStrip { RightToLeft = RightToLeft.Yes };
        var item = new ToolStripButton();
        strip.Items.Add(item);

        Assert.Equal(RightToLeft.Yes, item.RightToLeft);

        item.RightToLeft = RightToLeft.No;
        strip.RightToLeft = RightToLeft.Yes;

        Assert.Equal(RightToLeft.No, item.RightToLeft);
    }

    [Fact]
    public void Changing_the_strips_RightToLeft_raises_RightToLeftChanged_only_on_items_that_still_inherit_it()
    {
        var strip = new ToolStrip();
        var inheriting = new ToolStripButton();
        var explicitItem = new ToolStripButton { RightToLeft = RightToLeft.No };
        strip.Items.Add(inheriting);
        strip.Items.Add(explicitItem);

        var inheritingRaised = 0;
        var explicitRaised = 0;
        inheriting.RightToLeftChanged += (_, _) => inheritingRaised++;
        explicitItem.RightToLeftChanged += (_, _) => explicitRaised++;

        strip.RightToLeft = RightToLeft.Yes;

        Assert.Equal(1, inheritingRaised);
        Assert.Equal(0, explicitRaised);
    }

    // --- ImageTransparentColor ---------------------------------------------

    [Fact]
    public void Setting_ImageTransparentColor_makes_the_matching_pixels_of_the_bitmap_actually_transparent()
    {
        var bitmap = new Bitmap(4, 4);
        using (var g = Graphics.FromImage(bitmap)) g.Clear(Color.Red);
        var item = new ToolStripButton { Image = bitmap };

        Assert.Equal(255, bitmap.GetPixel(0, 0).A);

        item.ImageTransparentColor = Color.Red;

        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
    }

    // --- Drag and drop -------------------------------------------------------

    [AvaloniaFact]
    public void An_items_DoDragDrop_runs_through_its_owning_strips_own_drag_loop()
    {
        var strip = new ToolStrip { Font = Font, Size = new Size(200, 25) };
        var item = new ToolStripButton("One");
        strip.Items.Add(item);
        var form = new Form { ClientSize = new Size(300, 100) };
        form.Controls.Add(strip);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        var feedbackRaisedOnTheStrip = false;
        strip.GiveFeedback += (_, _) => feedbackRaisedOnTheStrip = true;

        // No button held: Control.DoDragDrop's loop sees MouseButtons.None on its very first pass and
        // drops immediately, but it still asks for feedback once - proving the item really borrowed the
        // strip's own loop rather than running (or skipping) one of its own.
        var effect = item.DoDragDrop(new DataObject("payload"), DragDropEffects.Move);

        Assert.True(feedbackRaisedOnTheStrip);
        Assert.Equal(DragDropEffects.None, effect);

        form.Close();
    }

    [Fact]
    public void An_item_on_no_strip_cannot_start_a_drag()
    {
        var item = new ToolStripButton("Orphan");

        var effect = item.DoDragDrop(new DataObject("payload"), DragDropEffects.Move);

        Assert.Equal(DragDropEffects.None, effect);
    }

    // --- Accessibility -------------------------------------------------------

    [Fact]
    public void An_items_default_accessible_role_is_PushButton()
    {
        var item = new ToolStripButton();

        Assert.Equal(AccessibleRole.PushButton, item.AccessibilityObject.Role);
    }

    [Fact]
    public void A_checked_buttons_accessible_object_reports_the_Checked_state()
    {
        var unchecked_ = new ToolStripButton();
        var checked_ = new ToolStripButton { Checked = true };

        Assert.False(unchecked_.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
        Assert.True(checked_.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
    }

    [Fact]
    public void DoDefaultAction_on_the_accessible_object_clicks_the_item()
    {
        var item = new ToolStripButton();
        var clicked = false;
        item.Click += (_, _) => clicked = true;

        item.AccessibilityObject.DoDefaultAction();

        Assert.True(clicked);
    }

    [Fact]
    public void A_button_shows_its_text_as_tool_tip_until_given_one_and_only_while_AutoToolTip_is_on()
    {
        var button = new ToolStripButton("&Save && close");
        var label = new ToolStripLabel("Status");

        Assert.True(button.AutoToolTip);
        Assert.Equal("Save & close", button.ToolTipText);
        Assert.False(label.AutoToolTip);
        Assert.Null(label.ToolTipText);

        button.ToolTipText = "Writes the file";
        Assert.Equal("Writes the file", button.ToolTipText);

        button.ToolTipText = null;
        button.AutoToolTip = false;
        Assert.Null(button.ToolTipText);
    }
}
