using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>Hovering a node, and dragging one past the drag threshold.</summary>
public class TreeViewHoverTests
{
    private static Form ShowWith(TreeView tree)
    {
        var form = new Form { ClientSize = new Size(200, 150) };
        form.Controls.Add(tree);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    [AvaloniaFact]
    public void Moving_over_a_node_raises_NodeMouseHover_once()
    {
        var tree = new TreeView();
        var node = tree.Nodes.Add("Alpha");
        TreeNode? hovered = null;
        var hovers = 0;
        tree.NodeMouseHover += (_, e) => { hovered = e.Node; hovers++; };
        var form = ShowWith(tree);

        form.Host!.MouseMove(new Avalonia.Point(10, 5));
        form.Host!.MouseMove(new Avalonia.Point(11, 5));

        Assert.Equal(node, hovered);
        Assert.Equal(1, hovers);

        form.Close();
    }

    [AvaloniaFact]
    public void A_held_button_moved_past_the_drag_threshold_raises_ItemDrag()
    {
        var tree = new TreeView();
        var node = tree.Nodes.Add("Alpha");
        ItemDragEventArgs? dragged = null;
        tree.ItemDrag += (_, e) => dragged = e;
        var form = ShowWith(tree);

        form.Host!.MouseDown(new Avalonia.Point(10, 5), MouseButton.Left);
        // The headless pointer does not remember a button across calls: a drag must repeat it on every move.
        form.Host!.MouseMove(new Avalonia.Point(10, 20), RawInputModifiers.LeftMouseButton);

        Assert.NotNull(dragged);
        Assert.Equal(node, dragged!.Item);
        Assert.Equal(MouseButtons.Left, dragged.Button);

        form.Close();
    }

    [AvaloniaFact]
    public void HotTracking_underlines_only_the_hovered_label()
    {
        var tree = new TreeView { HotTracking = true, Size = new Size(150, 100) };
        tree.Nodes.Add("Alpha");
        var form = ShowWith(tree);

        using var before = new Bitmap(tree.Width, tree.Height);
        tree.DrawToBitmap(before, new Rectangle(0, 0, tree.Width, tree.Height));

        form.Host!.MouseMove(new Avalonia.Point(10, 5));
        Dispatcher.UIThread.RunJobs();

        using var after = new Bitmap(tree.Width, tree.Height);
        tree.DrawToBitmap(after, new Rectangle(0, 0, tree.Width, tree.Height));

        Assert.True(AnyPixelDiffers(before, after), "Expected the hovered row to repaint with an underline.");

        form.Close();
    }

    private static bool AnyPixelDiffers(Bitmap left, Bitmap right)
    {
        for (var y = 0; y < left.Height; y++)
        {
            for (var x = 0; x < left.Width; x++)
            {
                if (left.GetPixel(x, y) != right.GetPixel(x, y)) return true;
            }
        }

        return false;
    }
}
