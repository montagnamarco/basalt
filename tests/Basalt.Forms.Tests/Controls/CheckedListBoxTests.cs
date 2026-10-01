using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>
/// The check box CheckedListBox adds on top of ListBox: its collections,
/// the ItemCheck event, CheckOnClick, Space, ThreeDCheckBoxes, and the
/// restrictions on SelectionMode and DrawMode.
/// </summary>
public class CheckedListBoxTests
{
    /// <summary>The 13x13 glyph and its margins, exactly as CheckedListBox.Behavior.cs lays them out.</summary>
    private const int CheckBoxSize = 13;

    private const int CheckBoxMargin = 2;

    private static (Form Form, CheckedListBox Box) Show(CheckedListBox box, int height = 100)
    {
        // No frame, so GetItemRectangle's client-relative coordinates line up
        // 1:1 with DrawToBitmap's pixels (DrawToBitmap paints the whole control,
        // frame included, so a Fixed3D border would shift every pixel test by its width).
        box.BorderStyle = BorderStyle.None;
        box.Bounds = new Rectangle(10, 10, 150, height);
        var form = new Form { ClientSize = new Size(200, 150) };
        form.Controls.Add(box);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        box.Focus();

        return (form, box);
    }

    private static void Press(Form form, PhysicalKey key)
    {
        form.Host!.KeyPressQwerty(key, RawInputModifiers.None);
        form.Host!.KeyReleaseQwerty(key, RawInputModifiers.None);
    }

    private static void Click(Form form, CheckedListBox box, int index)
    {
        var rectangle = box.GetItemRectangle(index);
        var point = new Avalonia.Point(box.Left + rectangle.X + 4, box.Top + rectangle.Y + rectangle.Height / 2);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
    }

    /// <summary>The check box's own rectangle for a row, mirroring CheckedListBox.Behavior.cs's layout.</summary>
    private static Rectangle GlyphBounds(Rectangle itemBounds)
    {
        var top = itemBounds.Y + Math.Max((itemBounds.Height - CheckBoxSize) / 2, 0);
        return new Rectangle(itemBounds.X + CheckBoxMargin, top, CheckBoxSize, CheckBoxSize);
    }

    /// <summary>
    /// Whether any pixel inside the glyph's border differs from the plain
    /// background colour. Inset by 2, not just past the 1-pixel border
    /// itself, so the border's anti-aliasing never reads as a false mark.
    /// </summary>
    private static bool HasMarkInside(Bitmap bitmap, Rectangle glyph, Color background)
    {
        var interior = new Rectangle(glyph.X + 2, glyph.Y + 2, glyph.Width - 4, glyph.Height - 4);

        for (var y = interior.Top; y < interior.Bottom; y++)
        {
            for (var x = interior.Left; x < interior.Right; x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() != background.ToArgb()) return true;
            }
        }

