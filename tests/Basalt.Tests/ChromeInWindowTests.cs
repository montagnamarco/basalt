using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// The window's own chrome: toolbar, icons, and the themes.
///
/// Built through the real window rather than by constructing the controls
/// directly, because what these check is that the window puts them where they
/// can be seen — twice now a control has been correct and invisible.
/// </summary>
public class ChromeInWindowTests
{
    [AvaloniaFact]
    public async Task PutsAToolbarInTheWindow()
    {
        using var host = new TestWindow();
        await host.SettleAsync();

        Assert.Single(host.Window.GetVisualDescendants().OfType<IdeToolbar>());
    }

    [AvaloniaFact]
    public async Task FillsTheToolbarWithActions()
    {
        using var host = new TestWindow();
        await host.SettleAsync();

        var toolbar = host.Window.GetVisualDescendants().OfType<IdeToolbar>().Single();

        Assert.True(toolbar.ButtonCount >= 10, $"Only {toolbar.ButtonCount} buttons.");
    }

    [AvaloniaFact]
    public async Task DrawsIconsRatherThanEmoji()
    {
        // An emoji renders differently on each of the three platforms this
        // runs on; the drawn icons do not.
        using var host = new TestWindow();
        await host.SettleAsync();

        Assert.NotEmpty(host.Window.GetVisualDescendants().OfType<IconView>());
    }

    [AvaloniaFact]
    public async Task PutsTheAssistantAndTestWindowsInTheLayout()
    {
        // Registering a dockable is not the same as it appearing: a tool with
        // no content is an empty tab, which reads as a broken feature.
        using var host = new TestWindow();
        await host.SettleAsync();

        var titles = host.Window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .Where(t => t is not null)
            .ToList();

        Assert.Contains("Assistant", titles);
        Assert.Contains("Tests", titles);
    }

    [AvaloniaFact]
    public async Task ShowsTheStatusBarFields()
    {
        using var host = new TestWindow();
        await host.SettleAsync();

        var named = host.Window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => t.Name is "CaretPositionLabel" or "EncodingLabel" or "LineEndingLabel")
            .ToList();

        Assert.Equal(3, named.Count);
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task ResolvesEveryBrushInBothThemes(string variant)
    {
        // An unresolved brush is what shows as black on black; the dark theme
        // is the one nobody looks at until it is wrong.
        using var host = new TestWindow();
        await host.SettleAsync();

        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant =
                variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        await host.SettleAsync();

        string[] brushes =
        [
            "EditorBackgroundBrush", "EditorForegroundBrush", "SideBarBackgroundBrush",
            "StatusBarBackgroundBrush", "StatusBarForegroundBrush", "PanelBorderBrush",
            "InputBackgroundBrush", "ButtonBackgroundBrush", "DescriptionForegroundBrush",
            "ErrorForegroundBrush", "WarningForegroundBrush"
        ];

        var missing = brushes
            .Where(name => host.Window.FindResource(name) is null)
            .ToList();

        Assert.Empty(missing);
    }

    [AvaloniaFact]
    public async Task GivesTheTwoThemesDifferentColours()
    {
        // Both resolving is not enough: a dark theme that resolves to the
        // light palette is still the light theme.
        using var host = new TestWindow();
        await host.SettleAsync();

        if (Application.Current is not { } application) return;

        application.RequestedThemeVariant = ThemeVariant.Light;
        await host.SettleAsync();
        var light = host.Window.FindResource("EditorBackgroundBrush")?.ToString();

        application.RequestedThemeVariant = ThemeVariant.Dark;
        await host.SettleAsync();
        var dark = host.Window.FindResource("EditorBackgroundBrush")?.ToString();

        Assert.NotEqual(light, dark);
    }
}
