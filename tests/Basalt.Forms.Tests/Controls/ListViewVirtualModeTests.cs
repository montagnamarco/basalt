using System.Linq;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

/// <summary>VirtualMode: VirtualListSize, Items reading through RetrieveVirtualItem, and index-based selection.</summary>
public class ListViewVirtualModeTests
{
    private static ListView MakeVirtualList(int size)
    {
        var list = new ListView { VirtualMode = true, VirtualListSize = size };
        list.RetrieveVirtualItem += (_, e) => e.Item = new ListViewItem($"Row {e.ItemIndex}");

        return list;
    }

    [Fact]
    public void Items_Count_follows_VirtualListSize_not_the_real_collection()
    {
        var list = MakeVirtualList(5);

        Assert.Equal(5, list.Items.Count);
    }

    [Fact]
    public void Items_indexer_reads_through_RetrieveVirtualItem()
    {
        var list = MakeVirtualList(3);

        Assert.Equal("Row 2", list.Items[2].Text);
    }

    [Fact]
    public void Adding_an_item_directly_is_rejected_while_VirtualMode_is_on()
    {
        var list = MakeVirtualList(3);

        Assert.Throws<InvalidOperationException>(() => list.Items.Add("Nope"));
    }

    [Fact]
    public void Enumerating_Items_yields_one_retrieved_item_per_index()
    {
        var list = MakeVirtualList(3);

        var texts = list.Items.Cast<ListViewItem>().Select(i => i.Text).ToArray();

        Assert.Equal(["Row 0", "Row 1", "Row 2"], texts);
    }

    [Fact]
    public void SelectedIndices_Add_selects_by_index_and_raises_VirtualItemsSelectionRangeChanged()
    {
        var list = MakeVirtualList(10);
        var raisedFor = -1;
        list.VirtualItemsSelectionRangeChanged += (_, e) => raisedFor = e.StartIndex;

        list.SelectedIndices.Add(4);

        Assert.Equal(4, raisedFor);
        Assert.True(list.SelectedIndices.Contains(4));
        Assert.Single(list.SelectedIndices.Cast<int>());
    }

    [Fact]
    public void SelectedIndices_Remove_deselects_and_stops_reporting_it_selected()
    {
        var list = MakeVirtualList(10);
        list.SelectedIndices.Add(4);

        list.SelectedIndices.Remove(4);

        Assert.False(list.SelectedIndices.Contains(4));
        Assert.Empty(list.SelectedIndices.Cast<int>());
    }

    [Fact]
    public void RetrieveVirtualItem_without_setting_Item_throws_rather_than_silently_returning_null()
    {
        var list = new ListView { VirtualMode = true, VirtualListSize = 1 };

        Assert.Throws<InvalidOperationException>(() => list.Items[0]);
    }

    [Fact]
    public void Shrinking_VirtualListSize_drops_selections_past_the_new_size()
    {
        var list = MakeVirtualList(10);
        list.SelectedIndices.Add(8);

        list.VirtualListSize = 5;

        Assert.False(list.SelectedIndices.Contains(8));
    }
}
