using System.ComponentModel.Design.Serialization;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Text;

namespace Basalt.Drawing.Tests;

/// <summary>
/// Printing with no printer: to a PDF file, or to page images for a preview,
/// in GDI+'s printer unit, the hundredth of an inch.
/// </summary>
[Collection(PrinterDiscoveryCollection.Name)]
public class PrintingTests
{
    [Fact]
    public void A_document_printed_to_a_file_becomes_a_PDF_with_its_pages()
    {
        var file = Path.Combine(Path.GetTempPath(), $"basalt-print-{Guid.NewGuid():N}.pdf");

        try
        {
            using var document = new PrintDocument();
            document.PrinterSettings.PrintToFile = true;
            document.PrinterSettings.PrintFileName = file;
            document.PrintController = new StandardPrintController();

            var page = 0;
            document.PrintPage += (_, e) =>
            {
                e.Graphics!.FillRectangle(Brushes.Black, e.MarginBounds);
                e.HasMorePages = ++page < 2;
            };

            document.Print();

            var bytes = File.ReadAllBytes(file);
            var text = Encoding.ASCII.GetString(bytes);

            Assert.StartsWith("%PDF", text);
            Assert.Equal(2, CountPages(text));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_page_handler_that_throws_still_ends_the_job_and_closes_the_file()
    {
        var file = Path.Combine(Path.GetTempPath(), $"basalt-print-{Guid.NewGuid():N}.pdf");

        try
        {
            using var document = new PrintDocument();
            document.PrinterSettings.PrintToFile = true;
            document.PrinterSettings.PrintFileName = file;
            document.PrintController = new StandardPrintController();

            var ended = false;
            document.EndPrint += (_, _) => ended = true;
            document.PrintPage += (_, _) => throw new InvalidOperationException("page failed");

            Assert.Throws<InvalidOperationException>(() => document.Print());

            Assert.True(ended);

            // Closed: it can be opened for writing again.
            using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_controller_that_fails_during_start_receives_the_cleanup_hook()
    {
        using var document = new PrintDocument();
        var controller = new FailingStartController();
        document.PrintController = controller;

        Assert.Throws<InvalidOperationException>(() => document.Print());

        Assert.True(controller.CleanedUp);
        Assert.True(controller.CancelledAtEnd);
    }

    [Fact]
    public void A_pdf_creation_failure_closes_the_output_stream()
    {
        var file = Path.Combine(Path.GetTempPath(), $"basalt-print-{Guid.NewGuid():N}.pdf");
        try
        {
            using var document = new PrintDocument();
            document.PrinterSettings.PrintToFile = true;
            document.PrinterSettings.PrintFileName = file;
            document.PrintController = new StandardPrintController(
                _ => throw new InvalidOperationException("PDF creation failed"),
                static pdf => pdf.Close());

            Assert.Throws<InvalidOperationException>(() => document.Print());
            using var exclusive = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_pdf_close_failure_still_closes_the_output_stream()
    {
        var file = Path.Combine(Path.GetTempPath(), $"basalt-print-{Guid.NewGuid():N}.pdf");
        try
        {
            using var document = new PrintDocument();
            document.PrinterSettings.PrintToFile = true;
            document.PrinterSettings.PrintFileName = file;
            document.PrintController = new StandardPrintController(
                static stream => SkiaSharp.SKDocument.CreatePdf(stream),
                _ => throw new InvalidOperationException("PDF close failed"));
            document.PrintPage += (_, e) => e.Graphics.FillRectangle(Brushes.Black, 0, 0, 10, 10);

            Assert.Throws<InvalidOperationException>(() => document.Print());
            using var exclusive = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private sealed class FailingStartController : PrintController
    {
        public bool CleanedUp { get; private set; }
        public bool CancelledAtEnd { get; private set; }

        public override void OnStartPrint(PrintDocument document, PrintEventArgs e) =>
            throw new InvalidOperationException("Controller startup failed");

        public override void OnEndPrint(PrintDocument document, PrintEventArgs e)
        {
            CleanedUp = true;
            CancelledAtEnd = e.Cancel;
        }
    }

    [Fact]
    public void Measuring_before_printing_gives_the_sizes_of_the_page()
    {
        var settings = new PrinterSettings();
        using var graphics = settings.CreateMeasurementGraphics();
        using var font = new Font("Arial", 10f);

        Assert.Equal(2355f / 2048f * 10f * 100f / 72f, font.GetHeight(graphics), 2);
    }

    [Fact]
    public void Without_a_printer_or_a_file_printing_fails_as_for_a_missing_printer()
    {
        using var document = new PrintDocument();
        document.PrintController = new StandardPrintController();
        document.PrintPage += (_, _) => { };

        Assert.Throws<InvalidPrinterException>(() => document.Print());
    }

    /// <summary>
    /// The page is 8.5 by 11 inches, so 850 by 1100 hundredths, and the default
    /// margins are an inch: a rectangle drawn on the margin bounds starts a
    /// hundred units in.
    /// </summary>
    [Fact]
    public void A_preview_page_is_drawn_in_hundredths_of_an_inch()
    {
        using var document = new PrintDocument();
        var preview = new PreviewPrintController();
        document.PrintController = preview;

        Rectangle pageBounds = default;
        Rectangle marginBounds = default;
        document.PrintPage += (_, e) =>
        {
            pageBounds = e.PageBounds;
            marginBounds = e.MarginBounds;
            e.Graphics!.FillRectangle(Brushes.Black, e.MarginBounds);
        };

        document.Print();

        var page = Assert.Single(preview.GetPreviewPageInfo());
        var image = (Bitmap)page.Image;

        Assert.Equal(new Rectangle(0, 0, 850, 1100), pageBounds);
        Assert.Equal(new Rectangle(100, 100, 650, 900), marginBounds);
        Assert.Equal(new Size(850, 1100), image.Size);
        Assert.Equal(0, image.GetPixel(99, 500).A);
        Assert.Equal(255, image.GetPixel(100, 500).A);
        Assert.Equal(255, image.GetPixel(749, 999).A);
        Assert.Equal(0, image.GetPixel(750, 999).A);
    }

    /// <summary>
    /// Ten points are ten seventy-seconds of an inch, 13.9 hundredths: on a
    /// page, where the device unit is the hundredth of an inch, a font's
    /// height follows the page's resolution and not the screen's.
    /// </summary>
    [Fact]
    public void Text_on_a_page_is_as_tall_as_on_paper()
    {
        using var document = new PrintDocument();
        document.PrintController = new PreviewPrintController();

        var height = 0f;
        document.PrintPage += (_, e) =>
        {
            using var font = new Font("Arial", 10f);
            height = font.GetHeight(e.Graphics!);
        };

        document.Print();

        Assert.Equal(2355f / 2048f * 10f * 100f / 72f, height, 2);
    }

    [Fact]
    public void A_landscape_page_is_wider_than_it_is_tall()
    {
        using var document = new PrintDocument();
        document.DefaultPageSettings.Landscape = true;
        document.PrintController = new PreviewPrintController();

        Rectangle pageBounds = default;
        document.PrintPage += (_, e) => pageBounds = e.PageBounds;
        document.Print();

        Assert.Equal(new Size(1100, 850), pageBounds.Size);
    }

    [Fact]
    public void Margins_convert_to_text_and_back()
    {
        var converter = new MarginsConverter();
        var margins = new Margins(10, 20, 30, 40);

        var text = (string)converter.ConvertTo(null, CultureInfo.InvariantCulture, margins, typeof(string))!;
        var back = (Margins)converter.ConvertFrom(null, CultureInfo.InvariantCulture, text)!;

        Assert.Equal(margins, back);
    }

    [Fact]
    public void Margins_descriptor_recreates_all_four_edges()
    {
        var converter = new MarginsConverter();
        var margins = new Margins(10, 20, 30, 40);

        var descriptor = (InstanceDescriptor)converter.ConvertTo(margins, typeof(InstanceDescriptor))!;
        var recreated = (Margins)descriptor.Invoke()!;

        Assert.Equal(margins, recreated);
        Assert.True(descriptor.IsComplete);
    }

    private static int CountPages(string pdf)
    {
        var count = 0;
        var at = 0;

        while ((at = pdf.IndexOf("/Type /Page", at, StringComparison.Ordinal)) >= 0)
        {
            // "/Type /Pages" is the page tree, not a page.
            if (at + 11 >= pdf.Length || pdf[at + 11] != 's') count++;

            at += 11;
        }

        return count;
    }
}
