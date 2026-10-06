using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Basalt.Tests;

/// <summary>
/// Visual Studio's property grid shows a small preview of an Image, Bitmap or Icon
/// property's current value beside the value text. These prove the Properties
/// grid row does too, by rendering the row and reading the pixels of the preview.
/// </summary>
public sealed class WinFormsPropertyGridImageThumbnailTests
{
    [AvaloniaFact]
    public void AnImagePropertyShowsA16By16PreviewOfItsCurrentValue()
    {
        using var pictureBox = new Forms.PictureBox();
        using var bitmap = new Drawing.Bitmap(40, 40);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(Drawing.Color.Blue);
        }
        pictureBox.Image = bitmap;

        var panel = new WinFormsPropertyPanel();
        panel.Show(pictureBox);
        var window = new Window { Content = panel, Width = 400, Height = 400 };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var row = FindImageRow(panel);
            row.BringIntoView();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var thumbnail = Assert.Single(row.GetVisualDescendants().OfType<Image>(),
                image => image.Name == "PropertyGridThumbnail");

            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var point = thumbnail.TranslatePoint(new Point(8, 8), window)!.Value;
            var pixel = ReadPixel(window, point);
            Assert.True(pixel.Blue > 150 && pixel.Red < 80 && pixel.Green < 80,
                $"The Image row should preview the blue bitmap at {point}; RGB={pixel.Red},{pixel.Green},{pixel.Blue}.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The preview is scaled down before it is encoded: the grid is rebuilt after
    /// every edit, and carrying a photo at full size into each rebuild would make
    /// unrelated edits wait for it.
    /// </summary>
    [AvaloniaFact]
    public void ALargeImageIsPreviewedFromAScaledDownCopy()
    {
        using var pictureBox = new Forms.PictureBox();
        using var bitmap = new Drawing.Bitmap(1000, 400);
        pictureBox.Image = bitmap;

        var panel = new WinFormsPropertyPanel();
        panel.Show(pictureBox);
        var window = new Window { Content = panel, Width = 400, Height = 400 };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var row = FindImageRow(panel);
            row.BringIntoView();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var thumbnail = Assert.Single(row.GetVisualDescendants().OfType<Image>(),
                image => image.Name == "PropertyGridThumbnail");
            var size = Assert.IsAssignableFrom<Bitmap>(thumbnail.Source).PixelSize;

            // Aspect kept: 1000x400 fits 16 wide as 16x6.
            Assert.Equal(new PixelSize(16, 6), size);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AnUnsetImagePropertyShowsNoThumbnail()
    {
        using var pictureBox = new Forms.PictureBox();

        var panel = new WinFormsPropertyPanel();
        panel.Show(pictureBox);
        var window = new Window { Content = panel, Width = 400, Height = 400 };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(FindImageRow(panel).GetVisualDescendants().OfType<Image>(),
                image => image.Name == "PropertyGridThumbnail");
        }
        finally
        {
            window.Close();
        }
    }

    private static Border FindImageRow(WinFormsPropertyPanel panel) =>
        Assert.Single(panel.GetVisualDescendants().OfType<Border>(), row => row.Name == "PropertyGridRow"
            && row.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "Image"));

    private static (byte Red, byte Green, byte Blue) ReadPixel(Window window, Point point)
    {
        var width = (int)window.Width;
        var height = (int)window.Height;
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(window);
        var pixels = new byte[width * height * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), pin.AddrOfPinnedObject(), pixels.Length, width * 4);
        }
        finally
        {
            pin.Free();
        }

        var offset = ((int)point.Y * width + (int)point.X) * 4;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2]);
    }
}
