using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Basalt.Core.Settings;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace Basalt.Tests;

/// <summary>
/// The chrome Dock draws round every panel: title bars, tab strips, surfaces.
///
/// App.axaml used to include only Dock's VsCodeLight preset, a set of
/// fixed-colour brushes, so in every dark theme the tool panels, their
/// title bars and tabs stayed pale with near-invisible text. These tests
/// rasterise the real window and read pixels, because a brush resolving to
/// the right colour says nothing about what reaches the screen.
/// </summary>
public sealed class DockThemeChromeTests
{
    /// <summary>
    /// A small solution on disk: without one the window is the welcome
    /// screen and no panel is ever built, which is how chrome problems went
    /// unseen by tests that only built the empty window.
    /// </summary>
    private static string WriteSolution()
    {
        var root = Path.Combine(Path.GetTempPath(), "basalt-chrome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "App.vbproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Library</OutputType>"
            + "<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root, "Module1.vb"), "Module Module1\nEnd Module\n");
        return Path.Combine(root, "App.vbproj");
    }

    private static IdeDockFactory FactoryOf(TestWindow host) =>
        (IdeDockFactory)typeof(MainWindow)
            .GetField("_factory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(host.Window)!;

    private static Color Token(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var value), key);
        return (Color)value!;
    }

