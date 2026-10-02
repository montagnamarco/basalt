using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Controls;

/// <summary>ListView.OwnerDraw: DrawItem, DrawSubItem and DrawColumnHeader, with DrawDefault falling back to normal drawing.</summary>
public class ListViewOwnerDrawTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    [Fact]
    public void OwnerDraw_off_never_raises_DrawItem()
    {
        var list = new ListView { Font = Font, BorderStyle = BorderStyle.None, Bounds = new Rectangle(0, 0, 150, 100), View = View.List };
        list.Items.Add("Alpha");
        var raised = false;
        list.DrawItem += (_, e) => raised = true;

        using var bitmap = Draw(list);

        Assert.False(raised);
    }

    [Fact]
    public void DrawItem_handler_replaces_the_items_pixels_when_DrawDefault_is_left_false()
    {
        var list = new ListView
        {
            Font = Font, BorderStyle = BorderStyle.None, OwnerDraw = true, View = View.List, Bounds = new Rectangle(0, 0, 150, 100),
        };
        list.Items.Add("Alpha");
        list.DrawItem += (_, e) =>
        {
            using var brush = new SolidBrush(Color.Lime);
            e.Graphics.FillRectangle(brush, e.Bounds);
        };

        using var bitmap = Draw(list);

        Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(2, 2).ToArgb());
    }

    [Fact]
    public void DrawItem_setting_DrawDefault_true_falls_back_to_normal_drawing()
    {
        var withoutOwnerDraw = new ListView { Font = Font, BorderStyle = BorderStyle.None, View = View.List, Bounds = new Rectangle(0, 0, 150, 100) };
        withoutOwnerDraw.Items.Add("Alpha");
        using var expected = Draw(withoutOwnerDraw);

        var ownerDrawn = new ListView
        {
            Font = Font, BorderStyle = BorderStyle.None, OwnerDraw = true, View = View.List, Bounds = new Rectangle(0, 0, 150, 100),
        };
        ownerDrawn.Items.Add("Alpha");
        var raised = false;
        ownerDrawn.DrawItem += (_, e) =>
        {
            raised = true;
            e.DrawDefault = true; // Asks ListView to draw the item itself, despite OwnerDraw being on.
        };
        using var actual = Draw(ownerDrawn);

        Assert.True(raised);
        Assert.Equal(expected.GetPixel(2, 2).ToArgb(), actual.GetPixel(2, 2).ToArgb());
    }

    [Fact]
    public void DrawColumnHeader_handler_replaces_the_headers_pixels_in_Details_view()
    {
        var list = new ListView
        {
            Font = Font, BorderStyle = BorderStyle.None, OwnerDraw = true, View = View.Details, Bounds = new Rectangle(0, 0, 150, 100),
        };
        list.Columns.Add("Name", 80);
        list.DrawColumnHeader += (_, e) =>
        {
            using var brush = new SolidBrush(Color.Lime);
            e.Graphics.FillRectangle(brush, e.Bounds);
        };

        using var bitmap = Draw(list);

        Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(2, 2).ToArgb());
    }

    [Fact]
    public void DrawSubItem_is_raised_per_column_once_DrawItem_leaves_DrawDefault_false()
    {
        var list = new ListView
        {
            Font = Font, BorderStyle = BorderStyle.None, OwnerDraw = true, View = View.Details, Bounds = new Rectangle(0, 0, 150, 100),
        };
        list.Columns.Add("A", 60);
        list.Columns.Add("B", 60);
        var item = list.Items.Add("Row");
        item.SubItems.Add("Second");
        list.DrawItem += (_, e) => { }; // DrawDefault left false: item itself is considered handled.
        var seenColumns = new List<int>();
        list.DrawSubItem += (_, e) => seenColumns.Add(e.ColumnIndex);

        Draw(list).Dispose();

        Assert.Equal([0, 1], seenColumns);
    }
}
