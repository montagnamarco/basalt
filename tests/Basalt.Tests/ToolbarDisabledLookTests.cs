using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// A disabled toolbar button must look faded, not pressed.
///
/// With nothing open, Continue, Pause, the stepping buttons and Stop are
/// disabled. Fluent paints a disabled button's content grey, so they read as
/// buttons that are on. The test rasterises the real window: the brushes
/// alone say nothing about what the template draws over them.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class ToolbarDisabledLookTests : IDisposable
{
    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-toolbar-look-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public ToolbarDisabledLookTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);
        try { File.Delete(_settings); } catch (IOException) { }
    }

    /// <summary>The strongest departure from the background any pixel in the area shows.</summary>
    private static double Strongest(Raster raster, PixelRect area, Color background)
    {
        var best = 0.0;

        for (var y = area.Y; y < area.Bottom; y++)
            for (var x = area.X; x < area.Right; x++)
            {
                var pixel = raster.At(x, y);
                best = Math.Max(best, Math.Abs(pixel.R - background.R)
                    + Math.Abs(pixel.G - background.G) + Math.Abs(pixel.B - background.B));
            }

        return best;
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task ADisabledToolbarButtonHasNoFillAndItsIconIsDimmed(string name)
    {
        var variant = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        using var theme = new ThemeScope(variant);
        using var host = new TestWindow();
        host.Window.Width = 1440;
        host.Window.Height = 900;
        await host.SettleAsync(8);

        var toolbarHost = host.Window.FindControl<Border>("ToolbarHost")!;
        var toolbar = toolbarHost.GetVisualDescendants().OfType<IdeToolbar>().Single();
        var continueTip = Localizer.Get(StringKeys.MenuDebugContinue);
        var button = toolbar.GetVisualDescendants().OfType<Button>()
            .Single(b => ToolTip.GetTip(b) as string == continueTip);

        // Nothing is open, so there is nothing to continue.
        Assert.False(button.IsEnabled);

        var raster = Raster.Of(host.Window);
        var hostBounds = Raster.BoundsIn(toolbarHost, host.Window);

        // The toolbar's own colour, sampled on its empty right-hand side.
        var background = raster.At(hostBounds.Right - 6, hostBounds.Y + 2);

        var box = Raster.BoundsIn(button, host.Window);
        var padding = raster.At(box.X + 3, box.Y + box.Height / 2);

        Assert.True(Raster.Near(padding, background),
            $"{name}: a disabled Continue button is filled {padding} on a {background} toolbar; it reads as pressed.");

        var icon = Raster.BoundsIn(button.GetVisualDescendants().OfType<IconView>().First(), host.Window);
        var dimmed = Strongest(raster, icon, background);

        // The same icon with the button enabled, to measure full strength
        // against. Only the test changes IsEnabled; the button's own state is
        // restored straight after the measurement.
        button.IsEnabled = true;
        await host.SettleAsync(3);
        var full = Strongest(Raster.Of(host.Window), icon, background);
        button.IsEnabled = false;
        await host.SettleAsync(3);

        Assert.True(full > 0, $"{name}: the Continue icon was not drawn at all.");
        Assert.True(dimmed > 0, $"{name}: the disabled Continue icon vanished.");
        Assert.True(dimmed < full * 0.6,
            $"{name}: the disabled Continue icon is as strong ({dimmed}) as an enabled one ({full}); it should be faded.");
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task AnEnabledToolbarButtonKeepsItsOwnLook(string name)
    {
        var variant = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        using var theme = new ThemeScope(variant);
        using var host = new TestWindow();
        host.Window.Width = 1440;
        host.Window.Height = 900;
        await host.SettleAsync(8);

        var toolbarHost = host.Window.FindControl<Border>("ToolbarHost")!;
        var toolbar = toolbarHost.GetVisualDescendants().OfType<IdeToolbar>().Single();
        var newTip = Localizer.Get(StringKeys.MenuFileNewSolution);
        var button = toolbar.GetVisualDescendants().OfType<Button>()
            .Single(b => ToolTip.GetTip(b) as string == newTip);

        Assert.True(button.IsEnabled);

        var raster = Raster.Of(host.Window);
        var hostBounds = Raster.BoundsIn(toolbarHost, host.Window);
        var background = raster.At(hostBounds.Right - 6, hostBounds.Y + 2);

        // An enabled button sits on the toolbar with no fill: the disabled
        // rule must not have leaked onto it, and the icon is at full strength.
        var box = Raster.BoundsIn(button, host.Window);
        Assert.True(Raster.Near(raster.At(box.X + 3, box.Y + box.Height / 2), background),
            $"{name}: an enabled New button is filled instead of sitting on the toolbar.");

        var icon = Raster.BoundsIn(button.GetVisualDescendants().OfType<IconView>().First(), host.Window);
        Assert.True(Strongest(raster, icon, background) > 150,
            $"{name}: the enabled New icon is drawn too faintly.");
    }
}
