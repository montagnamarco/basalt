using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Basalt.Core.Settings;
using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>
/// The two start page buttons have room for a two-line description, with even padding.
///
/// "New Project…" wraps its description onto a second line and "Open Project or
/// Solution…" does not, so the two buttons were different heights and the
/// wrapped line sat tight against the blue button's bottom edge. Both buttons
/// now reserve two description lines and pad evenly. These tests read the
/// rendered pixels at the default 1440 by 900 window and a narrow 1024 wide one.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class StartPageButtonFitTests : IDisposable
{
    private static readonly string[] ButtonNames =
    [
        "WelcomeNewProjectButton",
        "WelcomeOpenProjectButton"
    ];

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-start-fit-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public StartPageButtonFitTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);
        try { File.Delete(_settings); } catch (IOException) { }
    }

    /// <summary>The first and last rows in the area where something other than the background is drawn.</summary>
    private static (int First, int Last) InkRows(Raster raster, PixelRect area, Color background)
    {
        int first = -1, last = -1;

        for (var y = area.Y; y < area.Bottom; y++)
            for (var x = area.X; x < area.Right; x++)
                if (Raster.Contrast(raster.At(x, y), background) > 1.8)
                {
                    if (first < 0) first = y;
                    last = y;
                    break;
                }

        return (first, last);
    }

    [AvaloniaTheory]
    [InlineData(1440, 900)]
    [InlineData(1024, 700)]
    public async Task BothButtonsReserveTwoLinesAndPadEvenly(int width, int height)
    {
        using var host = new TestWindow();
        host.Window.Width = width;
        host.Window.Height = height;
        await host.SettleAsync(8);

        var problems = new List<string>();
        var boxes = new List<PixelRect>();
        var raster = Raster.Of(host.Window);

        foreach (var name in ButtonNames)
        {
            var button = host.Window.FindControl<Button>(name)!;
            var box = Raster.BoundsIn(button, host.Window);
            boxes.Add(box);

            var textName = name == "WelcomeNewProjectButton"
                ? "WelcomeNewProjectDescription" : "WelcomeOpenProjectDescription";
            var words = Raster.BoundsIn(host.Window.FindControl<TextBlock>(textName)!, host.Window);

            // The description's own arranged box must hold the lines it wraps to.
            var text = host.Window.FindControl<TextBlock>(textName)!;
            text.Measure(new Size(text.Bounds.Width, double.PositiveInfinity));
            if (words.Height + 0.5 < text.DesiredSize.Height)
                problems.Add($"{name}: the description needs {text.DesiredSize.Height:F0}px, is given {words.Height}px.");

            // What reached the screen: the ink above and below the content.
            var background = raster.At(box.X + 3, box.Y + box.Height / 2);
            var ink = InkRows(raster, new PixelRect(box.X + 6, box.Y, box.Width - 12, box.Height), background);
            var topGap = ink.First - box.Y;
            var bottomGap = box.Bottom - 1 - ink.Last;

            if (topGap < 6 || bottomGap < 6)
                problems.Add($"{name} at {width}px: ink sits {topGap}px from the top and {bottomGap}px from the bottom of a {box.Height}px button.");

            // Even padding is a fact of the markup: a one-line description
            // leaves more room below it than above, by design, so the ink is
            // not compared; the padding the button declares is.
            if (button.Padding.Top != button.Padding.Bottom)
                problems.Add($"{name}: its padding is uneven ({button.Padding.Top} above, {button.Padding.Bottom} below).");
        }

        // Both buttons reserve the same two lines, so they are the same height.
        if (boxes[0].Height != boxes[1].Height)
            problems.Add($"The buttons differ in height ({boxes[0].Height}px and {boxes[1].Height}px); the one with one line of description is shorter.");

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <remarks>
    /// The global TextBlock style set the editor's text colour explicitly, so the
    /// blue primary button's title was near-black on blue. Reintroduced once (the
    /// Button.primary TextBlock rule removed): this test failed.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheBlueButtonsTitleIsReadableOnItsBlue()
    {
        using var host = new TestWindow();
        host.Window.Width = 1440;
        host.Window.Height = 900;
        await host.SettleAsync(8);

        var button = host.Window.FindControl<Button>("WelcomeNewProjectButton")!;
        var title = button.GetVisualDescendants().OfType<TextBlock>().First(t => t.FontWeight == FontWeight.SemiBold);
        var raster = Raster.Of(host.Window);
        var box = Raster.BoundsIn(button, host.Window);
        var background = raster.At(box.X + 3, box.Y + 3);
        var words = Raster.BoundsIn(title, host.Window);

        // The glyphs' own colour: the one most of the title's visibly different pixels share.
        var counts = new Dictionary<Color, int>();
        for (var y = words.Y; y < words.Bottom; y++)
            for (var x = words.X; x < words.Right; x++)
            {
                var pixel = raster.At(x, y);
                if (Raster.Contrast(pixel, background) >= 1.5) counts[pixel] = counts.GetValueOrDefault(pixel) + 1;
            }
        Assert.NotEmpty(counts);
        var ink = counts.MaxBy(pair => pair.Value).Key;

        var contrast = Raster.Contrast(ink, background);
        Assert.True(contrast >= 4.5, $"the title is {ink} on {background}: {contrast:0.00}:1, below 4.5:1");
    }
}
