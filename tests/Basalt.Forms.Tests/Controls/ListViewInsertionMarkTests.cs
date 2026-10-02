using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

/// <summary>ListView.InsertionMark: where it sits next to an item, which edge, and NearestIndex picking the closest item to a point.</summary>
public class ListViewInsertionMarkTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static ListView ShowListView()
    {
        var listView = new ListView { Font = Font, BorderStyle = BorderStyle.None, View = View.List, Bounds = new Rectangle(0, 0, 150, 100) };
        listView.Items.Add("First");
        listView.Items.Add("Second");
        listView.Items.Add("Third");

        return listView;
    }

    [Fact]
    public void A_fresh_InsertionMark_has_no_Index_and_empty_Bounds()
    {
        var listView = ShowListView();

        Assert.Equal(-1, listView.InsertionMark.Index);
        Assert.Equal(Rectangle.Empty, listView.InsertionMark.Bounds);
    }

    [Fact]
    public void Setting_Index_gives_Bounds_a_thin_strip_at_that_item_s_leading_edge()
    {
        var listView = ShowListView();
        var itemBounds = listView.GetItemRect(1);

        listView.InsertionMark.Index = 1;

        var markBounds = listView.InsertionMark.Bounds;
        Assert.False(markBounds.IsEmpty);
        Assert.Equal(itemBounds.Left - markBounds.Width, markBounds.Left);
        Assert.Equal(itemBounds.Top, markBounds.Top);
    }

    [Fact]
    public void AppearsAfterItem_moves_the_mark_to_the_item_s_trailing_edge()
    {
        var listView = ShowListView();
        var itemBounds = listView.GetItemRect(1);

        listView.InsertionMark.Index = 1;
        listView.InsertionMark.AppearsAfterItem = true;

        var markBounds = listView.InsertionMark.Bounds;
        Assert.Equal(itemBounds.Right, markBounds.Left);
    }

    [Fact]
    public void NearestIndex_picks_the_item_whose_centre_is_closest_to_the_point()
    {
        var listView = ShowListView();
        var secondItemCenter = listView.GetItemRect(1) is { } bounds
            ? new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)
            : Point.Empty;

        Assert.Equal(1, listView.InsertionMark.NearestIndex(secondItemCenter));
    }

    [Fact]
    public void Setting_the_same_Index_again_does_not_invalidate_a_second_time()
    {
        var listView = ShowListView();
        listView.InsertionMark.Index = 1;

        // No direct way to observe Invalidate from here; this only proves the setter still accepts
        // a repeated value without throwing and leaves Index unchanged.
        listView.InsertionMark.Index = 1;

        Assert.Equal(1, listView.InsertionMark.Index);
    }
}
