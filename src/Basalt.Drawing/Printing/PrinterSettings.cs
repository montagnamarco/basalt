using System.Drawing.Imaging;

namespace System.Drawing.Printing;

/// <summary>
/// Which printer a PrintDocument targets and how. There is no real printer
/// to discover on this platform: InstalledPrinters is always empty, and any
/// PrinterName that is not the empty string ("no printer selected") fails
/// IsValid, the same way GDI+ fails it for a name no driver recognises.
/// </summary>
public partial class PrinterSettings : ICloneable
{
    private string _printerName = string.Empty;
    private string _printFileName = string.Empty;
    private bool _printToFile;
    private bool _collate = true;
    private short _copies = 1;
    private Duplex _duplex = Duplex.Simplex;
    private int _fromPage;
    private int _toPage;
    private int _minimumPage;
    private int _maximumPage = 9999;
    private PrintRange _printRange = PrintRange.AllPages;

    public PrinterSettings()
    {
    }

    public bool CanDuplex => false;

    public bool Collate
    {
        get => _collate;
        set => _collate = value;
    }

    public short Copies
    {
        get => _copies;
        set => _copies = value;
    }

    public PageSettings DefaultPageSettings => new(this);

    public Duplex Duplex
    {
        get => _duplex;
        set => _duplex = value;
    }

    public int FromPage
    {
        get => _fromPage;
        set => _fromPage = value;
    }

    /// <summary>Always empty: there is no printer spooler to enumerate here.</summary>
    public static StringCollection InstalledPrinters => new([]);

    /// <summary>
    /// "" means no printer is selected at all, rather than Windows' usual
    /// meaning of "whichever printer the system default is": there is no
    /// system default to fall back to on this platform.
    /// </summary>
    public bool IsDefaultPrinter => string.IsNullOrEmpty(PrinterName);

    public bool IsPlotter => false;

    /// <summary>A printer is valid only if it is both named and installed, and none ever is.</summary>
    public bool IsValid
    {
        get
        {
            if (PrinterName.Length == 0) return false;

            var installed = InstalledPrinters;
            for (var i = 0; i < installed.Count; i++)
            {
                if (string.Equals(installed[i], PrinterName, StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }

    /// <summary>Not verified against a real driver: GDI+'s most common value.</summary>
    public int LandscapeAngle => 90;

    public int MaximumCopies => 9999;

    public int MaximumPage
    {
        get => _maximumPage;
        set => _maximumPage = value;
    }

    public int MinimumPage
    {
        get => _minimumPage;
        set => _minimumPage = value;
    }

    /// <summary>
    /// Letter and A4, the two sizes most test documents assume; a real
    /// driver would list many more and this has not been compared with one.
    /// </summary>
    public PaperSizeCollection PaperSizes => new([
        new PaperSize("Letter", 850, 1100) { RawKind = (int)PaperKind.Letter },
        new PaperSize("A4", 827, 1169) { RawKind = (int)PaperKind.A4 },
    ]);

    public PaperSourceCollection PaperSources => new([
        new PaperSource { RawKind = (int)PaperSourceKind.AutomaticFeed, SourceName = "Automatic" },
    ]);

    public string PrintFileName
    {
        get => _printFileName;
        set => _printFileName = value ?? throw new ArgumentNullException(nameof(value));
    }

    public PrintRange PrintRange
    {
        get => _printRange;
        set => _printRange = value;
    }

    public bool PrintToFile
    {
        get => _printToFile;
        set => _printToFile = value;
    }

    public string PrinterName
    {
        get => _printerName;
        set => _printerName = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>A single 300 dpi entry: there is no driver to list the real set.</summary>
    public PrinterResolutionCollection PrinterResolutions => new([
        new PrinterResolution { Kind = PrinterResolutionKind.Custom, X = 300, Y = 300 },
    ]);

    public bool SupportsColor => true;

    public int ToPage
    {
        get => _toPage;
        set => _toPage = value;
    }

    public object Clone()
    {
        var clone = new PrinterSettings
        {
            PrinterName = PrinterName,
            PrintFileName = PrintFileName,
            PrintToFile = PrintToFile,
            Collate = Collate,
            Copies = Copies,
            Duplex = Duplex,
            FromPage = FromPage,
            ToPage = ToPage,
            MinimumPage = MinimumPage,
            MaximumPage = MaximumPage,
            PrintRange = PrintRange,
        };

        return clone;
    }

    /// <summary>
    /// Builds a Graphics sized and unit-matched to a page, for callers that
    /// measure text or layout before actually printing. No native handle is
    /// needed for this, only a bitmap to draw into, so unlike GetHdevmode and
    /// its relatives this is implemented rather than refused.
    /// </summary>
    public Graphics CreateMeasurementGraphics() => CreateMeasurementGraphics(DefaultPageSettings, honorOriginAtMargins: false);

    public Graphics CreateMeasurementGraphics(bool honorOriginAtMargins) => CreateMeasurementGraphics(DefaultPageSettings, honorOriginAtMargins);

    public Graphics CreateMeasurementGraphics(PageSettings pageSettings) => CreateMeasurementGraphics(pageSettings, honorOriginAtMargins: false);

    public Graphics CreateMeasurementGraphics(PageSettings pageSettings, bool honorOriginAtMargins)
    {
        ArgumentNullException.ThrowIfNull(pageSettings);

        var bounds = pageSettings.Bounds;

        // One pixel per hundredth of an inch and the page's own resolution,
        // so that text measured here is as wide as on the printed page.
        var bitmap = new Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
        var graphics = new Graphics(new SkiaSharp.SKCanvas(bitmap.Pixels), ownsCanvas: true, bitmap) { Resolution = 100f, IsPrinter = true };
        bitmap.MarkInUse();
        graphics.PageUnit = GraphicsUnit.Display;

        if (honorOriginAtMargins)
        {
            var margins = pageSettings.Margins;
            graphics.TranslateTransform(margins.Left, margins.Top);
        }

        return graphics;
    }

    public IntPtr GetHdevmode() => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public IntPtr GetHdevmode(PageSettings pageSettings) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public IntPtr GetHdevnames() => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    /// <summary>
    /// Whether a printer can accept this image's bytes directly, skipping
    /// GDI+'s own decoding: there is no such printer here, so this is always
    /// false. Not verified against a driver that actually supports it.
    /// </summary>
    public bool IsDirectPrintingSupported(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return false;
    }

    public bool IsDirectPrintingSupported(ImageFormat imageFormat)
    {
        ArgumentNullException.ThrowIfNull(imageFormat);
        return false;
    }

    public void SetHdevmode(IntPtr hdevmode) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public void SetHdevnames(IntPtr hdevnames) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public override string ToString() =>
        $"[PrinterSettings {PrinterName} Copies={Copies} Collate={Collate} Duplex={Duplex} FromPage={FromPage} LandscapeAngle={LandscapeAngle} MaximumCopies={MaximumCopies} OutputPort= ToPage={ToPage}]";
}
