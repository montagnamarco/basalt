using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Rendering;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Controls;

public sealed class TrackBarRoundThumbTests
{
    [AvaloniaFact]
    public void MacAndLinuxThumbsAreCompactCirclesWithMatchingMouseEndpoints()
    {
        foreach (var platform in new[]
            { PortableVisualStylePlatform.MacOS, PortableVisualStylePlatform.Linux })
        {
            var palette = PortableVisualStyles.Palette(platform);
            using var horizontal = CreateTrackBar(platform, Orientation.Horizontal);
            using var vertical = CreateTrackBar(platform, Orientation.Vertical);

            using (var image = Draw(horizontal))
            {
                var untouched = image.GetPixel(40, 4).ToArgb();
                Assert.Equal(palette.Field.ToArgb(), image.GetPixel(80, 22).ToArgb());
                Assert.NotEqual(untouched, image.GetPixel(73, 22).ToArgb());
                Assert.Equal(untouched, image.GetPixel(72, 14).ToArgb());
                Assert.Equal(untouched, image.GetPixel(80, 4).ToArgb());
            }

            using (var image = Draw(vertical))
            {
                var untouched = image.GetPixel(4, 40).ToArgb();
                Assert.Equal(palette.Field.ToArgb(), image.GetPixel(22, 80).ToArgb());
                Assert.NotEqual(untouched, image.GetPixel(22, 73).ToArgb());
                Assert.Equal(untouched, image.GetPixel(14, 72).ToArgb());
                Assert.Equal(untouched, image.GetPixel(4, 80).ToArgb());
            }

            Click(horizontal, 8, 22);
            Assert.Equal(horizontal.Minimum, horizontal.Value);
            Click(horizontal, 152, 22);
            Assert.Equal(horizontal.Maximum, horizontal.Value);

            Click(vertical, 22, 8);
            Assert.Equal(vertical.Maximum, vertical.Value);
            Click(vertical, 22, 152);
            Assert.Equal(vertical.Minimum, vertical.Value);
        }
    }

    private static TrackBar CreateTrackBar(PortableVisualStylePlatform platform,
        Orientation orientation) => new()
        {
            AutoSize = false,
            Orientation = orientation,
            Size = orientation == Orientation.Horizontal ? new Size(160, 45) : new Size(45, 160),
            TickStyle = TickStyle.None,
            Value = 5,
            VisualStylesOverride = true,
            VisualStylePlatformOverride = platform
        };

    private static void Click(TrackBar trackBar, int x, int y)
    {
        var argument = new MouseEventArgs(MouseButtons.Left, 1, x, y, 0);
        trackBar.DefaultMouseDown(argument);
        trackBar.DefaultMouseUp(argument);
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        return bitmap;
    }
}
