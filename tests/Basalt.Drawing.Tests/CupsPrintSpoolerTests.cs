using System.Drawing.Printing;

namespace Basalt.Drawing.Tests;

[Collection(PrinterDiscoveryCollection.Name)]
public class CupsPrintSpoolerTests
{
    [Fact]
    public void Pdf_job_arguments_include_printer_copies_collation_and_duplex()
    {
        var settings = new PrinterSettings
        {
            PrinterName = "Office Queue",
            Copies = 3,
            Collate = false,
            Duplex = Duplex.Horizontal,
        };

        var arguments = CupsPrintSpooler.BuildArguments("/tmp/print job.pdf", settings, "Monthly report");

        Assert.Equal([
            "-d", "Office Queue", "-t", "Monthly report", "-n", "3",
            "-o", "collate=false", "-o", "sides=two-sided-short-edge",
            "/tmp/print job.pdf",
        ], arguments);
    }

    [Fact]
    public void Cancelled_page_marks_the_job_cancelled_before_submission()
    {
        using var document = new PrintDocument();
        document.PrintController = new PreviewPrintController();
        document.PrintPage += (_, e) => e.Cancel = true;

        bool? cancelledAtEnd = null;
        document.EndPrint += (_, e) => cancelledAtEnd = e.Cancel;

        document.Print();

        Assert.True(cancelledAtEnd);
    }

    [Fact]
    public void Cancelled_page_query_marks_the_job_cancelled_before_submission()
    {
        using var document = new PrintDocument();
        document.PrintController = new PreviewPrintController();
        document.QueryPageSettings += (_, e) => e.Cancel = true;

        bool? cancelledAtEnd = null;
        document.EndPrint += (_, e) => cancelledAtEnd = e.Cancel;

        document.Print();

        Assert.True(cancelledAtEnd);
    }

    [Fact]
    public void Failed_page_marks_the_job_cancelled_before_submission()
    {
        using var document = new PrintDocument();
        document.PrintController = new PreviewPrintController();
        document.PrintPage += (_, _) => throw new InvalidOperationException("Rendering failed");

        bool? cancelledAtEnd = null;
        document.EndPrint += (_, e) => cancelledAtEnd = e.Cancel;

        Assert.Throws<InvalidOperationException>(() => document.Print());
        Assert.True(cancelledAtEnd);
    }

    [Fact]
    public void Failed_end_handler_marks_the_job_cancelled_before_submission()
    {
        using var document = new PrintDocument();
        var controller = new CancelRecordingController();
        document.PrintController = controller;
        document.PrintPage += (_, _) => { };
        document.EndPrint += (_, _) => throw new InvalidOperationException("End handler failed");

        Assert.Throws<InvalidOperationException>(() => document.Print());
        Assert.True(controller.CancelledAtEnd);
    }

    private sealed class CancelRecordingController : PreviewPrintController
    {
        public bool CancelledAtEnd { get; private set; }

        public override void OnEndPrint(PrintDocument document, PrintEventArgs e)
        {
            CancelledAtEnd = e.Cancel;
            base.OnEndPrint(document, e);
        }
    }
}
