using System.IO;
using SkiaSharp;

namespace System.Drawing.Printing;

/// <summary>
/// Sends a document to the printer. There is no real printer on this
/// platform, so the only job this can actually carry out is one redirected to
/// a file: PrinterSettings.PrintToFile with a PrintFileName writes a PDF, one
/// page per PrintPage, sized from PageSettings. Anything else fails exactly
/// the way GDI+ fails for a printer it cannot find, with InvalidPrinterException.
/// </summary>
public class StandardPrintController : PrintController
{
    // One hundredth of an inch in PDF points (1/72 inch): the factor the page
    // canvas is pre-scaled by, so a PrintPage handler's coordinates (still in
    // hundredths of an inch, GDI+'s printer unit) land on the right point.
    private const float PointsPerHundredthInch = 72f / 100f;

    private SKDocument? _document;
    private FileStream? _fileStream;

    public StandardPrintController()
    {
    }

    public override void OnStartPrint(PrintDocument document, PrintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(e);

        var settings = document.PrinterSettings;

        if (!settings.PrintToFile || string.IsNullOrEmpty(settings.PrintFileName))
        {
            // Not compared against a real Windows error: there, this path
            // reaches a missing or offline printer instead of a missing file.
            throw new InvalidPrinterException(settings);
        }

        _fileStream = new FileStream(settings.PrintFileName, FileMode.Create, FileAccess.Write);
        _document = SKDocument.CreatePdf(_fileStream);
    }

    public override Graphics OnStartPage(PrintDocument document, PrintPageEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_document is null) throw new InvalidOperationException("StandardPrintController.OnStartPage was called before OnStartPrint opened the document.");

        var widthPoints = e.PageBounds.Width * PointsPerHundredthInch;
        var heightPoints = e.PageBounds.Height * PointsPerHundredthInch;

        var canvas = _document.BeginPage(widthPoints, heightPoints);
        canvas.Scale(PointsPerHundredthInch, PointsPerHundredthInch);

        // GDI+ does not own this canvas: SKDocument keeps it alive until
        // EndPage, so Graphics must not dispose of it.
        var graphics = new Graphics(canvas, ownsCanvas: false) { Resolution = 100f, IsPrinter = true };
        graphics.PageUnit = GraphicsUnit.Display;

        return graphics;
    }

    public override void OnEndPage(PrintDocument document, PrintPageEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        e.Graphics.Dispose();
        _document?.EndPage();
    }

    public override void OnEndPrint(PrintDocument document, PrintEventArgs e)
    {
        _document?.Close();
        _document?.Dispose();
        _document = null;

        _fileStream?.Dispose();
        _fileStream = null;
    }
}
