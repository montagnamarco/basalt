using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Basalt.Core.Settings;
using Basalt.Shell;
using Basalt.Shell.Syntax;
using TextMateSharp.Grammars;

namespace Basalt.Tests;

/// <summary>
/// The themes the interface can be shown in.
///
/// A key a theme forgets falls back to its base variant rather than failing,
/// which is why these check every key of every theme: the failure is silent
/// and shows up only as one panel in the wrong colour.
/// </summary>
public sealed class ThemeTests
{
    /// <summary>Every colour key the interface reads.</summary>
    private static readonly string[] Keys =
    [
        "EditorBackground", "EditorForeground",
        "SideBarBackground", "SideBarBorder", "ActivityBarBackground",
        "TabActiveBackground", "TabInactiveBackground", "TabBorder", "TabActiveBorderTop",
        "StatusBarBackground", "StatusBarForeground",
        "TitleBarBackground", "PanelBorder",
        "ListHoverBackground", "ListActiveSelectionBackground",
        "ListActiveSelectionForeground", "ListInactiveSelectionBackground",
        "FocusBorder", "InputBackground", "InputBorder",
        "ButtonBackground", "ButtonForeground",
        "ButtonSecondaryBackground", "ButtonSecondaryForeground",
        "ErrorForeground", "WarningForeground", "DescriptionForeground", "ScrollbarSlider"
    ];

    /// <summary>Every theme a user can pick, other than following the system.</summary>
    public static TheoryData<AppTheme> Themes()
    {
        var data = new TheoryData<AppTheme>();

        foreach (var theme in Enum.GetValues<AppTheme>().Where(t => t != AppTheme.System))
            data.Add(theme);

        return data;
    }

