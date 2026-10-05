using System.Drawing.Printing;

namespace Basalt.Drawing.Tests;

[Collection(PrinterDiscoveryCollection.Name)]
public class PrinterDiscoveryTests
{
    [Fact]
    public void Cups_output_identifies_printers_and_default_destination()
    {
        var printers = SystemPrinterDiscovery.ParseCupsPrinters(
            "Office accepting requests since Mon 01 Jan 2024 00:00:00 UTC\n" +
            "PDF_Printer not accepting requests since Mon 01 Jan 2024 00:00:00 UTC\n" +
            "Office accepting requests since Mon 01 Jan 2024 00:00:00 UTC\n");

        Assert.Equal(["Office", "PDF_Printer"], printers);
        Assert.Equal("Office", SystemPrinterDiscovery.ParseCupsDefault("system default destination: Office\n"));
        Assert.Null(SystemPrinterDiscovery.ParseCupsDefault("no system default destination\n"));
    }

    [Fact]
    public void Printer_settings_reflect_a_fake_spooler_snapshot()
    {
        using var fake = FakeDiscovery.Install(
            new SystemPrinterDiscovery.Snapshot(["Office", "Warehouse"], "Office"),
            name => new PrinterCapabilities(true, true, [new PaperSize("A4", 827, 1169)], [new PrinterResolution { Kind = PrinterResolutionKind.Custom, X = 600, Y = 600 }]));

        var settings = new PrinterSettings();

        Assert.Equal("Office", settings.PrinterName);
        Assert.True(settings.IsDefaultPrinter);
        Assert.True(settings.IsValid);
        Assert.True(settings.CanDuplex);
        Assert.True(settings.SupportsColor);

        settings.PrinterName = "Basalt nonexistent printer 7F646125";
        Assert.False(settings.IsValid);
        Assert.False(settings.IsDefaultPrinter);
    }

    /// <summary>
    /// Proves the fix for the discovery cache: every property below reads
    /// from the same selected printer, so after the first read neither the
    /// fake spooler snapshot nor the fake capabilities lookup runs again.
    /// Without PrinterSettings caching its own discovery, this fails because
    /// PrinterName, IsDefaultPrinter, IsValid, CanDuplex and SupportsColor
    /// each re-ran discovery on every read.
    /// </summary>
    [Fact]
    public void Printer_settings_cache_discovery_until_the_printer_name_changes()
    {
        var snapshotCalls = 0;
        var capabilityCalls = 0;
        using var fake = FakeDiscovery.Install(
            () =>
            {
                snapshotCalls++;
                return new SystemPrinterDiscovery.Snapshot(["Office", "Warehouse"], "Office");
            },
            name =>
            {
                capabilityCalls++;
                return new PrinterCapabilities(true, true, [new PaperSize("A4", 827, 1169)], [new PrinterResolution { Kind = PrinterResolutionKind.Custom, X = 600, Y = 600 }]);
            });

        var settings = new PrinterSettings();

        _ = settings.PrinterName;
        _ = settings.IsDefaultPrinter;
        _ = settings.IsValid;
        _ = settings.CanDuplex;
        _ = settings.SupportsColor;
        _ = settings.PaperSizes;
        _ = settings.PrinterResolutions;

        Assert.Equal(1, snapshotCalls);
        Assert.Equal(1, capabilityCalls);

        // Reading again, still on "Office", must not trigger new discovery.
        _ = settings.IsValid;
        _ = settings.CanDuplex;
        Assert.Equal(1, snapshotCalls);
        Assert.Equal(1, capabilityCalls);

        // Changing the printer invalidates both caches (System.Drawing
        // semantics: values reflect the currently selected printer).
        settings.PrinterName = "Warehouse";
        _ = settings.IsValid;
        _ = settings.CanDuplex;

        Assert.Equal(2, snapshotCalls);
        Assert.Equal(2, capabilityCalls);
    }

    /// <summary>Test-only seam over the two internal discovery hooks, restored on dispose.</summary>
    private sealed class FakeDiscovery : IDisposable
    {
        public static FakeDiscovery Install(SystemPrinterDiscovery.Snapshot snapshot, Func<string, PrinterCapabilities> capabilities) =>
            Install(() => snapshot, capabilities);

        public static FakeDiscovery Install(Func<SystemPrinterDiscovery.Snapshot> snapshot, Func<string, PrinterCapabilities> capabilities)
        {
            SystemPrinterDiscovery.DiscoveryOverride = snapshot;
            PrinterCapabilities.DiscoveryOverride = capabilities;
            return new FakeDiscovery();
        }

        public void Dispose()
        {
            SystemPrinterDiscovery.DiscoveryOverride = null;
            PrinterCapabilities.DiscoveryOverride = null;
        }
    }

    [Fact]
    public void Cups_options_report_actual_paper_resolution_duplex_and_color_choices()
    {
        var capabilities = CupsPrinterCapabilities.Parse(
            "PageSize/Media Size: *iso_a4_210x297mm na_letter_8.5x11in\n" +
            "Resolution/Resolution: *300dpi 600x1200dpi\n" +
            "Duplex/Two-Sided Printing: *None DuplexNoTumble DuplexTumble\n" +
            "ColorModel/Color Mode: *Gray RGB\n");

        Assert.True(capabilities.CanDuplex);
        Assert.True(capabilities.SupportsColor);
        Assert.Equal(["iso_a4_210x297mm", "na_letter_8.5x11in"], capabilities.PaperSizes.Select(p => p.PaperName));
        Assert.Equal([PaperKind.A4, PaperKind.Letter], capabilities.PaperSizes.Select(p => p.Kind));
        Assert.Equal([(827, 1169), (850, 1100)], capabilities.PaperSizes.Select(p => (p.Width, p.Height)));
        Assert.Equal([(300, 300), (600, 1200)], capabilities.Resolutions.Select(r => (r.X, r.Y)));
    }

    [Fact]
    public void Missing_cups_options_remain_unknown_instead_of_claiming_hardware_support()
    {
        var capabilities = CupsPrinterCapabilities.Parse("PageSize/Media Size: *A4 Letter\n");

        Assert.Null(capabilities.CanDuplex);
        Assert.Null(capabilities.SupportsColor);
        Assert.Equal([PaperKind.A4, PaperKind.Letter], capabilities.PaperSizes.Select(p => p.Kind));
        Assert.Empty(capabilities.Resolutions);
    }

    [Fact]
    public void Windows_driver_paper_dimensions_use_hundredths_of_an_inch()
    {
        Assert.Equal(827, WindowsPrinterCapabilities.TenthsMillimeterToHundredthsInch(2100));
        Assert.Equal(1169, WindowsPrinterCapabilities.TenthsMillimeterToHundredthsInch(2970));
    }
}
