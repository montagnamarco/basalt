using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.Printing;

/// <summary>
/// PrintPreviewControl, proved the same way as other painting: a document
/// prints pages filled with known colours, and the control's bitmap is read
/// back pixel by pixel where each page is expected to land.
/// </summary>
public class PrintPreviewControlTests
{
    // A one-inch square page keeps the maths exact: Zoom = 1 draws it at 100
    // screen pixels, the fixed spacing the control leaves around pages.
    private static PrintDocument ThreePageDocument(params Color[] pageColors)
    {
        var document = new PrintDocument();
        document.DefaultPageSettings.PaperSize = new PaperSize("Test", 100, 100);

        var pageIndex = 0;
        document.PrintPage += (_, e) =>
        {
            using var brush = new SolidBrush(pageColors[pageIndex]);
            e.Graphics.FillRectangle(brush, e.PageBounds);

            pageIndex++;
            e.HasMorePages = pageIndex < pageColors.Length;
        };

        return document;
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    [Fact]
    public void A_single_column_shows_the_page_at_StartPage_filling_a_known_rectangle()
    {
        var document = ThreePageDocument(Color.Red, Color.Lime, Color.Blue);
        var control = new PrintPreviewControl
        {
            Document = document,
            AutoZoom = false,
            Zoom = 1.0,
            Columns = 1,
            Rows = 1,
            Size = new Size(120, 120),
        };

        using var bitmap = Draw(control);

        // Page 0 (red) lands at (10, 10, 100, 100): its centre must be red,
        // the grey workspace must show just outside the frame.
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(60, 60).ToArgb());
        Assert.Equal(SystemColors.AppWorkspace.ToArgb(), bitmap.GetPixel(2, 2).ToArgb());

        control.StartPage = 1;
        using var afterAdvance = Draw(control);

        Assert.Equal(Color.Lime.ToArgb(), afterAdvance.GetPixel(60, 60).ToArgb());
    }

    [Fact]
    public void Two_columns_show_two_consecutive_pages_side_by_side()
    {
        var document = ThreePageDocument(Color.Red, Color.Lime, Color.Blue);
        var control = new PrintPreviewControl
        {
            Document = document,
            AutoZoom = false,
            Zoom = 1.0,
            Columns = 2,
            Rows = 1,
            Size = new Size(230, 120),
        };

        using var bitmap = Draw(control);

        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(60, 60).ToArgb());
        Assert.Equal(Color.Lime.ToArgb(), bitmap.GetPixel(170, 60).ToArgb());
    }

    [Fact]
    public void AutoZoom_fits_the_page_to_the_client_area()
    {
        var document = ThreePageDocument(Color.Red, Color.Lime, Color.Blue);
        var control = new PrintPreviewControl
        {
            Document = document,
            AutoZoom = true,
            Columns = 1,
            Rows = 1,
            Size = new Size(500, 500),
        };

        using var bitmap = Draw(control);

        // A square page in a square client area, with AutoZoom, must fill
        // the client area up to the fixed spacing on every side.
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(250, 250).ToArgb());
        Assert.Equal(SystemColors.AppWorkspace.ToArgb(), bitmap.GetPixel(2, 2).ToArgb());
        Assert.Equal(SystemColors.AppWorkspace.ToArgb(), bitmap.GetPixel(497, 497).ToArgb());
    }

    [Fact]
    public void StartPage_clamps_to_the_rendered_page_count()
    {
        var document = ThreePageDocument(Color.Red, Color.Lime, Color.Blue);
        var control = new PrintPreviewControl { Document = document };

        control.StartPage = 50;
        Assert.Equal(2, control.StartPage);

        control.StartPage = -5;
        Assert.Equal(0, control.StartPage);
    }

    [Fact]
    public void StartPageChanged_fires_only_when_the_clamped_value_changes()
    {
        var document = ThreePageDocument(Color.Red, Color.Lime, Color.Blue);
        var control = new PrintPreviewControl { Document = document };
        var raised = 0;
        control.StartPageChanged += (_, _) => raised++;

        control.StartPage = 1;
        Assert.Equal(1, raised);

        // Same value already in effect after clamping: no second event.
        control.StartPage = 1;
        Assert.Equal(1, raised);
    }

    [Fact]
    public void InvalidatePreview_re_renders_the_document_after_a_page_changes()
    {
        var color = Color.Red;
        var document = new PrintDocument();
        document.DefaultPageSettings.PaperSize = new PaperSize("Test", 100, 100);
        document.PrintPage += (_, e) =>
        {
            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, e.PageBounds);
        };

        var control = new PrintPreviewControl
        {
            Document = document,
            AutoZoom = false,
            Zoom = 1.0,
            Size = new Size(120, 120),
        };

        using var before = Draw(control);
        Assert.Equal(Color.Red.ToArgb(), before.GetPixel(60, 60).ToArgb());

        color = Color.Blue;
        control.InvalidatePreview();

        using var after = Draw(control);
        Assert.Equal(Color.Blue.ToArgb(), after.GetPixel(60, 60).ToArgb());
    }

    [Fact]
    public void Setting_Zoom_turns_AutoZoom_off_and_invalid_sizes_are_refused()
    {
        var control = new PrintPreviewControl();
        Assert.True(control.AutoZoom);

        control.Zoom = 1.0;

        Assert.False(control.AutoZoom);
        Assert.Throws<ArgumentOutOfRangeException>(() => control.Zoom = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => control.Columns = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => control.Rows = -1);
    }

    [Fact]
    public void A_document_that_throws_while_printing_shows_a_message_instead_of_failing_the_paint()
    {
        var document = new PrintDocument();
        document.PrintPage += (_, _) => throw new InvalidOperationException("broken page");
        var control = new PrintPreviewControl { Document = document, Size = new Size(300, 200) };

        using var bitmap = Draw(control);

        var background = SystemColors.AppWorkspace.ToArgb();
        var inked = false;
        for (var y = 0; y < bitmap.Height && !inked; y++)
        {
            for (var x = 0; x < bitmap.Width && !inked; x++) inked = bitmap.GetPixel(x, y).ToArgb() != background;
        }

        Assert.True(inked, "the failure message should be drawn on the background");
    }
}
