using System.ComponentModel;

namespace System.Drawing.Printing;

/// <summary>
/// Runs a print job: reads one PageSettings per page through QueryPageSettings,
/// has its PrintController open the job and one Graphics per page, raises
/// PrintPage for each, and closes the job.
/// </summary>
public class PrintDocument : Component
{
    private PrinterSettings _printerSettings = new();
    private PageSettings? _defaultPageSettings;
    private PrintController _printController = new StandardPrintController();
    private string _documentName = "document";

    public PrintDocument()
    {
    }

    public event PrintEventHandler? BeginPrint;

    public event PrintEventHandler? EndPrint;

    public event PrintPageEventHandler? PrintPage;

    public event QueryPageSettingsEventHandler? QueryPageSettings;

    public PageSettings DefaultPageSettings
    {
        get => _defaultPageSettings ??= new PageSettings(_printerSettings);
        set => _defaultPageSettings = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string DocumentName
    {
        get => _documentName;
        set => _documentName = value ?? throw new ArgumentNullException(nameof(value));
    }

    public bool OriginAtMargins { get; set; }

    public PrintController PrintController
    {
        get => _printController;
        set => _printController = value ?? throw new ArgumentNullException(nameof(value));
    }

    public PrinterSettings PrinterSettings
    {
        get => _printerSettings;
        set
        {
            _printerSettings = value ?? throw new ArgumentNullException(nameof(value));

            // GDI+ rebuilds the default page from whichever printer is now
            // active the next time it is asked, rather than keeping the old
            // printer's page around under a new PrinterSettings.
            _defaultPageSettings = null;
        }
    }

    protected virtual void OnBeginPrint(PrintEventArgs e) => BeginPrint?.Invoke(this, e);

    protected virtual void OnEndPrint(PrintEventArgs e) => EndPrint?.Invoke(this, e);

    protected virtual void OnPrintPage(PrintPageEventArgs e) => PrintPage?.Invoke(this, e);

    protected virtual void OnQueryPageSettings(QueryPageSettingsEventArgs e) => QueryPageSettings?.Invoke(this, e);

    public override string ToString() => $"[PrintDocument {DocumentName}]";

    public void Print()
    {
        var controller = PrintController;

        var action = PrinterSettings.PrintToFile ? PrintAction.PrintToFile
            : controller.IsPreview ? PrintAction.PrintToPreview
            : PrintAction.PrintToPrinter;

        var printEventArgs = new PrintEventArgs(action);

        controller.OnStartPrint(this, printEventArgs);

        // The end of the job runs whatever a handler throws, so that the file
        // being printed to is closed and the controller lets go of its pages.
        try
        {
            OnBeginPrint(printEventArgs);

            if (!printEventArgs.Cancel)
            {
                RunPages(controller);
            }
        }
        finally
        {
            try
            {
                OnEndPrint(printEventArgs);
            }
            finally
            {
                controller.OnEndPrint(this, printEventArgs);
            }
        }
    }

    private void RunPages(PrintController controller)
    {
        while (true)
        {
            var pageSettings = (PageSettings)DefaultPageSettings.Clone();
            var queryArgs = new QueryPageSettingsEventArgs(pageSettings);

            OnQueryPageSettings(queryArgs);
            if (queryArgs.Cancel) return;

            var pageBounds = queryArgs.PageSettings.Bounds;
            var margins = queryArgs.PageSettings.Margins;
            var marginBounds = new Rectangle(
                margins.Left,
                margins.Top,
                Math.Max(0, pageBounds.Width - margins.Left - margins.Right),
                Math.Max(0, pageBounds.Height - margins.Top - margins.Bottom));

            // The real Graphics only exists once the controller builds it below;
            // this placeholder is never observed, since nothing reads Graphics
            // before OnStartPage replaces it through the internal setter.
            var pageEventArgs = new PrintPageEventArgs(null!, marginBounds, pageBounds, queryArgs.PageSettings);
            var pageGraphics = controller.OnStartPage(this, pageEventArgs);

            if (OriginAtMargins) pageGraphics.TranslateTransform(marginBounds.X, marginBounds.Y);

            pageEventArgs.Graphics = pageGraphics;

            try
            {
                OnPrintPage(pageEventArgs);
            }
            finally
            {
                controller.OnEndPage(this, pageEventArgs);
            }

            if (pageEventArgs.Cancel || !pageEventArgs.HasMorePages) return;
        }
    }
}
