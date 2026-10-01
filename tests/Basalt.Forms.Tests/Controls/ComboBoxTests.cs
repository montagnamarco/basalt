using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>Items, selection, text, painting, the drop-down popup and the keyboard and mouse of ComboBox.</summary>
public class ComboBoxTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static (Form Form, ComboBox Box) Show(ComboBox box)
    {
        box.Font = Font;
        box.Bounds = new Rectangle(10, 10, 150, box.PreferredHeight);
        var form = new Form { ClientSize = new Size(300, 300) };
        form.Controls.Add(box);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        box.Focus();

        return (form, box);
    }

    private static void Press(Form form, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        form.Host!.KeyPressQwerty(key, modifiers);
        form.Host!.KeyReleaseQwerty(key, modifiers);
    }

    /// <summary>The borderless popup window currently open, as a plain Control (its real type is private to ComboBox).</summary>
    private static Control OpenPopup() => Popups.All.First(popup => popup is not Form);

    private static void ClickButton(Form form, ComboBox box)
    {
        var point = new Avalonia.Point(box.Left + box.Width - 4, box.Top + box.Height / 2);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
    }

    /// <summary>Clicks inside the open drop-down, at the row for the given item index.</summary>
    private static void ClickDropDownItem(ComboBox box, int index)
    {
        // Captured once: the click itself commits and closes the popup, which would
        // otherwise leave nothing for a second lookup to find before the MouseUp.
        var popup = OpenPopup().PopupWindow!;
        var point = new Avalonia.Point(5, index * box.ItemHeight + box.ItemHeight / 2);
        popup.MouseDown(point, MouseButton.Left);
        popup.MouseUp(point, MouseButton.Left);
    }

    // --- Items and Sorted ----------------------------------------------------

    [Fact]
    public void Items_add_insert_remove_and_clear()
    {
        var box = new ComboBox();

        box.Items.Add("one");
        box.Items.AddRange("two", "three");
        box.Items.Insert(1, "inserted");

        Assert.Equal(["one", "inserted", "two", "three"], box.Items.Cast<object>());
        Assert.True(box.Items.Contains("inserted"));
        Assert.Equal(2, box.Items.IndexOf("two"));

        box.Items.Remove("inserted");
        Assert.DoesNotContain("inserted", box.Items.Cast<object>());

        box.Items.RemoveAt(0);
        Assert.Equal(["two", "three"], box.Items.Cast<object>());

        box.Items.Clear();
        Assert.Empty(box.Items);
    }

    [Fact]
    public void Sorted_keeps_items_in_text_order_as_they_are_added()
    {
        var box = new ComboBox { Sorted = true };

        box.Items.Add("banana");
        box.Items.Add("apple");
        box.Items.Add("cherry");

        Assert.Equal(["apple", "banana", "cherry"], box.Items.Cast<object>());
    }

    [Fact]
    public void Items_cannot_be_edited_directly_while_a_DataSource_drives_them()
    {
        var box = new ComboBox { DataSource = new[] { "a", "b" } };

        Assert.Throws<ArgumentException>(() => box.Items.Add("c"));
        Assert.Throws<ArgumentException>(() => box.Items.Clear());
    }

    [Fact]
    public void BeginUpdate_and_EndUpdate_do_not_lose_or_duplicate_items()
    {
        var box = new ComboBox();

        box.BeginUpdate();
        box.Items.Add("a");
        box.Items.Add("b");
        box.EndUpdate();

        Assert.Equal(2, box.Items.Count);
    }

    // --- Selection and Text ---------------------------------------------------

    [Fact]
    public void SelectedIndex_updates_SelectedItem_and_Text_together()
    {
        var box = new ComboBox();
        box.Items.AddRange("Apple", "Banana", "Cherry");

        box.SelectedIndex = 1;

        Assert.Equal("Banana", box.SelectedItem);
        Assert.Equal("Banana", box.Text);
    }

    [Fact]
    public void SelectedIndexChanged_is_raised_exactly_once_per_change()
    {
        var box = new ComboBox();
        box.Items.AddRange("a", "b", "c");
        var raises = 0;
        box.SelectedIndexChanged += (_, _) => raises++;

        box.SelectedIndex = 0;
        Assert.Equal(1, raises);

        // Setting the same index again changes nothing: no second event.
        box.SelectedIndex = 0;
        Assert.Equal(1, raises);

        box.SelectedIndex = 1;
        Assert.Equal(2, raises);
    }

    [Fact]
    public void A_DropDownList_combo_only_accepts_text_that_matches_an_item_exactly()
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        box.Items.AddRange("Apple", "Banana");

        box.Text = "Banana";
        Assert.Equal(1, box.SelectedIndex);

        box.Text = "Does not exist";
        // Ignored, exactly as WinForms does: the previous selection stands.
        Assert.Equal(1, box.SelectedIndex);
        Assert.Equal("Banana", box.Text);
    }

    [Fact]
    public void An_editable_combo_accepts_free_text_and_clears_the_selection_when_it_does_not_match()
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        box.Items.AddRange("Apple", "Banana");

        box.Text = "Something else";

        Assert.Equal("Something else", box.Text);
        Assert.Equal(-1, box.SelectedIndex);

        box.Text = "Apple";
        Assert.Equal(0, box.SelectedIndex);
    }

    [Fact]
    public void SelectionStart_Length_and_SelectedText_read_and_write_the_edit()
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Text = "Hello world" };

        box.Select(0, 5);

        Assert.Equal(0, box.SelectionStart);
        Assert.Equal(5, box.SelectionLength);
        Assert.Equal("Hello", box.SelectedText);

        box.SelectedText = "Goodbye";
        Assert.Equal("Goodbye world", box.Text);
    }

    [Fact]
    public void FindString_wraps_and_FindStringExact_requires_the_whole_text()
    {
        var box = new ComboBox();
        box.Items.AddRange("Apple", "Banana", "Apricot");

        Assert.Equal(0, box.FindString("Ap"));
        Assert.Equal(2, box.FindString("Ap", 0));

        Assert.Equal(-1, box.FindStringExact("Ap"));
        Assert.Equal(0, box.FindStringExact("apple"));
    }

    // --- Painting --------------------------------------------------------------

    [AvaloniaFact]
    public void A_DropDownList_combo_highlights_its_text_when_focused()
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.White };
        box.Items.AddRange("Apple", "Banana");
        var (form, combo) = Show(box);
        combo.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        using var bitmap = new Bitmap(combo.Width, combo.Height);
        combo.DrawToBitmap(bitmap, new Rectangle(0, 0, combo.Width, combo.Height));

        var pixel = bitmap.GetPixel(4, combo.Height / 2);
        Assert.Equal(SystemColors.Highlight.ToArgb(), pixel.ToArgb());

        form.Close();
    }

    [AvaloniaFact]
    public void The_drop_down_button_is_drawn_at_the_right_edge()
    {
        var box = new ComboBox { BackColor = Color.White, ForeColor = Color.White };
        var (form, combo) = Show(box);
        Dispatcher.UIThread.RunJobs();

        using var bitmap = new Bitmap(combo.Width, combo.Height);
        combo.DrawToBitmap(bitmap, new Rectangle(0, 0, combo.Width, combo.Height));

        // The classic combo button has a dark 3D edge; a plain white control would not.
        var buttonPixel = bitmap.GetPixel(combo.Width - 2, 1);
        Assert.NotEqual(Color.White.ToArgb(), buttonPixel.ToArgb());

        form.Close();
    }

    // --- Drop-down: opening and closing ---------------------------------------

    [AvaloniaFact]
    public void Clicking_the_button_opens_the_drop_down_and_raises_DropDown()
    {
        var (form, box) = Show(new ComboBox());
        box.Items.AddRange("a", "b", "c");
        var opened = 0;
        box.DropDown += (_, _) => opened++;

        ClickButton(form, box);

        Assert.True(box.DroppedDown);
        Assert.Equal(1, opened);
        Assert.Contains(Popups.All, popup => popup is not Form);

        form.Close();
    }

    [AvaloniaFact]
    public void Clicking_an_item_in_the_drop_down_commits_it_and_closes()
    {
        var (form, box) = Show(new ComboBox());
        box.Items.AddRange("a", "b", "c");
        var committed = 0;
        box.SelectionChangeCommitted += (_, _) => committed++;
        box.DroppedDown = true;
        Dispatcher.UIThread.RunJobs();

        ClickDropDownItem(box, 2);

        Assert.Equal(2, box.SelectedIndex);
        Assert.Equal(1, committed);
        Assert.False(box.DroppedDown);
        Assert.DoesNotContain(Popups.All, popup => popup is not Form);

        form.Close();
    }

    [AvaloniaFact]
    public void A_click_outside_the_drop_down_closes_it_without_changing_the_selection()
    {
        var (form, box) = Show(new ComboBox());
        box.Items.AddRange("a", "b", "c");
        box.SelectedIndex = 0;
        box.DroppedDown = true;
        Dispatcher.UIThread.RunJobs();

        form.Host!.MouseDown(new Avalonia.Point(250, 250), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(250, 250), MouseButton.Left);

        Assert.False(box.DroppedDown);
        Assert.Equal(0, box.SelectedIndex);

        form.Close();
    }

    [AvaloniaFact]
    public void A_Simple_combo_never_opens_a_popup()
    {
        var (form, box) = Show(new ComboBox { DropDownStyle = ComboBoxStyle.Simple });
        box.Items.AddRange("a", "b");

        box.DroppedDown = true;

        Assert.False(box.DroppedDown);
        Assert.DoesNotContain(Popups.All, popup => popup is not Form);

        form.Close();
    }

    // --- Keyboard --------------------------------------------------------------

    [AvaloniaFact]
    public void F4_opens_and_closes_the_drop_down()
    {
        var (form, box) = Show(new ComboBox());
        box.Items.AddRange("a", "b");

        Press(form, PhysicalKey.F4);
        Assert.True(box.DroppedDown);

        Press(form, PhysicalKey.F4);
        Assert.False(box.DroppedDown);

        form.Close();
    }

    [AvaloniaFact]
    public void Alt_Down_opens_the_drop_down()
    {
        var (form, box) = Show(new ComboBox());
        box.Items.AddRange("a", "b");

        Press(form, PhysicalKey.ArrowDown, RawInputModifiers.Alt);

        Assert.True(box.DroppedDown);

        form.Close();
    }

    [AvaloniaFact]
    public void Arrow_keys_change_and_commit_the_selection_when_closed()
    {
        var (form, box) = Show(new ComboBox());
        box.Items.AddRange("a", "b", "c");
        box.SelectedIndex = 0;
        var committed = 0;
        box.SelectionChangeCommitted += (_, _) => committed++;

        Press(form, PhysicalKey.ArrowDown);
        Assert.Equal(1, box.SelectedIndex);
        Assert.Equal(1, committed);

        Press(form, PhysicalKey.ArrowUp);
        Assert.Equal(0, box.SelectedIndex);
        Assert.Equal(2, committed);

        form.Close();
    }

    [AvaloniaFact]
    public void Escape_closes_the_drop_down_and_restores_the_previous_selection()
    {
        var (form, box) = Show(new ComboBox());
        box.Items.AddRange("a", "b", "c");
        box.SelectedIndex = 0;
        box.DroppedDown = true;
        Dispatcher.UIThread.RunJobs();

        // Moving the highlight while open does not commit until Enter or a click does.
        Press(form, PhysicalKey.ArrowDown);
        Press(form, PhysicalKey.Escape);

        Assert.False(box.DroppedDown);
        Assert.Equal(0, box.SelectedIndex);

        form.Close();
    }

    [AvaloniaFact]
    public void Enter_commits_the_highlighted_item_and_closes()
    {
        var (form, box) = Show(new ComboBox());
        box.Items.AddRange("a", "b", "c");
        box.SelectedIndex = 0;
        box.DroppedDown = true;
        Dispatcher.UIThread.RunJobs();

        Press(form, PhysicalKey.ArrowDown);
        Press(form, PhysicalKey.Enter);

        Assert.False(box.DroppedDown);
        Assert.Equal(1, box.SelectedIndex);

        form.Close();
    }

    [AvaloniaFact]
    public void Typing_in_a_DropDownList_combo_jumps_to_the_next_matching_item()
    {
        var (form, box) = Show(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList });
        box.Items.AddRange("Apple", "Banana", "Cherry");
        box.SelectedIndex = 0;

        form.Host!.KeyTextInput("c");

        Assert.Equal(2, box.SelectedIndex);

        form.Close();
    }

    // --- Owner draw ------------------------------------------------------------

    [AvaloniaFact]
    public void OwnerDrawFixed_raises_DrawItem_for_visible_drop_down_rows()
    {
        var (form, box) = Show(new ComboBox { DrawMode = DrawMode.OwnerDrawFixed });
        box.Items.AddRange("a", "b", "c");
        var drawn = new List<int>();
        box.DrawItem += (_, e) => drawn.Add(e.Index);

        box.DroppedDown = true;
        Dispatcher.UIThread.RunJobs();

        // Owner drawing only happens once the popup actually renders a frame.
        using var frame = OpenPopup().PopupWindow!.CaptureRenderedFrame();

        Assert.Equal([0, 1, 2], drawn);

        form.Close();
    }

    [Fact]
    public void Changing_the_selection_from_code_raises_SelectedIndexChanged_then_TextChanged()
    {
        var box = new ComboBox();
        box.Items.AddRange(["Apple", "Banana"]);
        var order = new List<string>();
        box.SelectedIndexChanged += (_, _) => order.Add($"index:{box.Text}");
        box.TextChanged += (_, _) => order.Add($"text:{box.Text}");

        box.SelectedIndex = 1;

        Assert.Equal(["index:Banana", "text:Banana"], order);
    }

    [AvaloniaFact]
    public void Typing_raises_TextUpdate_before_TextChanged()
    {
        var (form, box) = Show(new ComboBox());
        var order = new List<string>();
        box.TextUpdate += (_, _) => order.Add("update");
        box.TextChanged += (_, _) => order.Add("changed");

        form.Host!.KeyTextInput("a");

        Assert.Equal(["update", "changed"], order);
        form.Close();
    }

    [AvaloniaFact]
    public void Disposing_a_combo_with_its_list_open_closes_the_list()
    {
        var (form, box) = Show(new ComboBox());
        box.Items.AddRange(["Apple", "Banana"]);
        box.DroppedDown = true;
        Assert.Contains(Popups.All, popup => popup is not Form);

        box.Dispose();

        Assert.DoesNotContain(Popups.All, popup => popup is not Form);
        form.Close();
    }
}
