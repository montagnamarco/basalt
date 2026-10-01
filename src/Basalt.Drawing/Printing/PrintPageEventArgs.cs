namespace System.Drawing.Printing;

/// <summary>Raised once per page, with the Graphics to draw that page on.</summary>
public class PrintPageEventArgs : EventArgs
{
    private Graphics _graphics;

    public PrintPageEventArgs(Graphics graphics, Rectangle marginBounds, Rectangle pageBounds, PageSettings pageSettings)
    {
        _graphics = graphics;
        MarginBounds = marginBounds;
        PageBounds = pageBounds;
        PageSettings = pageSettings ?? throw new ArgumentNullException(nameof(pageSettings));
    }

    public bool Cancel { get; set; }

    // No public setter: GDI+ builds this event args before the page's real
    // Graphics exists (its bounds already come from PageSettings) and fills
    // the Graphics in once the PrintController has produced it.
    public Graphics Graphics
    {
        get => _graphics;
        internal set => _graphics = value;
    }

    public bool HasMorePages { get; set; }

    public Rectangle MarginBounds { get; }

    public Rectangle PageBounds { get; }

    public PageSettings PageSettings { get; }
}