    /// <summary>The colour a theme gives a key, or null when it gives none.</summary>
    private static Color? ColourOf(AppTheme theme, string key)
    {
        var window = new Window { RequestedThemeVariant = IdeThemes.VariantFor(theme) };

        return window.TryFindResource(key, window.ActualThemeVariant, out var value)
            && value is Color colour
                ? colour
                : null;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Themes))]
    public void EveryThemeDefinesEveryColour(AppTheme theme)
    {
        var missing = Keys.Where(k => ColourOf(theme, k) is null).ToList();

        Assert.True(missing.Count == 0,
            $"{theme} does not define: {string.Join(", ", missing)}");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Themes))]
    public void KeepsTextReadableAgainstItsBackground(AppTheme theme)
    {
        // Text the same colour as what it sits on is the one theme mistake
        // that makes the interface unusable rather than merely ugly.
        var background = ColourOf(theme, "EditorBackground");
        var foreground = ColourOf(theme, "EditorForeground");

        Assert.NotNull(background);
        Assert.NotNull(foreground);

        var contrast = Contrast(background.Value, foreground.Value);

        Assert.True(contrast >= 4.5,
            $"{theme}: editor text contrast is {contrast:F1}, below the 4.5 needed to read it.");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Themes))]
    public void KeepsTheStatusBarReadable(AppTheme theme)
    {
        var background = ColourOf(theme, "StatusBarBackground");
        var foreground = ColourOf(theme, "StatusBarForeground");

        var contrast = Contrast(background!.Value, foreground!.Value);

        Assert.True(contrast >= 4.5,
            $"{theme}: status bar contrast is {contrast:F1}, below the 4.5 needed to read it.");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Themes))]
    public void KeepsButtonTextReadable(AppTheme theme)
    {
        var contrast = Contrast(
            ColourOf(theme, "ButtonBackground")!.Value,
            ColourOf(theme, "ButtonForeground")!.Value);

        Assert.True(contrast >= 4.5,
            $"{theme}: button text contrast is {contrast:F1}.");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Themes))]
    public void SeparatesPanelsFromTheirBorders(AppTheme theme)
    {
        // A border the same colour as the panel is a border nobody sees.
        var background = ColourOf(theme, "SideBarBackground")!.Value;
        var border = ColourOf(theme, "SideBarBorder")!.Value;

        Assert.True(Contrast(background, border) >= 1.15,
            $"{theme}: the side bar border does not stand out from the side bar.");
    }

    [AvaloniaFact]
    public void GivesTheHighContrastThemesMoreContrastThanTheOrdinaryOnes()
    {
        // Otherwise the name promises something it does not deliver.
        var plain = Contrast(
            ColourOf(AppTheme.Light, "EditorBackground")!.Value,
            ColourOf(AppTheme.Light, "EditorForeground")!.Value);

        var high = Contrast(
            ColourOf(AppTheme.HighContrastLight, "EditorBackground")!.Value,
            ColourOf(AppTheme.HighContrastLight, "EditorForeground")!.Value);

        Assert.True(high > plain, $"High contrast is {high:F1}, ordinary light is {plain:F1}.");
    }

    [AvaloniaFact]
    public void GivesEachThemeItsOwnLook()
    {
        // Two themes that resolve to the same colours would be one theme with
        // two names, which is what a mistyped variant key produces.
        var backgrounds = Enum.GetValues<AppTheme>()
            .Where(t => t != AppTheme.System)
            .Select(t => (Theme: t, Colour: ColourOf(t, "EditorBackground")))
            .ToList();

        var solarized = backgrounds.Single(b => b.Theme == AppTheme.SolarizedDark).Colour;
        var dark = backgrounds.Single(b => b.Theme == AppTheme.Dark).Colour;
        var blue = backgrounds.Single(b => b.Theme == AppTheme.VisualStudioBlue).Colour;

        Assert.NotEqual(dark, solarized);
        Assert.NotEqual(blue, solarized);
    }

    [Fact]
    public void KeepsTheNamesAnOlderSettingsFileWrote()
    {
        // A settings file written before the new themes existed still names
        // Light, Dark or System, and has to read back as the same theme.
        Assert.Equal(0, (int)AppTheme.Light);
        Assert.Equal(1, (int)AppTheme.Dark);
        Assert.Equal(2, (int)AppTheme.System);
    }

    [Fact]
    public void LeavesTheSystemThemeToAvalonia()
    {
        Assert.Null(IdeThemes.VariantFor(AppTheme.System));
    }

    [Fact]
    public void KnowsWhichThemesAreDark()
    {
        Assert.True(IdeThemes.IsDark(AppTheme.Dark));
        Assert.True(IdeThemes.IsDark(AppTheme.SolarizedDark));
        Assert.True(IdeThemes.IsDark(AppTheme.HighContrastDark));
        Assert.False(IdeThemes.IsDark(AppTheme.Light));
        Assert.False(IdeThemes.IsDark(AppTheme.SolarizedLight));
        Assert.False(IdeThemes.IsDark(AppTheme.VisualStudioBlue));
    }

    [Fact]
    public void NamesEveryThemeForTheSettingsWindow()
    {
        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            var name = IdeThemes.DisplayName(theme);

            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }

    [Fact]
    public void TurnsEveryShownNameBackIntoItsTheme()
    {
        // The settings window shows names and stores values, so a name that
        // does not map back would quietly select the wrong theme.
        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            var name = IdeThemes.DisplayName(theme);

            var matches = Enum.GetValues<AppTheme>()
                .Where(t => IdeThemes.DisplayName(t) == name)
                .ToList();

            Assert.Equal(theme, Assert.Single(matches));
        }
    }

    [Fact]
    public void PairsEveryThemeWithARealGrammarTheme()
    {
        // TextMateSharp ships a counterpart for each, so none falls back to
        // an approximation. Two interface themes sharing one grammar theme
        // would mean one of them was never really mapped.
        var pairs = Enum.GetValues<AppTheme>()
            .Where(t => t != AppTheme.System)
            .Select(t => (Theme: t, Grammar: TextMateHighlighting.GrammarThemeFor(t)))
            .ToList();

        Assert.Equal(pairs.Count, pairs.Select(p => p.Grammar).Distinct().Count());
    }

    [Theory]
    [InlineData(AppTheme.SolarizedDark, ThemeName.SolarizedDark)]
    [InlineData(AppTheme.SolarizedLight, ThemeName.SolarizedLight)]
    [InlineData(AppTheme.HighContrastDark, ThemeName.HighContrastDark)]
    [InlineData(AppTheme.HighContrastLight, ThemeName.HighContrastLight)]
    [InlineData(AppTheme.Dark, ThemeName.DarkPlus)]
    [InlineData(AppTheme.Light, ThemeName.LightPlus)]
    public void ColoursSyntaxToMatchTheInterface(AppTheme theme, ThemeName expected)
    {
        Assert.Equal(expected, TextMateHighlighting.GrammarThemeFor(theme));
    }

    /// <summary>
    /// The contrast ratio between two colours, as WCAG defines it.
    ///
    /// 4.5 is the threshold for ordinary text; the tests above use it because
    /// it is the number that decides whether text can be read.
    /// </summary>
    private static double Contrast(Color a, Color b)
    {
        var first = Luminance(a);
        var second = Luminance(b);

        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color colour)
    {
        static double Channel(byte value)
        {
            var v = value / 255.0;

            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(colour.R)
             + 0.7152 * Channel(colour.G)
             + 0.0722 * Channel(colour.B);
    }
}
