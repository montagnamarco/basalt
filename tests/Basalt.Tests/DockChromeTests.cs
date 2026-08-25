using Avalonia.Controls;
using Shape = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// The buttons on a panel's title bar, and the icon beside the panel.
///
/// The buttons looked like blank grey squares, and an earlier guess said
/// Dock's theme drew nothing in them. Measuring proved otherwise: it draws a
/// Path with real geometry, 2048 by 2048 inside a button 20 by 26, because
/// nothing tells it to shrink.
/// </summary>
public class DockChromeTests
{
    private static IReadOnlyList<Button> ChromeButtons(TestWindow host) =>
        [.. host.Window.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.Name is "PART_PinButton" or "PART_MenuButton"
                              or "PART_CloseButton" or "PART_MaximizeRestoreButton")];

    [AvaloniaFact]
    public async Task TheTitleBarButtonsAreThere()
    {
        using var host = new TestWindow();
        await host.SettleAsync();

        Assert.NotEmpty(ChromeButtons(host));
    }

    [AvaloniaFact]
    public async Task TheirGlyphsFitInsideThem()
    {
        using var host = new TestWindow();
        await host.SettleAsync();

        var oversized = new List<string>();

        foreach (var button in ChromeButtons(host))
        {
            var path = button.GetVisualDescendants().OfType<Shape>().FirstOrDefault();

            if (path is null) continue;

            // A glyph wider than its button is a glyph nobody can read; the
            // ones here measured 2048 across.
            if (path.Bounds.Width > 20 || path.Bounds.Height > 20)
                oversized.Add($"{button.Name}: {path.Bounds.Width:0}x{path.Bounds.Height:0}");
        }

        Assert.True(oversized.Count == 0,
            "These glyphs do not fit their buttons:\n" + string.Join("\n", oversized));
    }

    [AvaloniaFact]
    public async Task EachGlyphIsActuallyDrawn()
    {
        // Fitting is not enough: a Path scaled to nothing also fits.
        using var host = new TestWindow();
        await host.SettleAsync();

        // Every button, not just one: NotEmpty passed while three of the four
        // drew nothing at all, which is exactly how the icons went missing
        // again without a test noticing.
        var missing = new List<string>();

        foreach (var button in ChromeButtons(host))
        {
            // A button that was never realised has no glyph because it has no
            // template yet: maximise only appears on a floating window, and
            // asking a docked panel for it proves nothing.
            if (!button.IsEffectivelyVisible) continue;
            if (button.GetVisualDescendants().Count() == 0) continue;

            var path = button.GetVisualDescendants().OfType<Shape>().FirstOrDefault();

            if (path is null)
            {
                missing.Add($"{button.Name}: no glyph at all");
                continue;
            }

            if (path.Bounds.Width < 6 || path.Bounds.Height < 6)
                missing.Add($"{button.Name}: {path.Bounds.Width:0}x{path.Bounds.Height:0}");
        }

        Assert.True(missing.Count == 0,
            "These buttons draw nothing readable:\n" + string.Join("\n", missing));
    }

    [AvaloniaFact]
    public async Task ThePanelShowsItsOwnIconWithoutRepeatingItsName()
    {
        // Dock's tab already carries the name; the header repeating it read
        // as "Solution Explorer" twice, one line above the other.
        using var host = new TestWindow();
        await host.SettleAsync();

        var header = host.Window.GetVisualDescendants().OfType<PanelHeader>().First();

        Assert.NotEmpty(header.GetVisualDescendants().OfType<IconView>());

        var titles = header.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text ?? "")
            .Where(t => t.Contains("Explorer", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(titles);
    }
}
