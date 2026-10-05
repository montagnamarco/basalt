using System.Drawing.Imaging;

namespace System.Drawing.Printing;

/// <summary>
/// Which printer a PrintDocument targets and how. Printer discovery uses the
/// platform spooler, while print job submission is handled by the controller.
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

    // Discovery and capabilities round-trip to a native spooler process or
    // P/Invoke call; both snapshots are cached per PrinterSettings instance
    // (not shared) until PrinterName changes, so a printer installed later is
    // only seen by a new instance or after reselecting a printer, and that
    // reading several properties in a row (as callers routinely do) costs
    // one discovery, not one per property.
    private readonly object _discoveryLock = new();
    private SystemPrinterDiscovery.Snapshot? _discoverySnapshot;
    private PrinterCapabilities? _capabilities;
    private string? _capabilitiesPrinterName;

    public PrinterSettings()
    {
    }

    public bool CanDuplex => Capabilities.CanDuplex ?? false;

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

    public static StringCollection InstalledPrinters => new(SystemPrinterDiscovery.Discover().Names);

    /// <summary>
    /// An unset name selects the system default if one is configured.
    /// </summary>
    public bool IsDefaultPrinter
    {
        get
        {
            var defaultName = DiscoverySnapshot.DefaultName;
            return defaultName is not null && (_printerName.Length == 0 || string.Equals(_printerName, defaultName, StringComparison.Ordinal));
        }
    }

    public bool IsPlotter => false;

    /// <summary>A printer is valid when the selected name is installed.</summary>
    public bool IsValid
    {
        get
        {
            var snapshot = DiscoverySnapshot;
            var name = _printerName.Length == 0 ? snapshot.DefaultName : _printerName;
            if (name is null) return false;

            for (var i = 0; i < snapshot.Names.Length; i++)
            {
                if (string.Equals(snapshot.Names[i], name, StringComparison.Ordinal)) return true;
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

    public PaperSizeCollection PaperSizes => new(PrinterName.Length == 0
        ? [
            new PaperSize("Letter", 850, 1100) { RawKind = (int)PaperKind.Letter },
            new PaperSize("A4", 827, 1169) { RawKind = (int)PaperKind.A4 },
        ]
        : Capabilities.PaperSizes);

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
        get => _printerName.Length == 0 ? DiscoverySnapshot.DefaultName ?? string.Empty : _printerName;
        set
        {
            _printerName = value ?? throw new ArgumentNullException(nameof(value));
            InvalidateDiscoveryCache();
        }
    }

    public PrinterResolutionCollection PrinterResolutions => new(PrinterName.Length == 0
        ? [new PrinterResolution { Kind = PrinterResolutionKind.Custom, X = 300, Y = 300 }]
        : Capabilities.Resolutions);

    public bool SupportsColor => PrinterName.Length == 0 || Capabilities.SupportsColor == true;

    public int ToPage
    {
        get => _toPage;
        set => _toPage = value;
    }

    /// <summary>
    /// The list of installed printers and the system default, fetched at
    /// most once per selected printer name.
    /// </summary>
    private SystemPrinterDiscovery.Snapshot DiscoverySnapshot
    {
        get
        {
            lock (_discoveryLock)
            {
                return _discoverySnapshot ??= SystemPrinterDiscovery.Discover();
            }
        }
    }

    /// <summary>
    /// The selected printer's capabilities, fetched at most once per
    /// selected printer name and re-fetched only if the resolved name
    /// (explicit, or the system default when none is set) changes.
    /// </summary>
    private PrinterCapabilities Capabilities
    {
        get
        {
            var name = PrinterName;
            lock (_discoveryLock)
            {
                if (_capabilities is not null && string.Equals(_capabilitiesPrinterName, name, StringComparison.Ordinal))
                    return _capabilities;

                var discovered = PrinterCapabilities.Discover(name);
                _capabilities = discovered;
                _capabilitiesPrinterName = name;
                return discovered;
            }
        }
    }

    private void InvalidateDiscoveryCache()
    {
        lock (_discoveryLock)
        {
            _discoverySnapshot = null;
            _capabilities = null;
            _capabilitiesPrinterName = null;
        }
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
