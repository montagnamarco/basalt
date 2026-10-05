using System.IO;
using SkiaSharp;

namespace System.Drawing.Printing;

/// <summary>
/// Renders a document as PDF and writes it to a file or submits it to CUPS.
/// Windows physical printing needs a GDI or XPS renderer; Windows printer
/// drivers generally cannot consume PDF bytes directly.
/// </summary>
public class StandardPrintController : PrintController
{
    // One hundredth of an inch in PDF points (1/72 inch): the factor the page
    // canvas is pre-scaled by, so a PrintPage handler's coordinates (still in
    // hundredths of an inch, GDI+'s printer unit) land on the right point.
    private const float PointsPerHundredthInch = 72f / 100f;

    private SKDocument? _document;
    private Stream? _fileStream;
    private readonly Func<Stream, SKDocument> _createPdf;
    private readonly Action<SKDocument> _closePdf;
    private string? _spoolFile;
    private int _pageCount;
    private WindowsPrinterSpooler? _windowsSpooler;

    public StandardPrintController()
        : this(SKDocument.CreatePdf, static pdf => pdf.Close())
    {
    }

    internal StandardPrintController(Func<Stream, SKDocument> createPdf, Action<SKDocument> closePdf)
    {
        _createPdf = createPdf ?? throw new ArgumentNullException(nameof(createPdf));
        _closePdf = closePdf ?? throw new ArgumentNullException(nameof(closePdf));
    }

    public override void OnStartPrint(PrintDocument document, PrintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(e);

        var settings = document.PrinterSettings;

        _pageCount = 0;
        _spoolFile = null;
        _windowsSpooler = null;

        string outputPath;
        if (settings.PrintToFile)
        {
            if (string.IsNullOrEmpty(settings.PrintFileName)) throw new InvalidPrinterException(settings);
            outputPath = settings.PrintFileName;
        }
        else
        {
            if (!settings.IsValid) throw new InvalidPrinterException(settings);
            if (OperatingSystem.IsWindows())
            {
                _windowsSpooler = new WindowsPrinterSpooler(settings, document.DocumentName);
                return;
            }

            _spoolFile = Path.Combine(Path.GetTempPath(), $"basalt-print-{Guid.NewGuid():N}.pdf");
            outputPath = _spoolFile;
        }

        try
        {
            _fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            _document = _createPdf(_fileStream);
        }
        catch
        {
            try
            {
                try
                {
                    DisposePdfResources();
                }
                finally
                {
                    DeleteSpoolFile();
                }
            }
            catch
            {
                // Preserve the failure that prevented the job from opening.
            }

            throw;
        }
    }

    public override Graphics OnStartPage(PrintDocument document, PrintPageEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_windowsSpooler is not null) return _windowsSpooler.BeginPage(e);

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

        if (_windowsSpooler is not null)
        {
            _windowsSpooler.EndPage(e);
            _pageCount++;
            return;
        }

        e.Graphics.Dispose();
        _document?.EndPage();
        _pageCount++;
    }

    public override void OnEndPrint(PrintDocument document, PrintEventArgs e)
    {
        if (_windowsSpooler is not null)
        {
            try
            {
                _windowsSpooler.End(e.Cancel || _pageCount == 0);
            }
            finally
            {
                _windowsSpooler = null;
            }
            return;
        }

        try
        {
            try
            {
                if (_document is not null) _closePdf(_document);
            }
            finally
            {
                DisposePdfResources();
            }

            if (_spoolFile is not null && !e.Cancel && _pageCount > 0)
                CupsPrintSpooler.Submit(_spoolFile, document.PrinterSettings, document.DocumentName);
        }
        finally
        {
            DeleteSpoolFile();
        }
    }

    private void DisposePdfResources()
    {
        try
        {
            _document?.Dispose();
        }
        finally
        {
            _document = null;
            try
            {
                _fileStream?.Dispose();
            }
            finally
            {
                _fileStream = null;
            }
        }
    }

    private void DeleteSpoolFile()
    {
        var path = _spoolFile;
        _spoolFile = null;
        if (path is not null) File.Delete(path);
    }
}
