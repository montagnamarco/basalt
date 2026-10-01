using System.ComponentModel;

namespace System.Drawing.Printing;

/// <summary>Raised before the first page and after the last; BeginPrint/EndPrint carry one of these.</summary>
public class PrintEventArgs : CancelEventArgs
{
    public PrintEventArgs() : this(PrintAction.PrintToPrinter)
    {
    }

    // Only a PrintDocument knows, at the point it raises BeginPrint/EndPrint,
    // whether this run goes to a file, a preview or (never, here) a real
    // printer; a caller constructing its own PrintEventArgs always gets the
    // GDI+ default of PrintToPrinter.
    internal PrintEventArgs(PrintAction action)
    {
        PrintAction = action;
    }

    public PrintAction PrintAction { get; }
}
