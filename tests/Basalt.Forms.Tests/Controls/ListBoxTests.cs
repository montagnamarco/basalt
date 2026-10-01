using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>Items, selection modes, scrolling, owner drawing, and the keyboard and mouse of ListBox.</summary>
public class ListBoxTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static (Form Form, ListBox Box) Show(ListBox box, int height = 100)
    {
        box.Font = Font;
        box.Bounds = new Rectangle(10, 10, 150, height);
        var form = new Form { ClientSize = new Size(200, 150) };
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

    private static void Click(Form form, ListBox box, int index)
    {
        var rectangle = box.GetItemRectangle(index);
        var point = new Avalonia.Point(box.Left + rectangle.X + 4, box.Top + rectangle.Y + rectangle.Height / 2);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
    }

    // --- Items -----------------------------------------------------------

    [Fact]
    public void Items_add_insert_remove_and_clear()
    {
        var box = new ListBox();

        box.Items.Add("one");
        box.Items.AddRange(["two", "three"]);
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
        var box = new ListBox { Sorted = true };

        box.Items.Add("banana");
        box.Items.Add("apple");
        box.Items.Add("cherry");

        Assert.Equal(["apple", "banana", "cherry"], box.Items.Cast<object>());
    }

    [Fact]
    public void Items_cannot_be_edited_directly_while_a_DataSource_drives_them()
    {
        var box = new ListBox { DataSource = new[] { "a", "b" } };

        Assert.Throws<ArgumentException>(() => box.Items.Add("c"));
        Assert.Throws<ArgumentException>(() => box.Items.Clear());
    }

    // --- Selection modes ---------------------------------------------------

    [Fact]
    public void SelectedIndex_in_One_mode_replaces_the_previous_selection()
    {
        var box = new ListBox();
        box.Items.AddRange(["a", "b", "c"]);

        box.SelectedIndex = 0;
        box.SelectedIndex = 2;

        Assert.Equal(2, box.SelectedIndex);
        Assert.Equal("c", box.SelectedItem);
        Assert.Equal([2], box.SelectedIndices.Cast<int>());
    }

    [Fact]
    public void SetSelected_in_MultiSimple_adds_without_clearing_other_selections()
    {
        var box = new ListBox { SelectionMode = SelectionMode.MultiSimple };
        box.Items.AddRange(["a", "b", "c"]);

        box.SetSelected(0, true);
        box.SetSelected(2, true);

        Assert.Equal([0, 2], box.SelectedIndices.Cast<int>());
        Assert.Equal(["a", "c"], box.SelectedItems.Cast<object>());

        box.SetSelected(0, false);
        Assert.Equal([2], box.SelectedIndices.Cast<int>());
    }

    [Fact]
    public void SelectionMode_None_rejects_every_selection_attempt()
    {
        var box = new ListBox { SelectionMode = SelectionMode.None };
        box.Items.AddRange(["a", "b"]);

        box.SetSelected(0, true);

        Assert.False(box.GetSelected(0));
        Assert.Equal(-1, box.SelectedIndex);
    }

    [Fact]
    public void ClearSelected_empties_the_selection()
    {
        var box = new ListBox { SelectionMode = SelectionMode.MultiSimple };
        box.Items.AddRange(["a", "b", "c"]);
        box.SetSelected(0, true);
        box.SetSelected(1, true);

        box.ClearSelected();

        Assert.Empty(box.SelectedIndices);
        Assert.Equal(-1, box.SelectedIndex);
    }

    [Fact]
    public void SelectedIndexChanged_is_raised_exactly_once_per_change()
    {
        var box = new ListBox { SelectionMode = SelectionMode.MultiSimple };
        box.Items.AddRange(["a", "b", "c"]);
        var raises = 0;
        box.SelectedIndexChanged += (_, _) => raises++;

        box.SetSelected(0, true);
        Assert.Equal(1, raises);

        // Selecting the same state again changes nothing: no second event.
        box.SetSelected(0, true);
        Assert.Equal(1, raises);

        box.SetSelected(1, true);
        Assert.Equal(2, raises);

        box.ClearSelected();
        Assert.Equal(3, raises);
    }

    // --- ItemHeight, IntegralHeight, TopIndex -------------------------------

    [Fact]
    public void ItemHeight_tracks_the_font_until_an_owner_draws_it()
    {
        var box = new ListBox { Font = Font };
        var atDefaultFont = box.ItemHeight;

        box.Font = new Font("Microsoft Sans Serif", 20f);

        Assert.True(box.ItemHeight > atDefaultFont, $"Expected a taller item after a bigger font, got {box.ItemHeight} vs {atDefaultFont}.");
    }

    [Fact]
    public void IntegralHeight_rounds_the_height_to_a_whole_number_of_items()
    {
        var box = new ListBox { Font = Font, IntegralHeight = true };

        box.Height = box.ItemHeight * 3 + 5;

        var frame = box.Height - box.ClientSize.Height;
        Assert.Equal(0, (box.Height - frame) % box.ItemHeight);
    }

    [Fact]
    public void TopIndex_scrolls_and_clamps_to_what_the_list_can_show()
    {
        var box = new ListBox { Font = Font, IntegralHeight = false, Height = 50 };
        for (var i = 0; i < 20; i++) box.Items.Add($"item {i}");

        box.TopIndex = 5;
        Assert.Equal(5, box.TopIndex);

        box.TopIndex = 1000;
        Assert.True(box.TopIndex < 20, $"TopIndex should clamp below the item count, got {box.TopIndex}.");

        box.TopIndex = -5;
        Assert.Equal(0, box.TopIndex);
    }

    // --- FindString ----------------------------------------------------------

    [Fact]
    public void FindString_wraps_and_FindStringExact_requires_the_whole_text()
    {
        var box = new ListBox();
        box.Items.AddRange(["Apple", "Banana", "Apricot"]);

        Assert.Equal(0, box.FindString("Ap"));
        Assert.Equal(2, box.FindString("Ap", 0));
        Assert.Equal(0, box.FindString("ap", 2));

        Assert.Equal(-1, box.FindStringExact("Ap"));
        Assert.Equal(0, box.FindStringExact("apple"));
    }

    // --- GetItemRectangle / pixels -------------------------------------------

    [Fact]
    public void GetItemRectangle_places_items_one_below_the_other_by_ItemHeight()
    {
        var box = new ListBox { Font = Font };
        box.Items.AddRange(["a", "b", "c"]);

        var first = box.GetItemRectangle(0);
        var second = box.GetItemRectangle(1);

        Assert.Equal(0, first.Y);
        Assert.Equal(box.ItemHeight, second.Y - first.Y);
        Assert.Equal(box.ItemHeight, first.Height);
    }

    [AvaloniaFact]
    public void The_selected_item_paints_with_the_highlight_colour()
    {
        var box = new ListBox { BackColor = Color.White };
        box.Items.AddRange(["a", "b", "c"]);
        var (form, listBox) = Show(box);
        listBox.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        using var bitmap = new Bitmap(listBox.Width, listBox.Height);
        listBox.DrawToBitmap(bitmap, new Rectangle(0, 0, listBox.Width, listBox.Height));

        var rectangle = listBox.GetItemRectangle(1);
        var pixel = bitmap.GetPixel(rectangle.X + 4, rectangle.Y + rectangle.Height / 2);

        Assert.Equal(SystemColors.Highlight.ToArgb(), pixel.ToArgb());

        form.Close();
    }

    // --- BeginUpdate / EndUpdate ----------------------------------------------

    [Fact]
    public void BeginUpdate_and_EndUpdate_do_not_lose_or_duplicate_items()
    {
        var box = new ListBox();

        box.BeginUpdate();
        box.Items.Add("a");
        box.Items.Add("b");
        box.EndUpdate();

        Assert.Equal(2, box.Items.Count);
    }

    // --- Owner draw ------------------------------------------------------------

    [Fact]
    public void OwnerDrawFixed_raises_DrawItem_for_every_visible_item()
    {
        var box = new ListBox { Font = Font, DrawMode = DrawMode.OwnerDrawFixed, Height = 100 };
        box.Items.AddRange(["a", "b", "c"]);
        var drawn = new List<int>();
        box.DrawItem += (_, e) => drawn.Add(e.Index);

        using var bitmap = new Bitmap(box.Width, box.Height);
        box.DrawToBitmap(bitmap, new Rectangle(0, 0, box.Width, box.Height));

        Assert.Equal([0, 1, 2], drawn);
    }

    [Fact]
    public void OwnerDrawVariable_raises_MeasureItem_and_uses_its_height()
    {
        var box = new ListBox { Font = Font, DrawMode = DrawMode.OwnerDrawVariable };
        box.MeasureItem += (_, e) => e.ItemHeight = e.Index == 1 ? 40 : 10;

        box.Items.AddRange(["short", "tall", "short"]);

        Assert.Equal(10, box.GetItemHeight(0));
        Assert.Equal(40, box.GetItemHeight(1));
        Assert.Equal(10, box.GetItemHeight(2));

        var second = box.GetItemRectangle(1);
        var third = box.GetItemRectangle(2);
        Assert.Equal(10, second.Y);
        Assert.Equal(50, third.Y);
    }

    // --- Keyboard --------------------------------------------------------------

    [AvaloniaFact]
    public void Arrow_keys_move_the_selection_in_One_mode()
    {
        var (form, box) = Show(new ListBox());
        box.Items.AddRange(["a", "b", "c"]);
        box.SelectedIndex = 0;

        Press(form, PhysicalKey.ArrowDown);
        Assert.Equal(1, box.SelectedIndex);

        Press(form, PhysicalKey.ArrowDown);
        Assert.Equal(2, box.SelectedIndex);

        Press(form, PhysicalKey.ArrowUp);
        Assert.Equal(1, box.SelectedIndex);

        Press(form, PhysicalKey.End);
        Assert.Equal(2, box.SelectedIndex);

        Press(form, PhysicalKey.Home);
        Assert.Equal(0, box.SelectedIndex);

        form.Close();
    }

    [AvaloniaFact]
    public void Space_toggles_the_focused_item_in_MultiSimple()
    {
        var box = new ListBox { SelectionMode = SelectionMode.MultiSimple };
        box.Items.AddRange(["a", "b", "c"]);
        var (form, _) = Show(box);

        // Focusing a MultiSimple box with nothing selected puts the keyboard cursor on the first item.
        Press(form, PhysicalKey.Space);
        Assert.True(box.GetSelected(0));

        Press(form, PhysicalKey.Space);
        Assert.False(box.GetSelected(0));

        form.Close();
    }

    [AvaloniaFact]
    public void Shift_arrow_extends_a_range_in_MultiExtended()
    {
        var (form, box) = Show(new ListBox { SelectionMode = SelectionMode.MultiExtended });
        box.Items.AddRange(["a", "b", "c", "d"]);
        Click(form, box, 0);

        Press(form, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Press(form, PhysicalKey.ArrowDown, RawInputModifiers.Shift);

        Assert.Equal([0, 1, 2], box.SelectedIndices.Cast<int>());

        form.Close();
    }

    [AvaloniaFact]
    public void A_first_shift_click_selects_from_the_first_item_when_nothing_set_an_anchor()
    {
        var (form, box) = Show(new ListBox { SelectionMode = SelectionMode.MultiExtended });
        box.Items.AddRange(["a", "b", "c", "d"]);

        var rectangle = box.GetItemRectangle(2);
        var point = new Avalonia.Point(box.Left + rectangle.X + 4, box.Top + rectangle.Y + rectangle.Height / 2);
        form.Host!.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
        form.Host!.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);

        Assert.Equal([0, 1, 2], box.SelectedIndices.Cast<int>());

        form.Close();
    }

    [Fact]
    public void Switching_between_multiple_selection_modes_keeps_the_selection_without_an_event()
    {
        var box = new ListBox { SelectionMode = SelectionMode.MultiExtended };
        box.Items.AddRange(["a", "b", "c"]);
        box.SetSelected(0, true);
        box.SetSelected(2, true);
        var raised = 0;
        box.SelectedIndexChanged += (_, _) => raised++;

        box.SelectionMode = SelectionMode.MultiSimple;
        Assert.Equal(0, raised);
        Assert.Equal([0, 2], box.SelectedIndices.Cast<int>());

        box.SelectionMode = SelectionMode.One;
        Assert.Equal(1, raised);
        Assert.Equal([0], box.SelectedIndices.Cast<int>());
    }

    [Fact]
    public void Sorting_a_filled_list_keeps_the_same_item_selected()
    {
        var box = new ListBox();
        box.Items.AddRange(["pear", "apple", "fig"]);
        box.SelectedIndex = 0;

        box.Sorted = true;

        Assert.Equal("pear", box.SelectedItem);
        Assert.Equal(2, box.SelectedIndex);
    }

    [AvaloniaFact]
    public void Typing_a_letter_jumps_to_the_next_item_starting_with_it()
    {
        var (form, box) = Show(new ListBox());
        box.Items.AddRange(["Apple", "Banana", "Cherry"]);
        box.SelectedIndex = 0;

        form.Host!.KeyTextInput("c");

        Assert.Equal(2, box.SelectedIndex);

        form.Close();
    }

    // --- Mouse -----------------------------------------------------------------

    [AvaloniaFact]
    public void Clicking_an_item_selects_it()
    {
        var (form, box) = Show(new ListBox());
        box.Items.AddRange(["a", "b", "c"]);

        Click(form, box, 2);

        Assert.Equal(2, box.SelectedIndex);
        Assert.True(box.Focused);

        form.Close();
    }

    [AvaloniaFact]
    public void Ctrl_clicking_toggles_an_item_in_MultiExtended_without_clearing_others()
    {
        var (form, box) = Show(new ListBox { SelectionMode = SelectionMode.MultiExtended });
        box.Items.AddRange(["a", "b", "c"]);
        Click(form, box, 0);

        var point = new Avalonia.Point(box.Left + 4, box.Top + box.GetItemRectangle(2).Y + 4);
        form.Host!.MouseDown(point, MouseButton.Left, RawInputModifiers.Control);
        form.Host!.MouseUp(point, MouseButton.Left, RawInputModifiers.Control);

        Assert.Equal([0, 2], box.SelectedIndices.Cast<int>());

        form.Close();
    }

    [AvaloniaFact]
    public void Dragging_the_mouse_extends_the_selection_in_MultiExtended()
    {
        var (form, box) = Show(new ListBox { SelectionMode = SelectionMode.MultiExtended });
        box.Items.AddRange(["a", "b", "c", "d"]);

        var start = new Avalonia.Point(box.Left + 4, box.Top + box.GetItemRectangle(0).Y + 4);
        var end = new Avalonia.Point(box.Left + 4, box.Top + box.GetItemRectangle(2).Y + 4);

        form.Host!.MouseDown(start, MouseButton.Left);
        // The headless pointer does not remember a button across calls: a drag must repeat it on every move.
        form.Host!.MouseMove(end, RawInputModifiers.LeftMouseButton);
        form.Host!.MouseUp(end, MouseButton.Left);

        Assert.Equal([0, 1, 2], box.SelectedIndices.Cast<int>());

        form.Close();
    }

    [AvaloniaFact]
    public void A_double_click_raises_DoubleClick()
    {
        var (form, box) = Show(new ListBox());
        box.Items.AddRange(["a", "b"]);
        var doubleClicks = 0;
        box.DoubleClick += (_, _) => doubleClicks++;

        var point = new Avalonia.Point(box.Left + 4, box.Top + box.GetItemRectangle(0).Y + 4);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);

        Assert.Equal(1, doubleClicks);

        form.Close();
    }

    // --- Data binding ------------------------------------------------------------

    [Fact]
    public void DataSource_and_DisplayMember_populate_the_items_and_their_text()
    {
        var box = new ListBox { BindingContext = new BindingContext() };
        box.DataSource = new[] { new Person("Ann"), new Person("Bo") };
        box.DisplayMember = "Name";

        Assert.Equal(2, box.Items.Count);
        Assert.Equal("Ann", box.GetItemText(box.Items[0]));
        Assert.Equal("Bo", box.GetItemText(box.Items[1]));
    }

    private sealed class Person(string name)
    {
        public string Name { get; } = name;
    }
}
