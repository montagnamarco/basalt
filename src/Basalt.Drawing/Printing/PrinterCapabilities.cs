namespace System.Drawing.Printing;

internal sealed record PrinterCapabilities(
    bool? CanDuplex,
    bool? SupportsColor,
    PaperSize[] PaperSizes,
    PrinterResolution[] Resolutions)
{
    internal static readonly PrinterCapabilities Unknown = new(null, null, [], []);

    /// <summary>
    /// Test-only seam: when set, <see cref="Discover"/> returns this instead of
    /// calling into the platform spooler (P/Invoke on Windows, lpoptions on
    /// CUPS). <see cref="PrinterSettings"/> caches whatever this returns.
    /// </summary>
    internal static Func<string, PrinterCapabilities>? DiscoveryOverride { get; set; }

    internal static PrinterCapabilities Discover(string printerName)
    {
        if (string.IsNullOrEmpty(printerName)) return Unknown;
        if (DiscoveryOverride is not null) return DiscoveryOverride(printerName);
        return OperatingSystem.IsWindows()
            ? WindowsPrinterCapabilities.Discover(printerName)
            : CupsPrinterCapabilities.Discover(printerName);
    }
}
