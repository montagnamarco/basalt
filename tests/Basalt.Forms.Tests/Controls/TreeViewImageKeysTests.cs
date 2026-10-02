using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

/// <summary>Resolving a node's picture, selected picture and state picture by string key.</summary>
public class TreeViewImageKeysTests
{
    private static ImageList MakeList(params string[] keys)
    {
        var list = new ImageList();
        foreach (var key in keys) list.Images.Add(key, new Bitmap(16, 16));

        return list;
    }

    [Fact]
    public void A_nodes_ImageKey_resolves_through_the_tree_views_ImageList()
    {
        var tree = new TreeView { ImageList = MakeList("folder", "file") };
        var node = tree.Nodes.Add("Alpha");
        node.ImageKey = "file";

        Assert.Equal(1, node.ResolveImageIndex(tree.ImageList));
    }

    [Fact]
    public void A_missing_key_resolves_to_minus_one_even_with_an_ImageIndex_set()
    {
        var tree = new TreeView { ImageList = MakeList("folder") };
        var node = tree.Nodes.Add("Alpha");
        node.ImageIndex = 0;
        node.ImageKey = "missing";

        Assert.Equal(-1, node.ResolveImageIndex(tree.ImageList));
    }

    [Fact]
    public void StateImageKey_resolves_through_the_tree_views_StateImageList()
    {
        var tree = new TreeView { StateImageList = MakeList("unchecked", "checked") };
        var node = tree.Nodes.Add("Alpha");
        node.StateImageKey = "checked";

        Assert.Equal(1, node.ResolveStateImageIndex(tree.StateImageList));
    }

    [Fact]
    public void Add_with_a_key_and_an_image_key_sets_both_on_the_new_node()
    {
        var tree = new TreeView();
        var node = tree.Nodes.Add("alpha", "Alpha", "file");

        Assert.Equal("alpha", node.Name);
        Assert.Equal("file", node.ImageKey);
    }

    [Fact]
    public void Add_with_a_key_an_image_key_and_a_selected_image_key_sets_all_three()
    {
        var tree = new TreeView();
        var node = tree.Nodes.Add("alpha", "Alpha", "file", "fileOpen");

        Assert.Equal("file", node.ImageKey);
        Assert.Equal("fileOpen", node.SelectedImageKey);
    }

    [Fact]
    public void Insert_with_an_image_key_places_the_node_at_the_given_index()
    {
        var tree = new TreeView();
        tree.Nodes.Add("Zed");
        var inserted = tree.Nodes.Insert(0, "alpha", "Alpha", "file");

        Assert.Equal(0, inserted.Index);
        Assert.Equal("file", inserted.ImageKey);
    }

    [Fact]
    public void NodeFont_overrides_the_tree_views_font_for_that_node_only()
    {
        var tree = new TreeView { Font = new Font("Microsoft Sans Serif", 8.25f) };
        var plain = tree.Nodes.Add("Alpha");
        var bold = tree.Nodes.Add("Beta");
        bold.NodeFont = new Font(tree.Font, FontStyle.Bold);

        Assert.Null(plain.NodeFont);
        Assert.Equal(FontStyle.Bold, bold.NodeFont!.Style);
    }
}
