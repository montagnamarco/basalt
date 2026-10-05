using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

public class ColumnHeaderSizingTests
{
    [Fact]
    public void AutoResize_uses_header_or_subitem_content()
    {
        var list = new ListView { View = View.Details, Font = new Font("Microsoft Sans Serif", 8.25f) };
        var column = list.Columns.Add("A", 1);
        list.Items.Add(new ListViewItem("A much longer item value"));

        column.AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
        var headerWidth = column.Width;
        column.AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);

        Assert.True(headerWidth > 1);
        Assert.True(column.Width > headerWidth);
    }

    [Fact]
    public void Header_image_selection_changes_header_autosize_and_clone_keeps_selection()
    {
        var list = new ListView { Font = new Font("Microsoft Sans Serif", 8.25f) };
        var images = new ImageList { ImageSize = new Size(24, 16) };
        list.SmallImageList = images;
        var column = new ColumnHeader(0) { Text = "Name" };
        list.Columns.Add(column);

        column.AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
        var widthWithImage = column.Width;
        column.ImageKey = "icon";
        Assert.Equal(-1, column.ImageIndex);
        Assert.Same(images, column.ImageList);
        Assert.Equal("icon", ((ColumnHeader)column.Clone()).ImageKey);

        column.ImageKey = string.Empty;
        column.AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
        Assert.True(widthWithImage > column.Width);
    }

    [Fact]
    public void Header_image_key_draws_the_selected_image()
    {
        using var icon = new Bitmap(8, 8);
        using (var graphics = Graphics.FromImage(icon)) graphics.Clear(Color.Lime);
        using var images = new ImageList { ImageSize = new Size(8, 8) };
        images.Images.Add("status", icon);

        var list = new ListView
        {
            View = View.Details,
            BorderStyle = BorderStyle.None,
            Size = new Size(100, 50),
            SmallImageList = images,
        };
        list.Columns.Add(new ColumnHeader("status") { Text = "State", Width = 80 });

        using var result = new Bitmap(100, 50);
        list.DrawToBitmap(result, new Rectangle(0, 0, 100, 50));

        var foundLimePixel = false;
        for (var y = 0; y < 25; y++)
        {
            for (var x = 3; x < 15; x++)
            {
                var pixel = result.GetPixel(x, y);
                foundLimePixel |= pixel.G > 180 && pixel.R < 100 && pixel.B < 100;
            }
        }

        Assert.True(foundLimePixel);
    }
}
