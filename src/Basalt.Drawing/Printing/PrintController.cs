namespace System.Drawing.Printing;

/// <summary>
/// The hooks a PrintDocument calls while printing: once at the start and end
/// of the run, and once per page. The base no-op implementations are GDI+'s
/// own: a bare PrintController does nothing, and StandardPrintController or
/// PreviewPrintController (or a caller's own subclass) does the real work.
/// </summary>
public abstract class PrintController
{
    protected PrintController()
    {
    }

    public virtual bool IsPreview => false;

    public virtual void OnStartPrint(PrintDocument document, PrintEventArgs e)
    {
    }

    // GDI+'s own base returns null here too: nothing is drawn until a real
    // controller hands back a Graphics.
    public virtual Graphics OnStartPage(PrintDocument document, PrintPageEventArgs e) => null!;

    public virtual void OnEndPage(PrintDocument document, PrintPageEventArgs e)
    {
    }

    public virtual void OnEndPrint(PrintDocument document, PrintEventArgs e)
    {
    }
}
