using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.ToolStrips;

/// <summary>
/// ToolStripControlHost and its wrappers (ToolStripTextBox, ToolStripComboBox,
/// ToolStripProgressBar): the hosted Control's bounds track the item's, it
/// takes real keyboard input once shown, and removing or disposing the item
/// removes the Control from the strip.
/// </summary>
public class HostedItemsTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    [Fact]
    public void A_hosted_control_sits_at_the_items_bounds_and_moves_when_the_strip_lays_out_again()
    {
        var box = new TextBox();
        var host = new ToolStripControlHost(box);
        var strip = new ToolStrip(new ToolStripButton("One"), host) { Font = Font, Size = new Size(300, 25) };

        strip.PerformLayout();

        Assert.Same(box, strip.Controls[0]);
        Assert.Equal(host.Bounds, box.Bounds);
        Assert.True(host.Bounds.X > 0, "The hosted control must come after the first button, not sit at the origin.");

        // Widening the strip does not move a left-aligned item, but a fresh item added before it must
        // push it along, proving the control really follows the item through a second layout pass.
        strip.Items.Insert(0, new ToolStripButton("Zero") { AutoSize = false, Size = new Size(40, 22) });
        strip.PerformLayout();

        Assert.Equal(host.Bounds, box.Bounds);
        Assert.True(box.Bounds.X >= 40, "The hosted control must have moved along with its item.");
    }

    [Fact]
    public void ToolStripTextBox_hosts_a_real_TextBox_sized_from_its_PreferredHeight()
    {
        var item = new ToolStripTextBox { Font = Font };

        Assert.Same(item.Control, item.TextBox);
        Assert.Equal(item.TextBox.PreferredHeight, item.Height);
    }

    [AvaloniaFact]
    public void ToolStripTextBox_autocomplete_reaches_the_hosted_edit_control()
    {
        var item = new ToolStripTextBox
        {
            AutoCompleteMode = AutoCompleteMode.Append,
            AutoCompleteSource = AutoCompleteSource.CustomSource,
        };
        item.AutoCompleteCustomSource.Add("apple");
        var strip = new ToolStrip(item);
        var form = new Form { ClientSize = new Size(300, 60) };
        form.Controls.Add(strip);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        item.Focus();
        form.Host!.KeyTextInput("app");

        Assert.Same(item.AutoCompleteCustomSource, item.TextBox.AutoCompleteCustomSource);
        Assert.Equal("apple", item.Text);
        Assert.Equal(3, item.TextBox.SelectionStart);

        form.Close();
    }

    [AvaloniaFact]
    public void ToolStripComboBox_autocomplete_and_flat_style_reach_the_hosted_combo()
    {
        var item = new ToolStripComboBox
        {
            AutoCompleteMode = AutoCompleteMode.Append,
            AutoCompleteSource = AutoCompleteSource.ListItems,
            FlatStyle = FlatStyle.Flat,
        };
        item.Items.Add("apple");
        var strip = new ToolStrip(item);
        var form = new Form { ClientSize = new Size(300, 60) };
        form.Controls.Add(strip);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        item.Focus();
        form.Host!.KeyTextInput("app");

        Assert.Equal(FlatStyle.Flat, item.ComboBox.FlatStyle);
        Assert.Equal("apple", item.Text);
        Assert.Equal(3, item.ComboBox.SelectionStart);

        form.Close();
    }

    [AvaloniaFact]
    public void Typing_into_a_shown_ToolStripTextBox_changes_its_Text()
    {
        var item = new ToolStripTextBox { Font = Font };
        var strip = new ToolStrip(item) { Font = Font };
        var form = new Form { ClientSize = new Size(300, 60) };
        form.Controls.Add(strip);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        item.Focus();
        form.Host!.KeyTextInput("Hi");

        Assert.Equal("Hi", item.Text);
        Assert.Equal("Hi", item.TextBox.Text);

        form.Close();
    }

    [Fact]
    public void Removing_the_item_removes_the_hosted_control_from_the_strip()
    {
        var box = new TextBox();
        var host = new ToolStripControlHost(box);
        var strip = new ToolStrip(host) { Font = Font };

        Assert.Same(box, strip.Controls[0]);

        strip.Items.Remove(host);

        Assert.Empty(strip.Controls);
        Assert.Null(box.Parent);
    }

    [Fact]
    public void Disposing_the_item_disposes_the_hosted_control()
    {
        var box = new TextBox();
        var host = new ToolStripControlHost(box);
        var strip = new ToolStrip(host) { Font = Font };

        host.Dispose();

        Assert.True(box.IsDisposed);
        Assert.Empty(strip.Controls);
    }

    [Fact]
    public void Hiding_the_item_hides_the_hosted_control_and_showing_it_again_restores_it()
    {
        var box = new TextBox();
        var host = new ToolStripControlHost(box);
        var strip = new ToolStrip(host) { Font = Font };

        host.Available = false;

        Assert.False(box.Visible);

        host.Available = true;

        Assert.True(box.Visible);
    }

    [Fact]
    public void Disabling_the_item_disables_the_hosted_control()
    {
        var box = new TextBox();
        var host = new ToolStripControlHost(box);
        var strip = new ToolStrip(host) { Font = Font };

        host.Enabled = false;

        Assert.False(box.Enabled);
        Assert.False(host.Enabled);
    }

    [Fact]
    public void Text_Font_BackColor_and_ForeColor_pass_through_to_the_hosted_control()
    {
        var box = new TextBox();
        var host = new ToolStripControlHost(box);
        var bold = new Font(Font, FontStyle.Bold);

        host.Text = "hello";
        host.Font = bold;
        host.BackColor = Color.Red;
        host.ForeColor = Color.Blue;

        Assert.Equal("hello", box.Text);
        Assert.Equal(bold, box.Font);
        Assert.Equal(Color.Red, box.BackColor);
        Assert.Equal(Color.Blue, box.ForeColor);
    }

    [AvaloniaFact]
    public void A_navigator_commits_a_typed_position_when_Enter_is_pressed()
    {
        var people = new List<string> { "a", "b", "c", "d", "e" };
        var source = new BindingSource { DataSource = people };
        var navigator = new BindingNavigator(source);
        var form = new Form { ClientSize = new Size(300, 40) };
        form.Controls.Add(navigator);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        var positionBox = (ToolStripTextBox)navigator.PositionItem!;
        positionBox.Focus();
        positionBox.Clear();

        form.Host!.KeyTextInput("4");
        Assert.Equal(0, source.Position);

        form.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal(3, source.Position);

        form.Close();
    }

    [AvaloniaFact]
    public void A_ToolStripComboBox_has_its_own_default_width_not_the_ComboBoxs()
    {
        var item = new ToolStripComboBox();

        Assert.Equal(100, item.Width);
        Assert.Equal(100, item.ComboBox.Width);
    }

    [AvaloniaFact]
    public void Moving_a_hosted_item_to_another_strip_moves_its_control()
    {
        var first = new ToolStrip();
        var second = new ToolStrip();
        var item = new ToolStripTextBox();
        first.Items.Add(item);
        Assert.Same(first, item.TextBox.Parent);

        second.Items.Add(item);

        Assert.Same(second, item.TextBox.Parent);
        Assert.DoesNotContain(item.TextBox, first.Controls.Cast<Control>());
    }

    [AvaloniaFact]
    public void An_items_mnemonic_clicks_it_only_when_the_character_matches()
    {
        var item = new MnemonicProbe("&Save");
        var clicks = 0;
        item.Click += (_, _) => clicks++;

        Assert.False(item.Process('x'));
        Assert.True(item.Process('s'));
        Assert.Equal(1, clicks);
    }

    private sealed class MnemonicProbe(string text) : ToolStripButton(text)
    {
        public bool Process(char character) => ProcessMnemonic(character);
    }

    [AvaloniaFact]
    public void Text_that_is_not_a_number_puts_the_navigators_current_position_back()
    {
        var source = new BindingSource { DataSource = new List<string> { "a", "b", "c" } };
        source.Position = 1;
        var navigator = new BindingNavigator(source);
        var other = new Button();
        var form = new Form { ClientSize = new Size(300, 60) };
        form.Controls.Add(navigator);
        form.Controls.Add(other);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        var box = (ToolStripTextBox)navigator.PositionItem!;
        box.Focus();

        box.Text = "abc";
        other.Focus();

        Assert.Equal(1, source.Position);
        Assert.Equal("2", box.Text);
        form.Close();
    }
}
