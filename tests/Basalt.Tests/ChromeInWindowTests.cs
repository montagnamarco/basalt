using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell;
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
    public async Task PutsTheAssistantInTheDefaultLayoutAndTheTestsWindowOnDemand()
    {
        // Registering a dockable is not the same as it appearing: a tool with
        // no content is an empty tab, which reads as a broken feature.
        //
        // The Tests window is no longer part of the design layout — like
        // Visual Studio's own Test Explorer, it opens on demand rather than
        // sitting docked from the start — so this only asserts it afterward,
        // once its View menu entry has opened it.
        using var host = new TestWindow();
        await host.SettleAsync();

        List<string?> Titles() => host.Window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .Where(t => t is not null)
            .ToList();

        Assert.Contains("Assistant", Titles());
        Assert.DoesNotContain("Tests", Titles());

        void Collect(IReadOnlyList<MenuEntry> entries, List<MenuEntry> into)
        {
            foreach (var entry in entries)
            {
                if (entry.Children is { Count: > 0 } children) Collect(children, into);
                else into.Add(entry);
            }
        }

        var all = new List<MenuEntry>();
        Collect(host.Window.MenuEntriesForTests(), all);

        var showTests = all.Single(e => e.Header == Localizer.Get(StringKeys.ToolTests));
        showTests.Action!();
        await host.SettleAsync();

        Assert.Contains("Tests", Titles());
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
        using var theme = new ThemeScope(variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        using var host = new TestWindow();
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
        using var theme = new ThemeScope(ThemeVariant.Light);
        using var host = new TestWindow();
        await host.SettleAsync();

        var application = Application.Current!;
        application.RequestedThemeVariant = ThemeVariant.Light;
        await host.SettleAsync();
        var light = host.Window.FindResource("EditorBackgroundBrush")?.ToString();

        application.RequestedThemeVariant = ThemeVariant.Dark;
        await host.SettleAsync();
        var dark = host.Window.FindResource("EditorBackgroundBrush")?.ToString();

        Assert.NotEqual(light, dark);
    }
}
