using System.Drawing;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

public class CollectionSerializationTests
{
#pragma warning disable SYSLIB0050 // Direct SerializationInfo calls verify the compatibility contract without BinaryFormatter.
    [Fact]
    public void List_item_round_trip_preserves_subitems_style_and_borrowed_font()
    {
        using var font = new Font("Arial", 11);
        var source = new ListViewItem(["Alpha", "Detail"])
        {
            Name = "alpha", ImageKey = "icon", Checked = true, StateImageIndex = 2,
            ToolTipText = "Info", UseItemStyleForSubItems = false, Font = font,
        };
        source.SubItems[1].Name = "detail";
        source.SubItems[1].BackColor = Color.Red;
        source.SubItems[1].Font = font;
        var info = InfoFor(source);
        ((ISerializable)source).GetObjectData(info, default);

        var result = new SerializableListViewItem(info);

        Assert.Equal("Alpha", result.Text);
        Assert.Equal("alpha", result.Name);
        Assert.Equal("icon", result.ImageKey);
        Assert.True(result.Checked);
        Assert.Equal(2, result.StateImageIndex);
        Assert.Equal("Info", result.ToolTipText);
        Assert.False(result.UseItemStyleForSubItems);
        Assert.Same(font, result.Font);
        Assert.Equal("Detail", result.SubItems[1].Text);
        Assert.Equal("detail", result.SubItems[1].Name);
        Assert.Equal(Color.Red, result.SubItems[1].BackColor);
        Assert.Same(result, result.SubItems[1].OwnerForTest());
        Assert.Same(source, source.SubItems[1].OwnerForTest());
    }

    [Fact]
    public void Tree_node_round_trip_clones_attached_children_and_preserves_font()
    {
        using var font = new Font("Arial", 10);
        var source = new TreeNode("Root")
        {
            Name = "root", Checked = true, ImageKey = "folder", StateImageKey = "state",
            BackColor = Color.Blue, NodeFont = font, Tag = "metadata",
        };
        var child = source.Nodes.Add("Child");
        child.ImageKey = "leaf";
        child.ToolTipText = "tip";
        var info = InfoFor(source);
        ((ISerializable)source).GetObjectData(info, default);

        var result = new SerializableTreeNode(info);

        Assert.Equal("Root", result.Text);
        Assert.Equal("root", result.Name);
        Assert.True(result.Checked);
        Assert.Equal("folder", result.ImageKey);
        Assert.Equal("state", result.StateImageKey);
        Assert.Equal(Color.Blue, result.BackColor);
        Assert.Same(font, result.NodeFont);
        Assert.Equal("metadata", result.Tag);
        Assert.Single(result.Nodes);
        Assert.NotSame(child, result.Nodes[0]);
        Assert.Same(result, result.Nodes[0].Parent);
        Assert.Equal("leaf", result.Nodes[0].ImageKey);
        Assert.Equal("tip", result.Nodes[0].ToolTipText);
        Assert.Same(source, child.Parent);
    }

    [Fact]
    public void Group_round_trip_preserves_header_footer_and_collapsed_state()
    {
        var source = new ListViewGroup("key", "Header")
        {
            Footer = "Footer", Subtitle = "Subtitle", TaskLink = "Open",
            FooterAlignment = HorizontalAlignment.Right,
            CollapsedState = ListViewGroupCollapsedState.Collapsed,
            TitleImageKey = "group-icon", Tag = "metadata",
        };
        var info = InfoFor(source);
        ((ISerializable)source).GetObjectData(info, default);
        var constructor = typeof(ListViewGroup).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(SerializationInfo), typeof(StreamingContext)], null)!;

        var result = (ListViewGroup)constructor.Invoke([info, default(StreamingContext)]);

        Assert.Equal("key", result.Name);
        Assert.Equal("Header", result.Header);
        Assert.Equal("Footer", result.Footer);
        Assert.Equal("Subtitle", result.Subtitle);
        Assert.Equal("Open", result.TaskLink);
        Assert.Equal(HorizontalAlignment.Right, result.FooterAlignment);
        Assert.Equal(ListViewGroupCollapsedState.Collapsed, result.CollapsedState);
        Assert.Equal("group-icon", result.TitleImageKey);
        Assert.Equal("metadata", result.Tag);
    }

    private static SerializationInfo InfoFor(object value) => new(value.GetType(), new FormatterConverter());
#pragma warning restore SYSLIB0050

    private sealed class SerializableListViewItem(SerializationInfo info) : ListViewItem(info, default);

    private sealed class SerializableTreeNode(SerializationInfo info) : TreeNode(info, default);
}

internal static class ListViewSubItemTestExtensions
{
    internal static ListViewItem? OwnerForTest(this ListViewItem.ListViewSubItem subItem) =>
        (ListViewItem?)typeof(ListViewItem.ListViewSubItem)
            .GetProperty("Owner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(subItem);
}
