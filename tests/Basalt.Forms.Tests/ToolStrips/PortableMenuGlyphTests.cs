using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Rendering;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.ToolStrips;

public sealed class PortableMenuGlyphTests
{
    [AvaloniaFact]
    public void DisabledMenuCaptionUsesThePlatformDisabledTextPixels()
    {
        foreach (var platform in Enum.GetValues<PortableVisualStylePlatform>())
        {
            using var panel = new Panel { Size = new Size(240, 60) };
            using var menu = new MenuStrip
            {
                VisualStylesOverride = true,
                VisualStylePlatformOverride = platform,
                RenderMode = ToolStripRenderMode.System
            };
            menu.Items.Add(new ToolStripMenuItem("MMMM") { Enabled = false });
            panel.Controls.Add(menu);
            menu.PerformLayout();
            using var bitmap = new Bitmap(menu.Width, menu.Height, PixelFormat.Format32bppArgb);
            menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size));
            Assert.True(Contains(bitmap, PortableVisualStyles.Palette(platform).DisabledText));
        }
    }

    [AvaloniaFact]
    public void SystemRendererPaintsSubmenuArrowsWithDisabledAndExplicitColors()
    {
        using var strip = new ToolStrip
        {
            VisualStylesOverride = true,
            VisualStylePlatformOverride = PortableVisualStylePlatform.MacOS
        };
        var item = new ToolStripMenuItem("Parent") { Enabled = false };
        strip.Items.Add(item);
        using var bitmap = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var renderer = new ToolStripSystemRenderer();
        var bounds = new Rectangle(2, 2, 12, 12);
        graphics.Clear(Color.White);
        renderer.DrawArrow(new ToolStripArrowRenderEventArgs(graphics, item, bounds, Color.Empty, ArrowDirection.Right));
        Assert.True(Contains(bitmap, PortableVisualStyles.Palette(PortableVisualStylePlatform.MacOS).DisabledText));
        graphics.Clear(Color.White);
        renderer.DrawArrow(new ToolStripArrowRenderEventArgs(graphics, item, bounds, Color.Red, ArrowDirection.Right));
        Assert.True(Contains(bitmap, Color.Red));
        strip.VisualStylesOverride = false;
        graphics.Clear(Color.White);
        renderer.DrawArrow(new ToolStripArrowRenderEventArgs(graphics, item, bounds, Color.Empty, ArrowDirection.Right));
        Assert.True(Contains(bitmap, SystemColors.GrayText));
    }

    private static bool Contains(Bitmap bitmap, Color expected)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() == expected.ToArgb())
                {
                    return true;
                }
            }
        }
        return false;
    }
}