        return false;
    }

    // --- ObjectCollection.Add with an initial check state -----------------------

    [Fact]
    public void Add_with_isChecked_sets_the_initial_state_without_raising_ItemCheck()
    {
        var box = new CheckedListBox();
        var raised = 0;
        box.ItemCheck += (_, _) => raised++;

        box.Items.Add("plain");
        box.Items.Add("checked", true);
        box.Items.Add("mixed", CheckState.Indeterminate);

        Assert.Equal(0, raised);
        Assert.False(box.GetItemChecked(0));
        Assert.True(box.GetItemChecked(1));
        Assert.Equal(CheckState.Indeterminate, box.GetItemCheckState(2));
    }

    [Fact]
    public void GetItemCheckState_defaults_to_Unchecked_for_a_newly_added_item()
    {
        var box = new CheckedListBox();
        box.Items.Add("a");

        Assert.Equal(CheckState.Unchecked, box.GetItemCheckState(0));
        Assert.False(box.GetItemChecked(0));
    }

    // --- CheckedItems / CheckedIndices --------------------------------------------

    [Fact]
    public void CheckedItems_and_CheckedIndices_reflect_every_checked_or_indeterminate_item()
    {
        var box = new CheckedListBox();
        box.Items.AddRange(["a", "b", "c"]);
        box.SetItemChecked(0, true);
        box.SetItemCheckState(2, CheckState.Indeterminate);

        Assert.Equal([0, 2], box.CheckedIndices.Cast<int>());
        Assert.Equal(["a", "c"], box.CheckedItems.Cast<object>());

        box.SetItemChecked(0, false);

        Assert.Equal([2], box.CheckedIndices.Cast<int>());
        Assert.Equal(["c"], box.CheckedItems.Cast<object>());
    }

    // --- GetItemChecked / SetItemChecked / GetItemCheckState / SetItemCheckState ---

    [Fact]
    public void SetItemChecked_and_GetItemChecked_round_trip()
    {
        var box = new CheckedListBox();
        box.Items.Add("a");

        box.SetItemChecked(0, true);
        Assert.True(box.GetItemChecked(0));

        box.SetItemChecked(0, false);
        Assert.False(box.GetItemChecked(0));
    }

    [Fact]
    public void ItemCheck_is_raised_before_the_change_and_NewValue_can_be_overridden()
    {
        var box = new CheckedListBox();
        box.Items.Add("a");
        var seen = new List<(int Index, CheckState New, CheckState Current)>();
        box.ItemCheck += (_, e) =>
        {
            seen.Add((e.Index, e.NewValue, e.CurrentValue));
            e.NewValue = CheckState.Indeterminate;
        };

        box.SetItemChecked(0, true);

        Assert.Equal([(0, CheckState.Checked, CheckState.Unchecked)], seen);

        // The handler replaced NewValue, so the item ends up Indeterminate, not Checked.
        Assert.Equal(CheckState.Indeterminate, box.GetItemCheckState(0));
    }

    // --- Checked state follows the item, not the index ----------------------------

    [Fact]
    public void Checked_state_follows_the_item_through_insert_remove_and_sort()
    {
        var box = new CheckedListBox();
        box.Items.Add("alpha");
        box.Items.Add("beta", true);
        box.Items.Add("gamma");

        box.Items.Insert(0, "new-first");
        Assert.Equal(2, box.Items.IndexOf("beta"));
        Assert.True(box.GetItemChecked(2));
        Assert.False(box.GetItemChecked(0));

        box.Items.RemoveAt(0);
        Assert.Equal(1, box.Items.IndexOf("beta"));
        Assert.True(box.GetItemChecked(1));

        box.Sorted = true;
        Assert.True(box.GetItemChecked(box.Items.IndexOf("beta")));
        Assert.False(box.GetItemChecked(box.Items.IndexOf("alpha")));
    }

    // --- SelectionMode and DrawMode restrictions -----------------------------------

    [Fact]
    public void SelectionMode_rejects_every_multi_selection_value()
    {
        var box = new CheckedListBox();

        Assert.Throws<ArgumentException>(() => box.SelectionMode = SelectionMode.MultiSimple);
        Assert.Throws<ArgumentException>(() => box.SelectionMode = SelectionMode.MultiExtended);

        box.SelectionMode = SelectionMode.None;
        box.SelectionMode = SelectionMode.One;
    }

    [Fact]
    public void DrawMode_setter_is_ignored_and_stays_Normal()
    {
        var box = new CheckedListBox();

        box.DrawMode = DrawMode.OwnerDrawFixed;

        Assert.Equal(DrawMode.Normal, box.DrawMode);
    }

    // --- Mouse: CheckOnClick --------------------------------------------------------

    [AvaloniaFact]
    public void CheckOnClick_false_the_first_click_only_selects_a_second_click_toggles()
    {
        var (form, box) = Show(new CheckedListBox());
        box.Items.AddRange(["a", "b"]);

        Click(form, box, 0);
        Assert.Equal(0, box.SelectedIndex);
        Assert.False(box.GetItemChecked(0));

        Click(form, box, 0);
        Assert.True(box.GetItemChecked(0));

        form.Close();
    }

    [AvaloniaFact]
    public void CheckOnClick_false_selecting_a_different_item_does_not_toggle_it()
    {
        var (form, box) = Show(new CheckedListBox());
        box.Items.AddRange(["a", "b"]);
        Click(form, box, 0);

        Click(form, box, 1);

        Assert.False(box.GetItemChecked(1));
        Assert.False(box.GetItemChecked(0));

        form.Close();
    }

    [AvaloniaFact]
    public void CheckOnClick_true_toggles_on_every_click()
    {
        var (form, box) = Show(new CheckedListBox { CheckOnClick = true });
        box.Items.AddRange(["a", "b"]);

        Click(form, box, 0);
        Assert.True(box.GetItemChecked(0));

        Click(form, box, 0);
        Assert.False(box.GetItemChecked(0));

        form.Close();
    }

    // --- Keyboard: Space --------------------------------------------------------------

    [AvaloniaFact]
    public void Space_toggles_the_selected_item_in_SelectionMode_One()
    {
        var (form, box) = Show(new CheckedListBox());
        box.Items.AddRange(["a", "b"]);
        box.SelectedIndex = 1;

        Press(form, PhysicalKey.Space);

        Assert.True(box.GetItemChecked(1));
        Assert.False(box.GetItemChecked(0));

        form.Close();
    }

    [AvaloniaFact]
    public void Space_toggles_the_current_item_in_SelectionMode_None_after_arrow_navigation()
    {
        var (form, box) = Show(new CheckedListBox { SelectionMode = SelectionMode.None });
        box.Items.AddRange(["a", "b", "c"]);

        Press(form, PhysicalKey.ArrowDown);
        Press(form, PhysicalKey.ArrowDown);
        Press(form, PhysicalKey.Space);

        Assert.True(box.GetItemChecked(2));
        Assert.False(box.GetItemChecked(0));
        Assert.False(box.GetItemChecked(1));

        form.Close();
    }

    [AvaloniaFact]
    public void The_current_item_in_SelectionMode_None_follows_its_row_when_an_earlier_item_is_removed()
    {
        var (form, box) = Show(new CheckedListBox { SelectionMode = SelectionMode.None });
        box.Items.AddRange(["a", "b", "c"]);
        Press(form, PhysicalKey.ArrowDown);
        Press(form, PhysicalKey.ArrowDown);

        box.Items.RemoveAt(0);
        Press(form, PhysicalKey.Space);

        Assert.Equal(["c"], box.CheckedItems.Cast<string>());

        form.Close();
    }

    // --- Painting -----------------------------------------------------------------

    [AvaloniaFact]
    public void Checked_items_paint_a_mark_inside_the_box_unchecked_items_do_not()
    {
        var box = new CheckedListBox { BackColor = Color.White };
        box.Items.AddRange(["a", "b"]);
        box.SetItemChecked(1, true);
        var (form, _) = Show(box);

        using var bitmap = new Bitmap(box.Width, box.Height);
        box.DrawToBitmap(bitmap, new Rectangle(0, 0, box.Width, box.Height));

        var uncheckedGlyph = GlyphBounds(box.GetItemRectangle(0));
        var checkedGlyph = GlyphBounds(box.GetItemRectangle(1));

        Assert.False(HasMarkInside(bitmap, uncheckedGlyph, SystemColors.Window));
        Assert.True(HasMarkInside(bitmap, checkedGlyph, SystemColors.Window));

        form.Close();
    }

    [AvaloniaFact]
    public void Default_check_box_draws_a_flat_uniformly_dark_outline()
    {
        var box = new CheckedListBox();
        box.Items.Add("a");
        var (form, _) = Show(box);

        using var bitmap = new Bitmap(box.Width, box.Height);
        box.DrawToBitmap(bitmap, new Rectangle(0, 0, box.Width, box.Height));

        // A flat outline is a single colour all the way around: its bottom-right
        // corner is ControlDark, the same as its top-left.
        var glyph = GlyphBounds(box.GetItemRectangle(0));
        var corner = bitmap.GetPixel(glyph.Right - 1, glyph.Bottom - 1);

        Assert.Equal(SystemColors.ControlDark.ToArgb(), corner.ToArgb());

        form.Close();
    }

    [AvaloniaFact]
    public void ThreeDCheckBoxes_paints_a_sunken_bevel_instead_of_the_flat_outline()
    {
        var box = new CheckedListBox { ThreeDCheckBoxes = true };
        box.Items.Add("a");
        var (form, _) = Show(box);

        using var bitmap = new Bitmap(box.Width, box.Height);
        box.DrawToBitmap(bitmap, new Rectangle(0, 0, box.Width, box.Height));

        // A sunken bevel paints its bottom-right corner in ControlLightLight,
        // the highlight side of the bevel - a flat box never uses that colour.
        var glyph = GlyphBounds(box.GetItemRectangle(0));
        var corner = bitmap.GetPixel(glyph.Right - 1, glyph.Bottom - 1);

        Assert.Equal(SystemColors.ControlLightLight.ToArgb(), corner.ToArgb());

        form.Close();
    }

    [Fact]
    public void Two_equal_items_have_a_check_box_each()
    {
        var box = new CheckedListBox();
        box.Items.Add("Apple");
        box.Items.Add("Apple");

        box.SetItemChecked(1, true);

        Assert.False(box.GetItemChecked(0));
        Assert.True(box.GetItemChecked(1));
        Assert.Equal([1], box.CheckedIndices.Cast<int>());
    }

    [Fact]
    public void Check_marks_follow_their_rows_through_sorting_and_clearing()
    {
        var box = new CheckedListBox();
        box.Items.Add("pear", true);
        box.Items.Add("apple");

        box.Sorted = true;
        Assert.Equal([1], box.CheckedIndices.Cast<int>());

        box.Items.Clear();
        box.Items.AddRange(["x", "y", "z"]);
        Assert.Empty(box.CheckedIndices.Cast<int>());
        box.SetItemChecked(2, true);
        Assert.True(box.GetItemChecked(2));
    }
}
