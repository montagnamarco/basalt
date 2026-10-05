using System.Drawing;
using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace Basalt.Drawing.Tests;

[Collection(PrinterDiscoveryCollection.Name)]
public class WindowsPrinterSpoolerTests
{
    [Fact]
    public void One_hundred_print_units_rasterize_to_one_inch_at_300_dpi()
    {
        using var bitmap = new Bitmap(330, 330);
        using (var graphics = WindowsPrinterSpooler.CreateRasterGraphics(bitmap))
            graphics.FillRectangle(Brushes.Black, 0, 0, 100, 100);

        Assert.Equal(Color.Black.ToArgb(), bitmap.GetPixel(299, 299).ToArgb());
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(300, 300).ToArgb());
    }

    [Fact]
    public void Page_mode_sets_orientation_paper_copies_duplex_and_collation()
    {
        var mode = Marshal.AllocHGlobal(220);
        try
        {
            Marshal.WriteInt16(mode, WindowsPrinterSpooler.DmSizeOffset, 220);
            Marshal.WriteInt32(mode, WindowsPrinterSpooler.DmFieldsOffset, 0);

            var settings = new PrinterSettings { Copies = 2, Duplex = Duplex.Vertical, Collate = true };
            var page = new PageSettings(settings) { Landscape = true, Color = false };
            WindowsPrinterSpooler.ApplyPageMode(mode, page, settings);

            Assert.Equal(2, Marshal.ReadInt16(mode, WindowsPrinterSpooler.DmOrientationOffset));
            Assert.Equal((short)PaperKind.Letter, Marshal.ReadInt16(mode, WindowsPrinterSpooler.DmPaperSizeOffset));
            Assert.Equal(2, Marshal.ReadInt16(mode, WindowsPrinterSpooler.DmCopiesOffset));
            Assert.Equal(1, Marshal.ReadInt16(mode, WindowsPrinterSpooler.DmColorOffset));
            Assert.Equal((short)Duplex.Vertical, Marshal.ReadInt16(mode, WindowsPrinterSpooler.DmDuplexOffset));
            Assert.Equal(1, Marshal.ReadInt16(mode, WindowsPrinterSpooler.DmCollateOffset));
            Assert.Equal(0x9903, Marshal.ReadInt32(mode, WindowsPrinterSpooler.DmFieldsOffset) & 0x9903);
        }
        finally
        {
            Marshal.FreeHGlobal(mode);
        }
    }

    [Fact]
    public void Custom_paper_uses_tenths_of_a_millimeter()
    {
        var mode = Marshal.AllocHGlobal(220);
        try
        {
            Marshal.WriteInt16(mode, WindowsPrinterSpooler.DmSizeOffset, 220);
            Marshal.WriteInt32(mode, WindowsPrinterSpooler.DmFieldsOffset, 0);

            var settings = new PrinterSettings();
            var page = new PageSettings(settings)
            {
                PaperSize = new PaperSize("Custom", 400, 600),
            };
            WindowsPrinterSpooler.ApplyPageMode(mode, page, settings);

            Assert.Equal(1016, Marshal.ReadInt16(mode, WindowsPrinterSpooler.DmPaperWidthOffset));
            Assert.Equal(1524, Marshal.ReadInt16(mode, WindowsPrinterSpooler.DmPaperLengthOffset));
            Assert.Equal(0x0C, Marshal.ReadInt32(mode, WindowsPrinterSpooler.DmFieldsOffset) & 0x0E);
        }
        finally
        {
            Marshal.FreeHGlobal(mode);
        }
    }

    [Fact]
    public void Windows_raster_page_scales_hundredths_of_an_inch_to_300_dpi()
    {
        using var document = new PrintDocument();
        var portrait = document.DefaultPageSettings.Bounds;

        Assert.Equal(2550, WindowsPrinterSpooler.PixelsAtDpi(portrait.Width, 300));
        Assert.Equal(3300, WindowsPrinterSpooler.PixelsAtDpi(portrait.Height, 300));

        document.DefaultPageSettings.Landscape = true;
        var landscape = document.DefaultPageSettings.Bounds;
        Assert.Equal(3300, WindowsPrinterSpooler.PixelsAtDpi(landscape.Width, 300));
        Assert.Equal(2550, WindowsPrinterSpooler.PixelsAtDpi(landscape.Height, 300));
    }
}
