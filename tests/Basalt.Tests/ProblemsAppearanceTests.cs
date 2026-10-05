using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using Basalt.Core.Model;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class ProblemsAppearanceTests
{
    [AvaloniaFact]
    public void ProblemsHasCompactRowsAndSeverityIcons()
    {
        using var vm = new MainWindowViewModel();
        vm.Diagnostics.Add(new("BC1", "Error", DiagnosticSeverity.Error, "Form.vb", 1, 1));
        var panel = new ProblemsPanel(vm, _ => { });
        var grid = Assert.IsType<DataGrid>(panel.Content);
        Assert.Equal(PanelMetrics.FontSize, grid.FontSize);
        Assert.Equal(24, grid.RowHeight);
        Assert.IsType<DataGridTemplateColumn>(grid.Columns[0]);
    }

    /// <summary>
    /// A near-opaque "#FFF1F0"/"#FFFBE5" row background reads as a pale tint
    /// on a white row, but it paints the same near-white rectangle on a dark
    /// theme's row too, hiding whatever the theme put there instead of
    /// tinting it. This renders the real panel in both themes and reads its
    /// pixels, rather than trusting the brush's own declared colour: the
    /// error and warning rows must still show a reddish or amber tint, and on
    /// a dark theme that tint must stay dark rather than wash out to white.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ErrorAndWarningBackgroundsStayTintedAndReadableInEveryTheme(bool dark)
    {
        var application = Application.Current!;
        var previousVariant = application.RequestedThemeVariant;

        // The IDE switches themes by setting this on the application (see
        // ChromeInWindowTests), not on an individual window: that is what
        // makes the dynamic resources behind the panel's own background
        // follow the theme, which is the surface the diagnostic tint blends
        // with.
        application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            using var vm = new MainWindowViewModel();
            vm.Diagnostics.Add(new("BC1", "Error", DiagnosticSeverity.Error, "Form.vb", 1, 1));
            vm.Diagnostics.Add(new("BC2", "Warning", DiagnosticSeverity.Warning, "Form.vb", 2, 1));
            var window = new Window { Width = 900, Height = 200, Content = new ProblemsPanel(vm, _ => { }) };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                using var bitmap = new RenderTargetBitmap(new PixelSize(900, 200));
                bitmap.Render(window);
                var pixels = new byte[900 * 200 * 4];
                var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try { bitmap.CopyPixels(new PixelRect(0, 0, 900, 200), pinned.AddrOfPinnedObject(), pixels.Length, 900 * 4); }
                finally { pinned.Free(); }

                var errorPixels = 0;
                var warningPixels = 0;
                var errorMaxChannel = 0;
                var warningMaxChannel = 0;
                var stride = 900 * 4;
                for (var offset = 0; offset < pixels.Length; offset += 4)
                {
                    // Columns 0-36 hold the severity icon: a solid Firebrick
                    // or Goldenrod glyph, anti-aliased against the row, which
                    // is a second source of red/amber pixels that has nothing
                    // to do with the row's own tinted background. Reading the
                    // row colour from the wider columns beside it avoids
                    // mistaking the icon's edge for the tint under test.
                    if (offset % stride < 40 * 4) continue;

                    // One of index 0/2 is red and the other blue depending on
                    // whether the surface is BGRA or RGBA; index 1 (green)
                    // sits in the same place either way. The tint is read
                    // from the gap between channels rather than an assumed
                    // channel order, so it does not depend on that format.
                    var ambiguousOne = pixels[offset];
                    var green = pixels[offset + 1];
                    var ambiguousTwo = pixels[offset + 2];
                    var high = Math.Max(ambiguousOne, ambiguousTwo);
                    var low = Math.Min(ambiguousOne, ambiguousTwo);

                    // A red tint: green sits with the lower of the two
                    // ambiguous channels, well below the higher (red) one.
                    if (high - green > 30 && high - low > 10 && green <= low + 15)
                    {
                        errorPixels++;
                        errorMaxChannel = Math.Max(errorMaxChannel, Math.Max(high, green));
                    }
                    // An amber tint: green sits close to the higher (red)
                    // channel instead, well above the lower (blue) one.
                    else if (high - low > 30 && green - low > 20 && high - green < 30)
                    {
                        warningPixels++;
                        warningMaxChannel = Math.Max(warningMaxChannel, Math.Max(high, green));
                    }
                }

                Assert.True(errorPixels > 100, "The error row must have a visible red-tinted background.");
                Assert.True(warningPixels > 100, "The warning row must have a visible amber-tinted background.");

                if (dark)
                {
                    Assert.True(errorMaxChannel < 180,
                        "On a dark theme the error row tint must stay dark, not wash out to near-white.");
                    Assert.True(warningMaxChannel < 180,
                        "On a dark theme the warning row tint must stay dark, not wash out to near-white.");
                }
                else
                {
                    Assert.True(errorMaxChannel > 200, "On a light theme the error row tint should stay pale.");
                    Assert.True(warningMaxChannel > 200, "On a light theme the warning row tint should stay pale.");
                }
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            application.RequestedThemeVariant = previousVariant;
        }
    }
}
