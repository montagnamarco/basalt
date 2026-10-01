using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>Items, columns, groups, selection and checking, Details painting, and the keyboard, mouse and scrolling of ListView.</summary>
public class ListViewTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static Form ShowWith(ListView listView, Size? clientSize = null)
    {
        listView.Font = Font;
        var form = new Form { ClientSize = clientSize ?? new Size(300, 200) };
        form.Controls.Add(listView);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        listView.Focus();

        return form;
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    private static void Click(Form form, Control control, Point clientPoint)
    {
        var point = new Avalonia.Point(control.Left + clientPoint.X, control.Top + clientPoint.Y);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
    }

    // --- Items, columns and keys ----------------------------------------------

    [Fact]
    public void Items_add_insert_remove_and_find_by_key()
    {
        var listView = new ListView();

        listView.Items.Add("one");
        var inserted = listView.Items.Insert(0, "inserted");
        listView.Items.Add("two");

        Assert.Equal(["inserted", "one", "two"], listView.Items.Cast<ListViewItem>().Select(i => i.Text));
        Assert.Equal(0, inserted.Index);

        inserted.Name = "the-key";
        Assert.Same(inserted, listView.Items["the-key"]);
        Assert.Equal(0, listView.Items.IndexOfKey("the-key"));

        listView.Items.Remove(inserted);
        Assert.Equal(["one", "two"], listView.Items.Cast<ListViewItem>().Select(i => i.Text));
        Assert.Null(inserted.ListView);

        listView.Items.Clear();
        Assert.Empty(listView.Items);
    }

    [Fact]
    public void SubItems_hold_the_text_of_the_other_columns()
    {
        var item = new ListViewItem(["name", "size", "date"]);

        Assert.Equal("name", item.Text);
        Assert.Equal(3, item.SubItems.Count);
        Assert.Equal("size", item.SubItems[1].Text);
        Assert.Equal("date", item.SubItems[2].Text);
    }

    [Fact]
    public void Columns_add_sets_width_and_raises_ColumnWidthChanged()
    {
        var listView = new ListView();
        var raised = -1;
        listView.ColumnWidthChanged += (_, e) => raised = e.ColumnIndex;

        var column = listView.Columns.Add("Name", 80);
        Assert.Equal(80, column.Width);
        Assert.Equal(0, column.Index);

        column.Width = 120;
        Assert.Equal(0, raised);
        Assert.Equal(120, listView.Columns[0].Width);
    }

    [Fact]
    public void Sorting_orders_items_by_text()
    {
        var listView = new ListView();
        listView.Items.AddRange(new ListViewItem("banana"), new ListViewItem("apple"), new ListViewItem("cherry"));

        listView.Sorting = SortOrder.Ascending;
        Assert.Equal(["apple", "banana", "cherry"], listView.Items.Cast<ListViewItem>().Select(i => i.Text));

        listView.Sorting = SortOrder.Descending;
        Assert.Equal(["cherry", "banana", "apple"], listView.Items.Cast<ListViewItem>().Select(i => i.Text));
    }

    // --- Selection -------------------------------------------------------------

    [Fact]
    public void Selecting_an_item_raises_ItemSelectionChanged_then_SelectedIndexChanged_once()
    {
        var listView = new ListView();
        var a = listView.Items.Add("a");
        listView.Items.Add("b");
        var selectionChanges = 0;
        var selectedIndexChanges = 0;
        listView.ItemSelectionChanged += (_, e) => { if (e.IsSelected) selectionChanges++; };
        listView.SelectedIndexChanged += (_, _) => selectedIndexChanges++;

        a.Selected = true;

        Assert.Equal(1, selectionChanges);
        Assert.Equal(1, selectedIndexChanges);
        Assert.Equal([0], listView.SelectedIndices.Cast<int>());
        Assert.Same(a, listView.SelectedItems[0]);
    }

    [Fact]
    public void MultiSelect_false_keeps_only_the_most_recently_selected_item()
    {
        var listView = new ListView { MultiSelect = false };
        var a = listView.Items.Add("a");
        var b = listView.Items.Add("b");

        a.Selected = true;
        b.Selected = true;

        Assert.False(a.Selected);
        Assert.True(b.Selected);
        Assert.Equal([1], listView.SelectedIndices.Cast<int>());
    }

    // --- Checking ----------------------------------------------------------

    [Fact]
    public void Checking_an_item_raises_ItemCheck_before_ItemChecked_and_ItemCheck_can_override_the_value()
    {
        var listView = new ListView { CheckBoxes = true };
        var item = listView.Items.Add("a");
        var order = new List<string>();
        listView.ItemCheck += (_, e) =>
        {
            order.Add("check");
            e.NewValue = CheckState.Unchecked; // Vetoes the requested value, as a real handler might.
        };
        listView.ItemChecked += (_, _) => order.Add("checked");

        item.Checked = true;

        Assert.False(item.Checked);
        Assert.Equal(["check"], order); // ItemChecked never adds "checked": the vetoed value caused no real change.
        Assert.Empty(listView.CheckedItems);
    }

    [Fact]
    public void CheckedItems_and_CheckedIndices_track_Checked_items_in_order()
    {
        var listView = new ListView { CheckBoxes = true };
        var a = listView.Items.Add("a");
        listView.Items.Add("b");
        var c = listView.Items.Add("c");

        a.Checked = true;
        c.Checked = true;

        Assert.Equal([0, 2], listView.CheckedIndices.Cast<int>());
        Assert.Equal(["a", "c"], listView.CheckedItems.Cast<ListViewItem>().Select(i => i.Text));
    }

    // --- Details painting --------------------------------------------------

    [AvaloniaFact]
    public void Details_header_cells_are_drawn_as_classic_raised_buttons()
    {
        var listView = new ListView { View = View.Details, BorderStyle = BorderStyle.None };
        listView.Columns.Add("Name", 80);
        listView.Items.Add("row"); // Only to make GetItemRect(0) answer where the header ends.
        ShowWith(listView);

        var bitmap = Draw(listView);

        // A raised button's top-left pixel is the light highlight; its bottom-right is the dark shadow.
        var headerHeight = listView.GetItemRect(0).Y;
        Assert.True(headerHeight > 0, "Details view must reserve space for the column header.");

        Assert.Equal(SystemColors.ControlLightLight.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(SystemColors.ControlDarkDark.ToArgb(), bitmap.GetPixel(79, headerHeight - 1).ToArgb());
    }

    [AvaloniaFact]
    public void A_selected_rows_background_is_the_highlight_colour()
    {
        var listView = new ListView { View = View.Details };
        var item = listView.Items.Add("selected");
        listView.Items.Add("unselected");
        ShowWith(listView);
        item.Selected = true;

        var bitmap = Draw(listView);
        var selectedRow = listView.GetItemRect(0);
        var unselectedRow = listView.GetItemRect(1);

        Assert.Equal(SystemColors.Highlight.ToArgb(), bitmap.GetPixel(5, selectedRow.Y + selectedRow.Height / 2).ToArgb());
        Assert.NotEqual(SystemColors.Highlight.ToArgb(), bitmap.GetPixel(5, unselectedRow.Y + unselectedRow.Height / 2).ToArgb());
    }

    [AvaloniaFact]
    public void FullRowSelect_highlights_every_column_not_only_the_first()
    {
        var listView = new ListView { View = View.Details, FullRowSelect = true };
        listView.Columns.Add("A", 40);
        listView.Columns.Add("B", 40);
        var item = listView.Items.Add("row");
        item.SubItems.Add(string.Empty); // Blank, so only the fill colour is sampled, not a text glyph.
        ShowWith(listView);
        item.Selected = true;

        var bitmap = Draw(listView);
        var row = listView.GetItemRect(0);

        // x = 60 lands in the second column, past the 40-pixel-wide first one.
        Assert.Equal(SystemColors.Highlight.ToArgb(), bitmap.GetPixel(60, row.Y + row.Height / 2).ToArgb());
    }

    [AvaloniaFact]
    public void Without_FullRowSelect_only_the_first_column_is_highlighted()
    {
        var listView = new ListView { View = View.Details, FullRowSelect = false };
        listView.Columns.Add("A", 40);
        listView.Columns.Add("B", 40);
        var item = listView.Items.Add("row");
        item.SubItems.Add(string.Empty); // Blank, so only the fill colour is sampled, not a text glyph.
        ShowWith(listView);
        item.Selected = true;

        var bitmap = Draw(listView);
        var row = listView.GetItemRect(0);

        Assert.NotEqual(SystemColors.Highlight.ToArgb(), bitmap.GetPixel(60, row.Y + row.Height / 2).ToArgb());
    }

    [AvaloniaFact]
    public void GridLines_draws_a_separator_under_every_row()
    {
        var listView = new ListView { View = View.Details, GridLines = true, BorderStyle = BorderStyle.None };
        listView.Columns.Add("Name", 80);
        listView.Items.Add("a");
        listView.Items.Add("b");
        ShowWith(listView);

        var bitmap = Draw(listView);
        var firstRowBottom = listView.GetItemRect(0).Bottom - 1;

        Assert.Equal(SystemColors.ControlLight.ToArgb(), bitmap.GetPixel(10, firstRowBottom).ToArgb());
    }

    // --- Mouse ---------------------------------------------------------------

    [AvaloniaFact]
    public void A_click_selects_the_item_under_it_and_sets_the_focused_item()
    {
        var listView = new ListView { View = View.Details };
        listView.Columns.Add("Name", 100);
        listView.Items.Add("a");
        var b = listView.Items.Add("b");
        var form = ShowWith(listView);

        var rowB = listView.GetItemRect(1);
        Click(form, listView, new Point(10, rowB.Y + rowB.Height / 2));

        Assert.True(b.Selected);
        Assert.Same(b, listView.FocusedItem);

        form.Close();
    }

    [AvaloniaFact]
    public void Ctrl_click_adds_to_the_selection_when_MultiSelect_is_true()
    {
        var listView = new ListView { View = View.Details };
        listView.Columns.Add("Name", 100);
        var a = listView.Items.Add("a");
        var b = listView.Items.Add("b");
        var form = ShowWith(listView);

        var rowA = listView.GetItemRect(0);
        var rowB = listView.GetItemRect(1);
        Click(form, listView, new Point(10, rowA.Y + rowA.Height / 2));

        var point = new Avalonia.Point(listView.Left + 10, listView.Top + rowB.Y + rowB.Height / 2);
        form.Host!.MouseDown(point, MouseButton.Left, RawInputModifiers.Control);
        form.Host!.MouseUp(point, MouseButton.Left, RawInputModifiers.Control);

        Assert.True(a.Selected);
        Assert.True(b.Selected);

        form.Close();
    }

    [AvaloniaFact]
    public void Shift_click_selects_the_range_between_the_focused_item_and_the_click()
    {
        var listView = new ListView { View = View.Details };
        listView.Columns.Add("Name", 100);
        listView.Items.Add("a");
        listView.Items.Add("b");
        listView.Items.Add("c");
        var form = ShowWith(listView);

        var rowA = listView.GetItemRect(0);
        var rowC = listView.GetItemRect(2);
        Click(form, listView, new Point(10, rowA.Y + rowA.Height / 2));

        var point = new Avalonia.Point(listView.Left + 10, listView.Top + rowC.Y + rowC.Height / 2);
        form.Host!.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
        form.Host!.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);

        Assert.Equal([0, 1, 2], listView.SelectedIndices.Cast<int>());

        form.Close();
    }

    [AvaloniaFact]
    public void Clicking_the_checkbox_toggles_Checked_without_changing_the_selection()
    {
        var listView = new ListView { View = View.Details, CheckBoxes = true };
        listView.Columns.Add("Name", 100);
        var item = listView.Items.Add("a");
        var form = ShowWith(listView);

        var row = listView.GetItemRect(0);
        Click(form, listView, new Point(6, row.Y + row.Height / 2));

        Assert.True(item.Checked);
        Assert.False(item.Selected);

        form.Close();
    }

    [AvaloniaFact]
    public void A_double_click_on_an_item_raises_ItemActivate()
    {
        var listView = new ListView { View = View.Details };
        listView.Columns.Add("Name", 100);
        listView.Items.Add("a");
        var form = ShowWith(listView);
        var activations = 0;
        listView.ItemActivate += (_, _) => activations++;

        var row = listView.GetItemRect(0);
        var point = new Avalonia.Point(listView.Left + 10, listView.Top + row.Y + row.Height / 2);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);

        Assert.Equal(1, activations);

        form.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_column_header_raises_ColumnClick()
    {
        var listView = new ListView { View = View.Details };
        listView.Columns.Add("Name", 80);
        listView.Columns.Add("Size", 60);
        var form = ShowWith(listView);
        var clicked = -1;
        listView.ColumnClick += (_, e) => clicked = e.Column;

        Click(form, listView, new Point(100, 5)); // Inside the second column, well clear of the divider.

        Assert.Equal(1, clicked);

        form.Close();
    }

    [AvaloniaFact]
    public void Dragging_a_column_divider_resizes_the_column_instead_of_raising_ColumnClick()
    {
        var listView = new ListView { View = View.Details };
        listView.Columns.Add("Name", 80);
        var form = ShowWith(listView);
        var clicked = false;
        listView.ColumnClick += (_, _) => clicked = true;

        var divider = new Avalonia.Point(listView.Left + 80, listView.Top + 5);
        form.Host!.MouseDown(divider, MouseButton.Left);
        form.Host!.MouseMove(new Avalonia.Point(listView.Left + 110, listView.Top + 5));
        form.Host!.MouseUp(new Avalonia.Point(listView.Left + 110, listView.Top + 5), MouseButton.Left);

        Assert.Equal(110, listView.Columns[0].Width);
        Assert.False(clicked);

        form.Close();
    }

    // --- Keyboard ------------------------------------------------------------

    [AvaloniaFact]
    public void Arrow_keys_move_the_focused_and_selected_item_in_Details_view()
    {
        var listView = new ListView { View = View.Details };
        listView.Columns.Add("Name", 100);
        listView.Items.Add("a");
        listView.Items.Add("b");
        listView.Items.Add("c");
        var form = ShowWith(listView);
        listView.Items[0].Selected = true;
        listView.FocusedItem = listView.Items[0];

        form.Host!.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

        Assert.Same(listView.Items[1], listView.FocusedItem);
        Assert.True(listView.Items[1].Selected);
        Assert.False(listView.Items[0].Selected);

        form.Close();
    }

    [AvaloniaFact]
    public void Space_toggles_Checked_on_the_focused_item()
    {
        var listView = new ListView { View = View.Details, CheckBoxes = true };
        var item = listView.Items.Add("a");
        var form = ShowWith(listView);
        listView.FocusedItem = item;

        form.Host!.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

        Assert.True(item.Checked);

        form.Close();
    }

    // --- Scrolling -------------------------------------------------------------

    [Fact]
    public void EnsureVisible_scrolls_a_far_item_into_the_viewport_in_Details_view()
    {
        var listView = new ListView { View = View.Details, Font = Font, Bounds = new Rectangle(0, 0, 200, 60) };
        listView.Columns.Add("Name", 150);
        for (var i = 0; i < 30; i++) listView.Items.Add($"item {i}");

        var lastIndex = listView.Items.Count - 1;
        var beforeTop = listView.GetItemRect(lastIndex).Top;
        Assert.True(beforeTop > listView.ClientSize.Height, "The last item must start out below the visible area.");

        listView.EnsureVisible(lastIndex);

        var afterTop = listView.GetItemRect(lastIndex).Top;
        Assert.True(afterTop >= 0 && afterTop < listView.ClientSize.Height, $"Expected the last item inside the viewport, got top {afterTop}.");
    }

    [Fact]
    public void List_view_arranges_items_top_to_bottom_then_wraps_into_the_next_column()
    {
        var listView = new ListView { View = View.List, Font = Font, Bounds = new Rectangle(0, 0, 400, 40) };
        for (var i = 0; i < 6; i++) listView.Items.Add($"item {i}");

        var first = listView.GetItemRect(0);
        var second = listView.GetItemRect(1);

        // The viewport is only 40 pixels tall, so the second item cannot fit under the first: it starts a new column.
        Assert.Equal(first.Y, second.Y);
        Assert.True(second.X > first.X, "The second item should have wrapped into the next column.");
    }

    [Fact]
    public void Removing_a_selected_item_reports_its_deselection()
    {
        var listView = new ListView();
        var item = listView.Items.Add("a");
        listView.Items.Add("b");
        item.Selected = true;
        var deselected = new List<string>();
        var indexChanges = 0;
        listView.ItemSelectionChanged += (_, e) => { if (!e.IsSelected) deselected.Add(e.Item!.Text); };
        listView.SelectedIndexChanged += (_, _) => indexChanges++;

        listView.Items.Remove(item);

        Assert.Equal(["a"], deselected);
        Assert.Equal(1, indexChanges);
        Assert.False(item.Selected);
        Assert.Empty(listView.SelectedIndices.Cast<int>());
    }

    [Fact]
    public void An_item_owned_by_one_ListView_cannot_be_added_to_another()
    {
        var first = new ListView();
        var item = first.Items.Add("a");

        Assert.Throws<ArgumentException>(() => new ListView().Items.Add(item));
    }

    [AvaloniaFact]
    public void A_second_shift_click_pivots_on_the_first_item_and_touches_only_changed_items()
    {
        var listView = new ListView { View = View.Details };
        listView.Columns.Add("Name", 100);
        listView.Items.Add("a");
        listView.Items.Add("b");
        listView.Items.Add("c");
        var form = ShowWith(listView);

        void ShiftClick(int index)
        {
            var row = listView.GetItemRect(index);
            var point = new Avalonia.Point(listView.Left + 10, listView.Top + row.Y + row.Height / 2);
            form.Host!.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
            form.Host!.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
        }

        var rowA = listView.GetItemRect(0);
        Click(form, listView, new Point(10, rowA.Y + rowA.Height / 2));
        ShiftClick(2);

        var changes = new List<string>();
        listView.ItemSelectionChanged += (_, e) => changes.Add($"{e.Item!.Text}:{e.IsSelected}");
        ShiftClick(1);

        Assert.Equal([0, 1], listView.SelectedIndices.Cast<int>());
        Assert.Equal(["c:False"], changes);

        form.Close();
    }
}
