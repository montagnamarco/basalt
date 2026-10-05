using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

public class ListViewColumnCollectionCompatibilityTests
{
    [Fact]
    public void Image_key_add_overload_draws_the_named_header_image()
    {
        using var icon = new Bitmap(8, 8);
        using (var graphics = Graphics.FromImage(icon)) graphics.Clear(Color.Lime);
        using var images = new ImageList { ImageSize = new Size(8, 8) };
        images.Images.Add("status", icon);
        using var list = new ListView
        {
            View = View.Details,
            BorderStyle = BorderStyle.None,
            Size = new Size(100, 50),
            SmallImageList = images,
        };
        var column = list.Columns.Add("state", "State", 80, HorizontalAlignment.Left, "status");

        using var result = new Bitmap(100, 50);
        list.DrawToBitmap(result, new Rectangle(0, 0, 100, 50));

        Assert.Same(list, column.ListView);
        Assert.Same(column, list.Columns["state"]);
        Assert.Equal("status", column.ImageKey);

        var foundImagePixel = false;
        for (var y = 0; y < 25; y++)
        {
            for (var x = 3; x < 15; x++)
            {
                var pixel = result.GetPixel(x, y);
                foundImagePixel |= pixel.G > 180 && pixel.R < 100 && pixel.B < 100;
            }
        }

        Assert.True(foundImagePixel);
    }

    [Fact]
    public void Keyed_insert_overloads_keep_order_and_image_selection()
    {
        using var list = new ListView();
        list.Columns.Add("tail", "Tail");

        list.Columns.Insert(0, "first", "First");
        list.Columns.Insert(1, "second", "Second", 42);
        list.Columns.Insert(2, "index", "By index", 51, HorizontalAlignment.Center, 2);
        list.Columns.Insert(3, "key", "By key", 63, HorizontalAlignment.Right, "glyph");
        var addedByIndex = list.Columns.Add("last", "Last", 71, HorizontalAlignment.Left, 3);

        Assert.Equal(new[] { "first", "second", "index", "key", "tail", "last" },
            list.Columns.Cast<ColumnHeader>().Select(column => column.Name));
        Assert.All(list.Columns.Cast<ColumnHeader>(), column => Assert.Same(list, column.ListView));
        Assert.Equal(42, list.Columns["second"]!.Width);
        Assert.Equal(2, list.Columns["index"]!.ImageIndex);
        Assert.Equal(HorizontalAlignment.Center, list.Columns["index"]!.TextAlign);
        Assert.Equal("glyph", list.Columns["key"]!.ImageKey);
        Assert.Equal(HorizontalAlignment.Right, list.Columns["key"]!.TextAlign);
        Assert.Equal(3, addedByIndex.ImageIndex);
    }
}
