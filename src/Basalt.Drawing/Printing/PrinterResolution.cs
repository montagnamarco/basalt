namespace System.Drawing.Printing;

/// <summary>A resolution, in dots per inch, a printer can render a page at.</summary>
public class PrinterResolution
{
    public PrinterResolutionKind Kind { get; set; } = PrinterResolutionKind.Custom;

    public int X { get; set; }

    public int Y { get; set; }

    public override string ToString() => $"[PrinterResolution X={X} Y={Y} Kind={Kind}]";
}
