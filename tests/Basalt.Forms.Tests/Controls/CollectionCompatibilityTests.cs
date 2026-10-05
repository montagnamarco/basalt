using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

public class CollectionCompatibilityTests
{
    [Fact]
    public void Subitem_keys_are_case_insensitive_and_removable()
    {
        var item = new ListViewItem("First");
        item.SubItems.Add(new ListViewItem.ListViewSubItem(item, "Second") { Name = "detail" });

        Assert.True(item.SubItems.ContainsKey("DETAIL"));
        Assert.Equal(1, item.SubItems.IndexOfKey("Detail"));
        Assert.False(item.SubItems.IsReadOnly);

        item.SubItems.RemoveByKey("DETAIL");
        Assert.Single(item.SubItems);
        Assert.False(item.SubItems.ContainsKey("detail"));
    }

    [Fact]
    public void Item_converter_returns_label_and_state_image_choices_fit_valid_range()
    {
        var converter = new ListViewItemConverter();
        Assert.True(converter.CanConvertTo(typeof(string)));
        Assert.Equal("Alpha", converter.ConvertTo(new ListViewItem("Alpha"), typeof(string)));

        var choices = new ListViewItemStateImageIndexConverter().GetStandardValues(null).Cast<int>().ToArray();
        Assert.Equal([-1], choices);
    }

    [Fact]
    public void Collapsing_group_removes_items_from_details_layout_and_raises_event()
    {
        using var list = new ListView { View = View.Details, BorderStyle = BorderStyle.None, Size = new Size(180, 100) };
        list.Columns.Add("Name", 160);
        var group = list.Groups.Add("g", "Group");
        var item = list.Items.Add("Row");
        item.Group = group;
        var events = new List<int>();
        list.GroupCollapsedStateChanged += (_, e) => events.Add(e.GroupIndex);

        Assert.False(item.Bounds.IsEmpty);
        group.CollapsedState = ListViewGroupCollapsedState.Collapsed;

        Assert.Equal([0], events);
        Assert.Equal(Rectangle.Empty, item.Bounds);
        Assert.Null(list.GetItemAt(20, 50));
        group.CollapsedState = ListViewGroupCollapsedState.Expanded;
        Assert.False(item.Bounds.IsEmpty);
    }

    [Fact]
    public void Manual_icon_positions_and_arrange_icons_change_layout()
    {
        using var list = new ListView
        {
            View = View.SmallIcon, BorderStyle = BorderStyle.None, Size = new Size(200, 80), AutoArrange = false,
        };
        var first = list.Items.Add("First");
        var second = list.Items.Add("Second");
        first.Position = new Point(34, 12);
        second.Position = new Point(95, 40);

        Assert.Equal(new Point(34, 12), first.Bounds.Location);
        Assert.Equal(new Point(95, 40), second.Bounds.Location);

        list.ArrangeIcons(ListViewAlignment.Left);
        Assert.Equal(Point.Empty, first.Bounds.Location);
        Assert.Equal(0, second.Bounds.X);
        Assert.True(second.Bounds.Y > first.Bounds.Y);
    }

    [Fact]
    public void Tree_right_to_left_layout_mirrors_image_pixels_and_hit_testing()
    {
        using var image = new Bitmap(10, 10);
        using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.Red);
        using var images = new ImageList { ImageSize = new Size(10, 10) };
        images.Images.Add(image);
        using var tree = new TreeView { BorderStyle = BorderStyle.None, Size = new Size(180, 40), ImageList = images };
        var node = tree.Nodes.Add("Node");
        node.ImageIndex = 0;

        using var left = Draw(tree);
        var leftX = FindRedX(left);
        Assert.InRange(leftX, 0, 70);
        Assert.Same(node, tree.HitTest(leftX, 8).Node);

        tree.RightToLeftLayout = true;
        using var right = Draw(tree);
        var rightX = FindRedX(right);
        Assert.InRange(rightX, 100, 179);
        Assert.Same(node, tree.HitTest(rightX, 8).Node);
        Assert.Equal(TreeViewHitTestLocations.Image, tree.HitTest(rightX, 8).Location);
    }

    [AvaloniaFact]
    public void Group_task_link_and_item_drag_raise_events_from_pointer_input()
    {
        using var list = new ListView { View = View.Details, BorderStyle = BorderStyle.None, Size = new Size(200, 100) };
        list.Columns.Add("Name", 180);
        var group = list.Groups.Add("g", "Group");
        group.TaskLink = "Open";
        var item = list.Items.Add("Row");
        item.Group = group;
        var form = new Form { ClientSize = new Size(230, 130) };
        form.Controls.Add(list);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        var taskClicks = 0;
        var drags = 0;
        list.GroupTaskLinkClick += (_, e) => { Assert.Equal(0, e.GroupIndex); taskClicks++; };
        list.ItemDrag += (_, e) => { Assert.Same(item, e.Item); drags++; };

        var groupPoint = new Avalonia.Point(list.Left + 178, list.Top + 30);
        form.Host!.MouseDown(groupPoint, MouseButton.Left);
        form.Host!.MouseUp(groupPoint, MouseButton.Left);
        Assert.Equal(1, taskClicks);

        var row = item.Bounds;
        var rowPoint = new Avalonia.Point(list.Left + row.X + 30, list.Top + row.Y + 8);
        form.Host!.MouseDown(rowPoint, MouseButton.Left);
        form.Host!.MouseMove(new Avalonia.Point(rowPoint.X + 8, rowPoint.Y));
        form.Host!.MouseUp(new Avalonia.Point(rowPoint.X + 8, rowPoint.Y), MouseButton.Left);
        Assert.Equal(1, drags);
        form.Close();
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));
        return bitmap;
    }

    private static int FindRedX(Bitmap bitmap)
    {
        for (var x = 0; x < bitmap.Width; x++)
            if (bitmap.GetPixel(x, 8).R > 200 && bitmap.GetPixel(x, 8).G < 50) return x;
        throw new Xunit.Sdk.XunitException("No red image pixel was rendered in the tree row.");
    }
}
