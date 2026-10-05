using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

public class ListViewStateAndHoverTests
{
    [Theory]
    [InlineData(View.Details)]
    [InlineData(View.List)]
    [InlineData(View.LargeIcon)]
    public void State_image_draws_pixels_in_each_view(View view)
    {
        using var images = StateImages();
        using var list = new ListView
        {
            View = view, BorderStyle = BorderStyle.None, Size = new Size(180, 100), StateImageList = images,
        };
        if (view == View.Details) list.Columns.Add("Name", 140);
        list.Items.Add("Alpha").StateImageIndex = 1;

        using var bitmap = Draw(list);
        var row = list.GetItemRect(0);
        Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(row.X + 8, row.Y + row.Height / 2).ToArgb());
    }

    [Fact]
    public void State_images_replace_checkboxes_and_follow_checked_state()
    {
        using var images = StateImages();
        using var list = new ListView
        {
            View = View.Details, BorderStyle = BorderStyle.None, Size = new Size(180, 100),
            CheckBoxes = true, StateImageList = images,
        };
        list.Columns.Add("Name", 140);
        var item = list.Items.Add("Alpha");
        var row = list.GetItemRect(0);
        var point = new Point(row.X + 8, row.Y + row.Height / 2);

        using var uncheckedBitmap = Draw(list);
        Assert.Equal(Color.Red.ToArgb(), uncheckedBitmap.GetPixel(point.X, point.Y).ToArgb());

        item.Checked = true;
        using var checkedBitmap = Draw(list);
        Assert.Equal(Color.Lime.ToArgb(), checkedBitmap.GetPixel(point.X, point.Y).ToArgb());
    }

    [AvaloniaFact]
    public void Hover_dwell_raises_item_event_selects_and_opens_its_tooltip()
    {
        var list = new ListView
        {
            View = View.List, BorderStyle = BorderStyle.None, Size = new Size(180, 80), HoverSelection = true,
        };
        var item = list.Items.Add("Alpha");
        item.ToolTipText = "Item details";
        var form = Show(list);
        var hovered = 0;
        var popup = 0;
        list.ItemMouseHover += (_, e) => { Assert.Same(item, e.Item); hovered++; };
        list.ItemToolTip.ShowAlways = true;
        list.ItemToolTip.Popup += (_, _) => popup++;

        var row = list.GetItemRect(0);
        form.Host!.MouseMove(new Avalonia.Point(row.X + 30, row.Y + 8));
        Assert.False(item.Selected);
        MouseHoverTracker.Elapse();

        Assert.Equal(1, hovered);
        Assert.True(item.Selected);
        Assert.Equal(1, popup);

        list.ShowItemToolTips = false;
        list.RaiseMouseHover();
        Assert.Equal(1, popup);
        form.Close();
    }

    [AvaloniaFact]
    public void Hot_tracking_repaints_hovered_label()
    {
        var list = new ListView { View = View.List, BorderStyle = BorderStyle.None, Size = new Size(180, 80), HotTracking = true };
        list.Items.Add("Alpha");
        var form = Show(list);
        using var before = Draw(list);
        var row = list.GetItemRect(0);
        form.Host!.MouseMove(new Avalonia.Point(row.X + 30, row.Y + 8));
        Dispatcher.UIThread.RunJobs();
        using var after = Draw(list);

        var changed = false;
        for (var y = row.Y; y < row.Bottom && !changed; y++)
            for (var x = row.X; x < row.Right; x++)
                if (before.GetPixel(x, y) != after.GetPixel(x, y)) { changed = true; break; }
        Assert.True(changed, "The hovered item's label should gain an underline.");
        form.Close();
    }

    private static ImageList StateImages()
    {
        var images = new ImageList { ImageSize = new Size(16, 16) };
        using var red = new Bitmap(16, 16);
        using var green = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(red)) graphics.Clear(Color.Red);
        using (var graphics = Graphics.FromImage(green)) graphics.Clear(Color.Lime);
        images.Images.Add(red);
        images.Images.Add(green);
        return images;
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));
        return bitmap;
    }

    private static Form Show(ListView list)
    {
        var form = new Form { ClientSize = new Size(200, 100) };
        form.Controls.Add(list);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        return form;
    }
}
