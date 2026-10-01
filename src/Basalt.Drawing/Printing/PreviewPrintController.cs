namespace System.Drawing.Printing;

/// <summary>
/// Collects each page as a Bitmap instead of sending it anywhere, for a
/// caller that wants to show what printing would produce. One bitmap pixel
/// per hundredth of an inch (GDI+'s printer unit), not a real screen's
/// density: that has not been compared with Windows' own preview dialog.
/// </summary>
public class PreviewPrintController : PrintController
{
    private readonly List<PreviewPageInfo> _pages = [];
    private Bitmap? _currentPage;

    public PreviewPrintController()
    {
    }

    public override bool IsPreview => true;

    public virtual bool UseAntiAlias { get; set; } = true;

    public PreviewPageInfo[] GetPreviewPageInfo() => _pages.ToArray();

    public override void OnStartPrint(PrintDocument document, PrintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(e);

        // A fresh run replaces whatever an earlier Print() call collected.
        _pages.Clear();
    }

    public override Graphics OnStartPage(PrintDocument document, PrintPageEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var width = Math.Max(1, e.PageBounds.Width);
        var height = Math.Max(1, e.PageBounds.Height);

        _currentPage = new Bitmap(width, height);

        var graphics = new Graphics(new SkiaSharp.SKCanvas(_currentPage.Pixels), ownsCanvas: true, _currentPage) { Resolution = 100f, IsPrinter = true };
        _currentPage.MarkInUse();
        graphics.PageUnit = GraphicsUnit.Display;

        return graphics;
    }

    public override void OnEndPage(PrintDocument document, PrintPageEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        e.Graphics.Dispose();

        if (_currentPage is not null)
        {
            _pages.Add(new PreviewPageInfo(_currentPage, _currentPage.Size));
            _currentPage = null;
        }
    }

    public override void OnEndPrint(PrintDocument document, PrintEventArgs e)
    {
        // Each page's Bitmap was already finished in OnEndPage; there is
        // nothing left to flush once printing ends.
    }
}
