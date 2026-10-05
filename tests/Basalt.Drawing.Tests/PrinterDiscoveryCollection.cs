namespace Basalt.Drawing.Tests;

/// <summary>
/// Printer discovery is process-wide static state (SystemPrinterDiscovery and
/// PrinterCapabilities overrides). Every test class that installs a fake or
/// reads discovery through PrinterSettings shares this collection, which xUnit
/// never runs in parallel with itself, so a fake printer cannot leak into a
/// test that expects no printer (or submit a real lp job).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PrinterDiscoveryCollection
{
    public const string Name = "Printer discovery";
}