    /// <summary>The title bar's border: the strip across the top of a tool panel.</summary>
    private static Border TitleBarOf(ToolChromeControl chrome) =>
        // Not the first PART_Border in the chrome: the tab strip inside it has one too.
        chrome.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "PART_Grip")
            .GetVisualAncestors().OfType<Border>().First(b => b.Name == "PART_Border");

    private static ToolChromeControl WidestChrome(TestWindow host) =>
        host.Window.GetVisualDescendants().OfType<ToolChromeControl>()
            .OrderByDescending(c => c.Bounds.Width).First();

    /// <summary>
    /// Sizes the window and lets Dock build. The caller holds a
    /// <see cref="ThemeScope"/> for the variant, so it is put back afterwards.
    /// </summary>
    private static async Task ShowAsync(TestWindow host)
    {
        host.Window.Width = 1440;
        host.Window.Height = 900;
        await host.SettleAsync(8);
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task TheToolTitleBarAndItsTextFollowTheTheme(string name)
    {
        var variant = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        using var theme = new ThemeScope(variant);
        using var host = new TestWindow(WriteSolution());
        await ShowAsync(host);

        var chrome = WidestChrome(host);
        var bar = TitleBarOf(chrome);
        var title = chrome.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "PART_Title");
        var raster = Raster.Of(host.Window);

        var barBounds = Raster.BoundsIn(bar, host.Window);
        // Away from the title on the left and the buttons on the right.
        var sample = raster.At(barBounds.X + barBounds.Width / 2, barBounds.Y + 4);

        Assert.True(Raster.Near(sample, Token("TitleBarBackground", variant)),
            $"{name}: the title bar is drawn {sample}, not the theme's title bar colour.");

        var textBounds = Raster.BoundsIn(title, host.Window);
        var contrast = raster.BestContrastAgainst(textBounds, sample);

        Assert.True(contrast >= 4.5,
            $"{name}: the title text reaches only {contrast:F1}:1 against its bar.");

        // The pale chrome this replaces: a dark theme's bar must be dark.
        if (name == "Dark") Assert.True(Raster.Luminance(sample) < 0.1, $"Dark title bar is {sample}.");
        else Assert.True(Raster.Luminance(sample) > 0.6, $"Light title bar is {sample}.");
    }

    [AvaloniaFact]
    public async Task TheTitleBarBackgroundIsPaintedByTheStyleFromTheThemesBrush()
    {
        // The test above compares a pixel with a token, which Dock's own
        // template could also satisfy. Here the theme's brushes are replaced
        // by colours nothing else uses: the bar can only show them if the
        // style in Styles.axaml matched the template's border and resolved them.
        using var scope = new ThemeScope(ThemeVariant.Light);
        using var host = new TestWindow(WriteSolution());
        await ShowAsync(host);

        var idle = Color.FromRgb(0xFF, 0x00, 0xFF);
        var active = Color.FromRgb(0x00, 0xFF, 0xFF);
        host.Window.Resources["DockSurfaceHeaderBrush"] = new SolidColorBrush(idle);
        host.Window.Resources["DockSurfaceHeaderActiveBrush"] = new SolidColorBrush(active);
        await host.SettleAsync(4);

        var raster = Raster.Of(host.Window);
        var chromes = host.Window.GetVisualDescendants().OfType<ToolChromeControl>()
            .Where(c => c.Bounds.Width > 100).ToList();

        Assert.NotEmpty(chromes);

        foreach (var chrome in chromes)
        {
            var border = TitleBarOf(chrome);
            var bounds = Raster.BoundsIn(border, host.Window);
            var expected = chrome.Classes.Contains(":active") ? active : idle;

            Assert.True(Raster.Near(raster.At(bounds.X + bounds.Width / 2, bounds.Y + 4), expected),
                $"A title bar ({(chrome.Classes.Contains(":active") ? "active" : "idle")}) is not painted from its theme brush (Dock paints its grip over the border unless the style clears it).");
        }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task TheFocusedPanelAndTheSelectedTabCarryAnAccent(string name)
    {
        var variant = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        using var theme = new ThemeScope(variant);
        using var host = new TestWindow(WriteSolution());
        await ShowAsync(host);

        var factory = FactoryOf(host);
        factory.SetActiveDockable(factory.Problems);
        factory.SetFocusedDockable(factory.Problems.Owner as IDock, factory.Problems);
        await host.SettleAsync(6);

        var accent = Token("TabActiveBorderTop", variant);
        var raster = Raster.Of(host.Window);

        var active = host.Window.GetVisualDescendants().OfType<ToolChromeControl>()
            .Single(c => c.Classes.Contains(":active"));
        var bar = Raster.BoundsIn(TitleBarOf(active), host.Window);

        Assert.True(Raster.Near(raster.At(bar.X + bar.Width / 2, bar.Bottom - 1), accent),
            $"{name}: no accent line under the focused panel's title.");

        var others = host.Window.GetVisualDescendants().OfType<ToolChromeControl>()
            .Where(c => !c.Classes.Contains(":active") && c.Bounds.Width > 300);

        foreach (var other in others)
        {
            var b = Raster.BoundsIn(TitleBarOf(other), host.Window);
            Assert.False(Raster.Near(raster.At(b.X + b.Width / 2, b.Bottom - 1), accent),
                $"{name}: a panel without focus also shows the accent.");
        }

        var selected = host.Window.GetVisualDescendants().OfType<ToolTabStripItem>()
            .First(t => t.Classes.Contains(":selected") && t.Bounds.Width > 40);
        var tab = Raster.BoundsIn(selected, host.Window);

        Assert.True(Raster.Near(raster.At(tab.X + tab.Width / 2, tab.Y), accent),
            $"{name}: the selected tab has no accent bar.");
    }

    [AvaloniaFact]
    public async Task EveryThemeKeepsChromeTextReadable()
    {
        using var scope = new ThemeScope(ThemeVariant.Light);
        using var host = new TestWindow(WriteSolution());
        await ShowAsync(host);

        var themes = new[]
        {
            ThemeVariant.Light, ThemeVariant.Dark, IdeThemes.VisualStudioBlue,
            IdeThemes.HighContrastLight, IdeThemes.HighContrastDark,
            IdeThemes.SolarizedLight, IdeThemes.SolarizedDark
        };

        var low = new List<string>();

        foreach (var variant in themes)
        {
            Application.Current!.RequestedThemeVariant = variant;
            await host.SettleAsync(4);

            var chrome = WidestChrome(host);
            var bar = (ISolidColorBrush)TitleBarOf(chrome).Background!;
            var title = chrome.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "PART_Title");
            Check(low, variant, "title", ((ISolidColorBrush)title.Foreground!).Color, bar.Color);

            var item = host.Window.GetVisualDescendants().OfType<ToolTabStripItem>()
                .First(t => !t.Classes.Contains(":selected") && t.Bounds.Width > 20);
            var strip = (ISolidColorBrush)Application.Current.FindResource("DockSurfacePanelBrush")!;
            Check(low, variant, "tab", ((ISolidColorBrush)item.Foreground!).Color, strip.Color);

            var selected = host.Window.GetVisualDescendants().OfType<ToolTabStripItem>()
                .First(t => t.Classes.Contains(":selected") && t.Bounds.Width > 20);
            Check(low, variant, "selected tab", ((ISolidColorBrush)selected.Foreground!).Color,
                ((ISolidColorBrush)selected.Background!).Color);

            // And what reached the screen: the brushes above can be right
            // while a style or a template draws something else over them.
            var raster = Raster.Of(host.Window);
            var barBounds = Raster.BoundsIn(TitleBarOf(chrome), host.Window);
            var barPixel = raster.At(barBounds.X + barBounds.Width / 2, barBounds.Y + 4);
            var titleArea = Raster.BoundsIn(title, host.Window);
            var drawn = raster.BestContrastAgainst(titleArea, barPixel);
            if (drawn < 4.5) low.Add($"{variant}: the title is drawn at only {drawn:F1}:1 on its bar ({barPixel})");

            var tabArea = Raster.BoundsIn(selected, host.Window);
            var tabLabel = Raster.BoundsIn(
                selected.GetVisualDescendants().OfType<TextBlock>().First(), host.Window);
            var tabPixel = raster.At(tabArea.X + 2, tabArea.Y + tabArea.Height / 2);
            var tabDrawn = raster.BestContrastAgainst(tabLabel, tabPixel);
            if (tabDrawn < 4.5) low.Add($"{variant}: the selected tab is drawn at only {tabDrawn:F1}:1 ({tabPixel})");
        }

        Assert.True(low.Count == 0, string.Join("\n", low));
    }

    private static void Check(List<string> low, ThemeVariant variant, string what, Color text, Color on)
    {
        var contrast = Raster.Contrast(text, on);
        if (contrast < 4.5) low.Add($"{variant}: {what} text is {contrast:F1}:1 ({text} on {on})");
    }

    [AvaloniaFact]
    public async Task NoTabIsCutAtTheEdgeOfItsStrip()
    {
        // "Outli" and "oIbox" in the narrow left column: the tabs were laid
        // out at their natural width and the strip clipped the last glyphs.
        using var scope = new ThemeScope(ThemeVariant.Dark);
        using var host = new TestWindow(WriteSolution());
        await ShowAsync(host);

        var items = host.Window.GetVisualDescendants().OfType<ToolTabStripItem>()
            .Where(i => i.IsEffectivelyVisible && i.Bounds.Width > 0).ToList();

        Assert.NotEmpty(items);

        foreach (var item in items)
        {
            var strip = item.GetVisualAncestors().OfType<ToolTabStrip>().First();
            var tab = Raster.BoundsIn(item, host.Window);
            var stripBounds = Raster.BoundsIn(strip, host.Window);
            var label = item.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.GetVisualAncestors().OfType<ContentPresenter>().Any(p => p.Name == "PART_HeaderPresenter"));
            var labelBounds = Raster.BoundsIn(label, host.Window);

            Assert.True(tab.Right <= stripBounds.Right + 1,
                $"'{label.Text}' tab ends at {tab.Right}, past its strip at {stripBounds.Right}.");

            // The text is either whole or ends in an ellipsis, never cut:
            // TextBlock draws the ellipsis only when it has been given less
            // than it wants, so the label's box has to sit inside the tab.
            Assert.True(labelBounds.Right <= tab.Right,
                $"'{label.Text}' runs {labelBounds.Right - tab.Right}px beyond its tab.");

            Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
            Assert.Equal((item.DataContext as IDockable)?.Title, ToolTip.GetTip(item));
        }
    }

    [Fact]
    public void ANarrowStripTrimsTheWideTabsAndLeavesTheShortOnesAlone()
    {
        var shared = ShrinkingTabPanel.Share([100, 40, 100], 200);

        Assert.True(shared.Sum() <= 200.5, $"The tabs still add up to {shared.Sum()}.");
        Assert.Equal(40, shared[1]);
        Assert.True(shared[0] < 100);
        Assert.Equal(shared[0], shared[2], 0.5);
    }

    [Fact]
    public void WithRoomEveryTabKeepsItsOwnWidth() =>
        Assert.Equal([60.0, 80.0, 50.0], ShrinkingTabPanel.Share([60, 80, 50], 400));

    [AvaloniaTheory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.VisualStudioBlue)]
    [InlineData(AppTheme.HighContrastLight)]
    [InlineData(AppTheme.HighContrastDark)]
    [InlineData(AppTheme.SolarizedLight)]
    [InlineData(AppTheme.SolarizedDark)]
    public void EveryDockBrushIsDefinedByTheTheme(AppTheme theme)
    {
        // Dock reads these keys by name; one the theme forgets falls back to
        // nothing, and the chrome loses that colour silently.
        var preset = (ResourceDictionary)new ResourceInclude(new Uri("avares://Basalt.Shell/"))
        {
            Source = new Uri("avares://Dock.Avalonia.Themes.Fluent/Presets/Ide/VsCodeLight.axaml")
        }.Loaded;

        var variant = IdeThemes.VariantFor(theme)!;
        var missing = preset.Keys
            .Select(k => k.ToString()!)
            .Where(k => !Application.Current!.TryGetResource(k, variant, out _))
            .ToList();

        Assert.True(missing.Count == 0, $"{theme} does not define: {string.Join(", ", missing)}");
    }

    [AvaloniaFact]
    public void ThePanelMetricsInCodeAndInMarkupAgree()
    {
        // Code-built panels read PanelMetrics; markup reads the resources.
        // Two sources that drift would put a panel's text a size off its
        // neighbour's without either being wrong on its own.
        var app = Application.Current!;

        Assert.True(app.TryGetResource("PanelFontSize", ThemeVariant.Light, out var font));
        Assert.True(app.TryGetResource("PanelSecondaryFontSize", ThemeVariant.Light, out var small));
        Assert.True(app.TryGetResource("PanelRowHeight", ThemeVariant.Light, out var row));

        Assert.Equal(PanelMetrics.FontSize, (double)font!);
        Assert.Equal(PanelMetrics.SecondaryFontSize, (double)small!);
        Assert.Equal(PanelMetrics.RowHeight, (double)row!);
    }
}
