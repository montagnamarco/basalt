using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

/// <summary>ListViewItem's extra constructors, and the Position/IndentCount/StateImageIndex properties and public collection constructors.</summary>
public class ListViewItemConstructionTests
{
    [Fact]
    public void Items_and_imageKey_constructor_sets_text_and_subitems_and_image_key()
    {
        var item = new ListViewItem(["First", "Second"], "key1");

        Assert.Equal("First", item.Text);
        Assert.Equal("Second", item.SubItems[1].Text);
        Assert.Equal("key1", item.ImageKey);
    }

    [Fact]
    public void SubItem_array_constructor_uses_the_given_subitems_instead_of_a_default_empty_one()
    {
        var item = new ListViewItem();
        var subItems = new[] { new ListViewItem.ListViewSubItem(null, "A"), new ListViewItem.ListViewSubItem(null, "B") };

        var built = new ListViewItem(subItems, 3);

        Assert.Equal(2, built.SubItems.Count);
        Assert.Equal("A", built.Text);
        Assert.Equal(3, built.ImageIndex);
    }

    [Fact]
    public void Items_imageIndex_colors_and_font_constructor_applies_the_style_to_every_subitem()
    {
        var font = new Font("Microsoft Sans Serif", 9f);

        var item = new ListViewItem(["A", "B"], 1, Color.Red, Color.Blue, font);

        Assert.Equal(Color.Red, item.SubItems[0].ForeColor);
        Assert.Equal(Color.Red, item.SubItems[1].ForeColor);
        Assert.Equal(Color.Blue, item.SubItems[1].BackColor);
        Assert.Equal(font, item.SubItems[1].Font);
    }

    [Fact]
    public void Text_imageIndex_group_constructor_sets_all_three()
    {
        var group = new ListViewGroup("Header");

        var item = new ListViewItem("Name", 2, group);

        Assert.Equal("Name", item.Text);
        Assert.Equal(2, item.ImageIndex);
        Assert.Same(group, item.Group);
    }

    [Fact]
    public void Position_and_IndentCount_and_StateImageIndex_are_settable_and_readable()
    {
        var item = new ListViewItem { Position = new Point(5, 9), IndentCount = 2, StateImageIndex = 1 };

        Assert.Equal(new Point(5, 9), item.Position);
        Assert.Equal(2, item.IndentCount);
        Assert.Equal(1, item.StateImageIndex);
    }

    [Fact]
    public void The_items_and_selection_collection_constructors_are_public()
    {
        var list = new ListView();

        var items = new ListView.ListViewItemCollection(list);
        var selected = new ListView.SelectedIndexCollection(list);

        Assert.NotNull(items);
        Assert.NotNull(selected);
    }
}
