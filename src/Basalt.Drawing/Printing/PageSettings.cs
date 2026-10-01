namespace System.Drawing.Printing;

/// <summary>
/// One page's paper, orientation, margins, colour, resolution and source; all
/// of its measurements are in hundredths of an inch, GDI+'s printer unit.
/// </summary>
public class PageSettings : ICloneable
{
    private PrinterSettings _printerSettings;

    public PageSettings() : this(new PrinterSettings())
    {
    }

    public PageSettings(PrinterSettings printerSettings)
    {
        ArgumentNullException.ThrowIfNull(printerSettings);

        _printerSettings = printerSettings;
        PaperSize = new PaperSize("Letter", 850, 1100) { RawKind = (int)PaperKind.Letter };
        PaperSource = new PaperSource { RawKind = (int)PaperSourceKind.AutomaticFeed, SourceName = "Automatic" };
        PrinterResolution = new PrinterResolution { Kind = PrinterResolutionKind.Custom, X = 300, Y = 300 };
        Margins = new Margins();
        Color = true;
    }

    public bool Color { get; set; }

    /// <summary>
    /// The size, in hundredths of an inch, swapped when Landscape is set: GDI+
    /// reports the page as the printer will lay it down, not as the paper
    /// itself is labelled.
    /// </summary>
    public Rectangle Bounds
    {
        get
        {
            var width = PaperSize.Width;
            var height = PaperSize.Height;

            if (Landscape) (width, height) = (height, width);

            return new Rectangle(0, 0, width, height);
        }
    }

    /// <summary>
    /// There is no real printer to ask for its unprintable edge here, so this
    /// is always 0; not compared with what a Windows driver reports.
    /// </summary>
    public float HardMarginX => 0f;

    public float HardMarginY => 0f;

    public bool Landscape { get; set; }

    public Margins Margins { get; set; }

    public PaperSize PaperSize { get; set; }

    public PaperSource PaperSource { get; set; }

    /// <summary>
    /// The full page, since there is no hardware margin to subtract; not
    /// compared with a real printer's printable area.
    /// </summary>
    public RectangleF PrintableArea
    {
        get
        {
            var bounds = Bounds;
            return new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
    }

    public PrinterResolution PrinterResolution { get; set; } = new();

    public PrinterSettings PrinterSettings
    {
        get => _printerSettings;
        set => _printerSettings = value ?? throw new ArgumentNullException(nameof(value));
    }

    public object Clone()
    {
        var clone = new PageSettings(_printerSettings)
        {
            Color = Color,
            Landscape = Landscape,
            Margins = (Margins)Margins.Clone(),
            PaperSize = PaperSize,
            PaperSource = PaperSource,
            PrinterResolution = PrinterResolution,
        };

        return clone;
    }

    public void CopyToHdevmode(IntPtr hdevmode) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public void SetHdevmode(IntPtr hdevmode) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public override string ToString() =>
        $"[PageSettings: Color={Color}, Landscape={Landscape}, Margins={Margins}, PaperSize={PaperSize}, PaperSource={PaperSource}, PrinterResolution={PrinterResolution}]";
}
