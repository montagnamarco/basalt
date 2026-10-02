using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>DomainUpDown: Items, SelectedIndex/SelectedItem, Sorted, Wrap, the spin buttons and typing a match.</summary>
public class DomainUpDownTests
{
    private static Form Show(params Control[] controls)
    {
        var form = new Form { ClientSize = new Size(300, 200) };
        form.Controls.AddRange(controls);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    private static void Type(Form form, string text)
    {
        foreach (var character in text) form.Host!.KeyTextInput(character.ToString());
    }

    private static void ClickEdit(Form form, DomainUpDown domainUpDown)
    {
        var x = domainUpDown.Left + 10;
        var y = domainUpDown.Top + domainUpDown.Height / 2;
        form.Host!.MouseDown(new Avalonia.Point(x, y), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(x, y), MouseButton.Left);
    }

    private static void ClickSpinButton(Form form, DomainUpDown domainUpDown, bool up)
    {
        var x = domainUpDown.Left + domainUpDown.Width - 8;
        var y = domainUpDown.Top + (up ? domainUpDown.Height / 4 : domainUpDown.Height * 3 / 4);
        form.Host!.MouseDown(new Avalonia.Point(x, y), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(x, y), MouseButton.Left);
    }

    // --- Items: a plain ArrayList that keeps SelectedIndex valid as it changes ---

    [Fact]
    public void Items_add_insert_and_remove_work_like_an_ArrayList()
    {
        var domainUpDown = new DomainUpDown();

        domainUpDown.Items.Add("one");
        domainUpDown.Items.Add("two");
        domainUpDown.Items.Insert(1, "middle");

        Assert.Equal(3, domainUpDown.Items.Count);
        Assert.Equal("middle", domainUpDown.Items[1]);

        domainUpDown.Items.Remove("middle");
        Assert.Equal(2, domainUpDown.Items.Count);
    }

    [Fact]
    public void Removing_the_selected_item_clamps_SelectedIndex_into_range()
    {
        var domainUpDown = new DomainUpDown();
        domainUpDown.Items.Add("one");
        domainUpDown.Items.Add("two");
        domainUpDown.SelectedIndex = 1;

        domainUpDown.Items.RemoveAt(1);

        Assert.Equal(0, domainUpDown.SelectedIndex);
    }

    [Fact]
    public void Edits_before_the_selected_item_keep_the_same_item_and_text_selected()
    {
        var domainUpDown = new DomainUpDown();
        domainUpDown.Items.AddRange(new[] { "a", "b", "c" });
        domainUpDown.SelectedIndex = 1;

        domainUpDown.Items.RemoveAt(0);
        Assert.Equal("b", domainUpDown.SelectedItem);
        Assert.Equal("b", domainUpDown.Text);

        domainUpDown.Items.Insert(0, "z");
        Assert.Equal("b", domainUpDown.SelectedItem);
        Assert.Equal(1, domainUpDown.SelectedIndex);
    }

    [Fact]
    public void Removing_the_only_item_clears_the_text_and_reports_the_change()
    {
        var domainUpDown = new DomainUpDown();
        domainUpDown.Items.Add("only");
        domainUpDown.SelectedIndex = 0;
        var changes = 0;
        domainUpDown.SelectedItemChanged += (_, _) => changes++;

        domainUpDown.Items.RemoveAt(0);

        Assert.Equal(-1, domainUpDown.SelectedIndex);
        Assert.Equal(string.Empty, domainUpDown.Text);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Replacing_the_selected_item_shows_its_new_text()
    {
        var domainUpDown = new DomainUpDown();
        domainUpDown.Items.AddRange(new[] { "a", "b" });
        domainUpDown.SelectedIndex = 1;

        domainUpDown.Items[1] = "bee";

        Assert.Equal("bee", domainUpDown.Text);
    }

    // --- SelectedIndex/SelectedItem: the two sides of the same selection ---

    [Fact]
    public void Setting_SelectedIndex_updates_SelectedItem_and_the_displayed_text()
    {
        var domainUpDown = new DomainUpDown();
        domainUpDown.Items.Add("Monday");
        domainUpDown.Items.Add("Tuesday");

        domainUpDown.SelectedIndex = 1;

        Assert.Equal("Tuesday", domainUpDown.SelectedItem);
        Assert.Equal("Tuesday", domainUpDown.Text);
    }

    [Fact]
    public void Setting_SelectedItem_updates_SelectedIndex()
    {
        var domainUpDown = new DomainUpDown();
        domainUpDown.Items.Add("Monday");
        domainUpDown.Items.Add("Tuesday");

        domainUpDown.SelectedItem = "Monday";

        Assert.Equal(0, domainUpDown.SelectedIndex);
    }

    [Fact]
    public void An_out_of_range_SelectedIndex_throws()
    {
        var domainUpDown = new DomainUpDown();
        domainUpDown.Items.Add("one");

        Assert.Throws<ArgumentOutOfRangeException>(() => domainUpDown.SelectedIndex = 5);
        Assert.Throws<ArgumentOutOfRangeException>(() => domainUpDown.SelectedIndex = -2);
    }

    [Fact]
    public void Selecting_the_same_index_again_does_not_raise_SelectedItemChanged()
    {
        var domainUpDown = new DomainUpDown();
        domainUpDown.Items.Add("one");
        domainUpDown.SelectedIndex = 0;
        var raised = 0;
        domainUpDown.SelectedItemChanged += (_, _) => raised++;

        domainUpDown.SelectedIndex = 0;

        Assert.Equal(0, raised);
    }

    // --- UpButton/DownButton: direct calls and the spin buttons, with and without Wrap ---

    [Fact]
    public void UpButton_and_DownButton_move_one_item_at_a_time()
    {
        var domainUpDown = new DomainUpDown();
        domainUpDown.Items.Add("a");
        domainUpDown.Items.Add("b");
        domainUpDown.Items.Add("c");

        domainUpDown.UpButton();
        Assert.Equal("a", domainUpDown.SelectedItem);

        domainUpDown.UpButton();
        Assert.Equal("b", domainUpDown.SelectedItem);

        domainUpDown.DownButton();
        Assert.Equal("a", domainUpDown.SelectedItem);
    }

    [Fact]
    public void Without_Wrap_UpButton_and_DownButton_stop_at_the_ends()
    {
        var domainUpDown = new DomainUpDown { Wrap = false };
        domainUpDown.Items.Add("a");
        domainUpDown.Items.Add("b");
        domainUpDown.SelectedIndex = 1;

        domainUpDown.UpButton();
        Assert.Equal("b", domainUpDown.SelectedItem); // Already last: stays.

        domainUpDown.DownButton();
        domainUpDown.DownButton();
        Assert.Equal("a", domainUpDown.SelectedItem); // Stops at first instead of going negative.
    }

    [Fact]
    public void With_Wrap_UpButton_cycles_past_the_last_item_back_to_the_first()
    {
        var domainUpDown = new DomainUpDown { Wrap = true };
        domainUpDown.Items.Add("a");
        domainUpDown.Items.Add("b");
        domainUpDown.SelectedIndex = 1;

        domainUpDown.UpButton();

        Assert.Equal("a", domainUpDown.SelectedItem);
    }

    [AvaloniaFact]
    public void Clicking_the_up_button_selects_the_next_item()
    {
        var domainUpDown = new DomainUpDown { Bounds = new Rectangle(10, 10, 100, 24) };
        domainUpDown.Items.Add("red");
        domainUpDown.Items.Add("green");
        var form = Show(domainUpDown);

        ClickSpinButton(form, domainUpDown, up: true);

        Assert.Equal("red", domainUpDown.SelectedItem);

        form.Close();
    }

    // --- Sorted: items are kept alphabetical, and the selection follows its item through a sort ---

    [Fact]
    public void Sorted_items_are_added_in_alphabetical_order()
    {
        var domainUpDown = new DomainUpDown { Sorted = true };

        domainUpDown.Items.Add("banana");
        domainUpDown.Items.Add("apple");
        domainUpDown.Items.Add("cherry");

        Assert.Equal(["apple", "banana", "cherry"], domainUpDown.Items.ToArray());
    }

    [Fact]
    public void Turning_Sorted_on_resorts_the_existing_items_and_keeps_the_selection()
    {
        var domainUpDown = new DomainUpDown();
        domainUpDown.Items.Add("banana");
        domainUpDown.Items.Add("apple");
        domainUpDown.SelectedItem = "apple";

        domainUpDown.Sorted = true;

        Assert.Equal(["apple", "banana"], domainUpDown.Items.ToArray());
        Assert.Equal("apple", domainUpDown.SelectedItem);
    }

    // --- Typing: a keystroke-by-keystroke match against the items' text, as the user types ---

    [AvaloniaFact]
    public void Typing_an_items_exact_text_selects_it_without_pressing_Enter()
    {
        var domainUpDown = new DomainUpDown { Bounds = new Rectangle(10, 10, 150, 24) };
        domainUpDown.Items.Add("Monday");
        domainUpDown.Items.Add("Tuesday");
        var form = Show(domainUpDown);
        ClickEdit(form, domainUpDown);

        Type(form, "Tuesday");

        Assert.Equal(1, domainUpDown.SelectedIndex);

        form.Close();
    }

    [AvaloniaFact]
    public void Typing_text_matching_no_item_leaves_the_selection_alone()
    {
        var domainUpDown = new DomainUpDown { Bounds = new Rectangle(10, 10, 150, 24) };
        domainUpDown.Items.Add("Monday");
        domainUpDown.SelectedIndex = 0;
        var form = Show(domainUpDown);
        ClickEdit(form, domainUpDown);
        domainUpDown.Select(0, domainUpDown.Text.Length);

        Type(form, "zzz");

        Assert.Equal(0, domainUpDown.SelectedIndex);
        Assert.Equal("zzz", domainUpDown.Text);

        form.Close();
    }

    /// <summary>Reintroducing the bug ("new" instead of "override" on Value) would make this null: a screen reader reads AccessibleObject.Value through the base type, which a "new" member hides from.</summary>
    [Fact]
    public void DomainItemAccessibleObject_Value_reads_the_items_text_through_the_base_type()
    {
        var parent = new DomainUpDown.DomainUpDownAccessibleObject(new DomainUpDown());
        AccessibleObject accessible = new DomainUpDown.DomainItemAccessibleObject("Monday", parent);

        Assert.Equal("Monday", accessible.Value);
    }
}
