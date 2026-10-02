using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>
/// VirtualMode's real layout/paint/hit-test/scroll path for View.Details and View.List: a list of
/// 100,000 rows actually paints, scrolls and can be clicked, painting and RetrieveVirtualItem stay
/// limited to the rows on screen, and CacheVirtualItems fires once per distinct visible range.
/// </summary>
public class ListViewVirtualModeLayoutTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static ListView MakeVirtualList(int size, View view = View.Details)
    {
        var list = new ListView
        {
            VirtualMode = true, VirtualListSize = size, View = view,
            Font = Font, BorderStyle = BorderStyle.None, Bounds = new Rectangle(0, 0, 200, 150),
        };
        if (view == View.Details) list.Columns.Add("Name", 150);
        list.RetrieveVirtualItem += (_, e) => e.Item = new ListViewItem($"Row {e.ItemIndex}");

        return list;
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    [Fact]
    public void A_100_000_row_VirtualMode_list_paints_row_0_and_a_row_far_beyond_1_000_once_scrolled_into_view()
    {
        var list = MakeVirtualList(100_000);
        list.SelectedIndices.Add(0);

        using var firstBitmap = Draw(list);
        var row0Rect = list.GetItemRect(0);
        Assert.Equal(SystemColors.Highlight.ToArgb(), firstBitmap.GetPixel(row0Rect.X + 2, row0Rect.Y + row0Rect.Height / 2).ToArgb());

        list.SelectedIndices.Clear();
        list.SelectedIndices.Add(1500);
        list.EnsureVisible(1500);

        using var secondBitmap = Draw(list);
        var row1500Rect = list.GetItemRect(1500);
        Assert.InRange(row1500Rect.Y, 0, list.Height - 1);
        Assert.Equal(SystemColors.Highlight.ToArgb(), secondBitmap.GetPixel(row1500Rect.X + 2, row1500Rect.Y + row1500Rect.Height / 2).ToArgb());
    }

    [Fact]
    public void A_100_000_row_VirtualMode_list_needs_and_scrolls_with_a_vertical_scroll_bar()
    {
        var list = MakeVirtualList(100_000);

        // Content height (VirtualListSize x row height) massively exceeds the 150px client area:
        // without the fix NeedsVerticalScrollBar reads the real (empty) Items collection and finds
        // nothing to scroll.
        list.EnsureVisible(99_999);
        var lastRowRect = list.GetItemRect(99_999);

        Assert.True(lastRowRect.Y < list.Height);
    }

    [AvaloniaFact]
    public void Clicking_a_row_selects_that_index()
    {
        var list = MakeVirtualList(100_000);
        var form = new Form { ClientSize = new Size(300, 200) };
        form.Controls.Add(list);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        var rowRect = list.GetItemRect(3);
        var point = new Avalonia.Point(list.Left + rowRect.X + 5, list.Top + rowRect.Y + rowRect.Height / 2);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);

        Assert.True(list.SelectedIndices.Contains(3));
        form.Close();
    }

    [Fact]
    public void Painting_the_visible_rows_of_a_100_000_row_list_asks_RetrieveVirtualItem_for_fewer_than_50_of_them()
    {
        var list = MakeVirtualList(100_000);
        var retrieveCount = 0;
        list.RetrieveVirtualItem += (_, e) => retrieveCount++;

        using var bitmap = Draw(list);

        Assert.True(retrieveCount > 0);
        Assert.True(retrieveCount < 50, $"Expected fewer than 50 RetrieveVirtualItem calls for one paint, got {retrieveCount}.");
    }

    [Fact]
    public void CacheVirtualItems_fires_once_for_the_first_paint_and_again_only_once_the_visible_range_changes()
    {
        var list = MakeVirtualList(100_000);
        var calls = 0;
        list.CacheVirtualItems += (_, _) => calls++;

        using var firstPaint = Draw(list);
        Assert.Equal(1, calls);

        // Same visible range: repainting must not re-raise it.
        using var secondPaint = Draw(list);
        Assert.Equal(1, calls);

        // Scrolling far enough to see different rows changes the visible range, and must raise it again.
        list.EnsureVisible(5000);
        using var thirdPaint = Draw(list);
        Assert.Equal(2, calls);
    }

    // --- Keyboard --------------------------------------------------------------

    private static Form ShowVirtualList(ListView list)
    {
        var form = new Form { ClientSize = new Size(300, 200) };
        form.Controls.Add(list);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        list.Focus();

        return form;
    }

    private static void Click(Form form, ListView list, int index, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var rowRect = list.GetItemRect(index);
        var point = new Avalonia.Point(list.Left + rowRect.X + 5, list.Top + rowRect.Y + rowRect.Height / 2);
        form.Host!.MouseDown(point, MouseButton.Left, modifiers);
        form.Host!.MouseUp(point, MouseButton.Left, modifiers);
    }

    private static void Press(Form form, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        form.Host!.KeyPressQwerty(key, modifiers);
        form.Host!.KeyReleaseQwerty(key, modifiers);
    }

    [AvaloniaFact]
    public void Down_arrow_moves_selection_to_the_next_row_in_a_100_000_row_VirtualMode_list()
    {
        var list = MakeVirtualList(100_000);
        var form = ShowVirtualList(list);
        Click(form, list, 0);

        Press(form, PhysicalKey.ArrowDown);

        Assert.False(list.SelectedIndices.Contains(0));
        Assert.True(list.SelectedIndices.Contains(1));
        form.Close();
    }

    [AvaloniaFact]
    public void End_moves_selection_to_the_last_row_of_a_100_000_row_VirtualMode_list()
    {
        var list = MakeVirtualList(100_000);
        var form = ShowVirtualList(list);
        Click(form, list, 0);

        Press(form, PhysicalKey.End);

        Assert.True(list.SelectedIndices.Contains(99_999));
        form.Close();
    }

    [AvaloniaFact]
    public void Shift_plus_Down_extends_the_selected_range_from_the_anchor_in_a_100_000_row_VirtualMode_list()
    {
        var list = MakeVirtualList(100_000);
        var form = ShowVirtualList(list);
        Click(form, list, 2);

        Press(form, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Press(form, PhysicalKey.ArrowDown, RawInputModifiers.Shift);

        var selected = list.SelectedIndices.Cast<int>().OrderBy(i => i).ToArray();
        Assert.Equal([2, 3, 4], selected);
        form.Close();
    }

    [AvaloniaFact]
    public void Shift_plus_End_extends_the_selected_range_to_the_last_row_of_a_100_000_row_VirtualMode_list()
    {
        var list = MakeVirtualList(100_000);
        var form = ShowVirtualList(list);
        list.EnsureVisible(99_995);
        Click(form, list, 99_995);

        Press(form, PhysicalKey.End, RawInputModifiers.Shift);

        var selected = list.SelectedIndices.Cast<int>().OrderBy(i => i).ToArray();
        Assert.Equal([99_995, 99_996, 99_997, 99_998, 99_999], selected);
        form.Close();
    }

    // --- VirtualListSize shrinking ----------------------------------------------

    [AvaloniaFact]
    public void Shrinking_VirtualListSize_keeps_a_later_Shift_click_from_selecting_indices_past_the_new_size()
    {
        var list = MakeVirtualList(10);
        var form = ShowVirtualList(list);
        list.EnsureVisible(8);
        Click(form, list, 8);
        Assert.True(list.SelectedIndices.Contains(8));

        // Shrinks past the anchor (8) without any intervening plain click or key press to reset it.
        list.VirtualListSize = 3;

        list.EnsureVisible(0);
        Click(form, list, 0, RawInputModifiers.Shift);

        var selected = list.SelectedIndices.Cast<int>().ToArray();
        Assert.All(selected, index => Assert.InRange(index, 0, 2));
        form.Close();
    }

    [Fact]
    public void Shrinking_VirtualListSize_clamps_the_scroll_position_so_the_viewport_is_not_left_blank()
    {
        var list = MakeVirtualList(100_000);
        list.EnsureVisible(99_999); // scrolls near the old end

        // Without clamping _scrollY, it stays at (roughly) the old content height even though the
        // new content (10 rows) is far shorter: VisibleVirtualRange's first > last, and nothing paints.
        list.VirtualListSize = 10;

        var retrieveCount = 0;
        list.RetrieveVirtualItem += (_, _) => retrieveCount++;
        using var bitmap = Draw(list);

        Assert.True(retrieveCount > 0, "expected the shrunk list to still paint some rows instead of a blank viewport");
        Assert.InRange(list.GetItemRect(9).Y, 0, list.Height - 1);
    }

    // --- Exact-multiple geometry --------------------------------------------------

    [Fact]
    public void VisibleVirtualRange_does_not_include_one_row_past_an_exact_multiple_of_the_row_height()
    {
        var list = MakeVirtualList(100_000, View.List);
        var rowHeight = list.GetItemRect(0).Height;
        const int rows = 5;
        list.Bounds = new Rectangle(0, 0, 200, rowHeight * rows);

        var retrieveCount = 0;
        list.RetrieveVirtualItem += (_, _) => retrieveCount++;
        (int Start, int End)? range = null;
        list.CacheVirtualItems += (_, e) => range = (e.StartIndex, e.EndIndex);

        using var bitmap = Draw(list);

        Assert.Equal((0, rows - 1), range);
        Assert.Equal(rows, retrieveCount);
    }
}
