using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.ToolStrips;

public sealed class MenuRenderingRegressionTests
{
    [Fact]
    public void MenuBarItemsUseCompactPaddingAndPreserveExplicitPadding()
    {
        using var menu = new MenuStrip { Size = new Size(300, 24) };
        var file = new ToolStripMenuItem("File");
        menu.Items.Add(file);
        Assert.Equal(8, file.Padding.Horizontal);
        var barWidth = file.GetPreferredSize(Size.Empty).Width;
        using var popup = new ToolStripDropDownMenu();
        popup.Items.Add(file);
        Assert.True(file.GetPreferredSize(Size.Empty).Width > barWidth);
        file.Padding = new Padding(12);
        menu.Items.Add(file);
        Assert.Equal(new Padding(12), file.Padding);
    }

    [Fact]
    public void SelectedProfessionalMenuLabelHasReadableDarkPixels()
    {
        using var menu = new MenuStrip { Size = new Size(220, 28), RenderMode = ToolStripRenderMode.Professional };
        var file = new ToolStripMenuItem("File") { ForeColor = Color.Black };
        menu.Items.Add(file);
        menu.PerformLayout();
        file.SetSelectedInternal(true);
        using var bitmap = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size));
        var darkPixels = 0;
        for (var y = file.Bounds.Top + 3; y < Math.Min(file.Bounds.Bottom - 3, bitmap.Height); y++)
            for (var x = file.Bounds.Left + 3; x < Math.Min(file.Bounds.Right - 3, bitmap.Width); x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A > 200 && color.R < 100 && color.G < 100 && color.B < 100) darkPixels++;
            }
        Assert.True(darkPixels > 5, "The pale selection must retain visible menu label pixels.");
    }
}
