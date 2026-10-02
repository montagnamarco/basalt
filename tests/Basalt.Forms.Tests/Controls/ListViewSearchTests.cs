using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

/// <summary>FindItemWithText and FindNearestItem, locally over Items and through SearchForVirtualItem while VirtualMode is on.</summary>
public class ListViewSearchTests
{
    [Fact]
    public void FindItemWithText_matches_a_prefix_by_default()
    {
        var list = new ListView { View = View.List };
        list.Items.Add("Alpha");
        var beta = list.Items.Add("Beta");

        var found = list.FindItemWithText("Be");

        Assert.Same(beta, found);
    }

    [Fact]
    public void FindItemWithText_with_isPrefixSearch_false_requires_an_exact_match()
    {
        var list = new ListView { View = View.List };
        list.Items.Add("Beta");

        var found = list.FindItemWithText("Be", includeSubItemsInSearch: false, startIndex: 0, isPrefixSearch: false);

        Assert.Null(found);
    }

    [Fact]
    public void FindItemWithText_searches_subitems_when_asked_to()
    {
        var list = new ListView { View = View.Details };
        list.Columns.Add("A");
        list.Columns.Add("B");
        var item = list.Items.Add("First");
        item.SubItems.Add("Needle");

        Assert.Null(list.FindItemWithText("Needle", includeSubItemsInSearch: false, 0, false));
        Assert.Same(item, list.FindItemWithText("Needle", includeSubItemsInSearch: true, 0, false));
    }

    [Fact]
    public void FindItemWithText_in_VirtualMode_raises_SearchForVirtualItem_instead_of_scanning_Items()
    {
        var list = new ListView { VirtualMode = true, VirtualListSize = 3 };
        list.RetrieveVirtualItem += (_, e) => e.Item = new ListViewItem($"Row {e.ItemIndex}");
        list.SearchForVirtualItem += (_, e) => e.Index = 2;

        var found = list.FindItemWithText("anything");

        Assert.Equal("Row 2", found.Text);
    }

    [Fact]
    public void FindNearestItem_picks_the_closest_item_in_the_requested_direction()
    {
        var list = new ListView { View = View.List, Font = new Font("Microsoft Sans Serif", 8.25f), Bounds = new Rectangle(0, 0, 300, 300) };
        var near = list.Items.Add("Near");
        var far = list.Items.Add("Far");

        var nearRect = list.GetItemRect(0);
        var farRect = list.GetItemRect(1);

        // Searching Down from above both items should land on whichever one is actually closer.
        var reference = new Point(nearRect.X, Math.Min(nearRect.Y, farRect.Y) - 50);
        var expected = nearRect.Y <= farRect.Y ? near : far;

        var found = list.FindNearestItem(SearchDirectionHint.Down, reference);

        Assert.Same(expected, found);
    }
}
