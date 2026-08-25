using Avalonia.Styling;
using Basalt.Core.Settings;

namespace Basalt.Shell;

/// <summary>
/// The theme variants the interface can be shown in.
///
/// Avalonia's own Light and Dark are two of them; the rest are custom
/// variants, which have to be static fields because a custom one cannot be
/// written as a plain key in XAML — the type converter takes only the built-in
/// names, and a custom variant needs `x:Static`.
///
/// Each names the built-in variant it is based on. A key a theme does not
/// define falls back to its base rather than failing, so a palette only has
/// to say where it differs.
/// </summary>
public static class IdeThemes
{
    public static readonly ThemeVariant VisualStudioBlue =
        new(nameof(VisualStudioBlue), ThemeVariant.Light);

    public static readonly ThemeVariant HighContrastLight =
        new(nameof(HighContrastLight), ThemeVariant.Light);

    public static readonly ThemeVariant HighContrastDark =
        new(nameof(HighContrastDark), ThemeVariant.Dark);

    public static readonly ThemeVariant SolarizedLight =
        new(nameof(SolarizedLight), ThemeVariant.Light);

    public static readonly ThemeVariant SolarizedDark =
        new(nameof(SolarizedDark), ThemeVariant.Dark);

    /// <summary>
    /// The variant a setting asks for.
    ///
    /// System is left to Avalonia, which follows the desktop.
    /// </summary>
    public static ThemeVariant? VariantFor(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        AppTheme.System => null,
        AppTheme.VisualStudioBlue => VisualStudioBlue,
        AppTheme.HighContrastLight => HighContrastLight,
        AppTheme.HighContrastDark => HighContrastDark,
        AppTheme.SolarizedLight => SolarizedLight,
        AppTheme.SolarizedDark => SolarizedDark,
        _ => ThemeVariant.Light
    };

    /// <summary>Whether a theme is a dark one, for the parts that need to know.</summary>
    public static bool IsDark(AppTheme theme) => theme switch
    {
        AppTheme.Dark or AppTheme.HighContrastDark or AppTheme.SolarizedDark => true,
        _ => false
    };

    /// <summary>The name to show for a theme.</summary>
    public static string DisplayName(AppTheme theme) => theme switch
    {
        AppTheme.Light => "Light",
        AppTheme.Dark => "Dark",
        AppTheme.System => "Follow the system",
        AppTheme.VisualStudioBlue => "Visual Studio Blue",
        AppTheme.HighContrastLight => "High Contrast Light",
        AppTheme.HighContrastDark => "High Contrast Dark",
        AppTheme.SolarizedLight => "Solarized Light",
        AppTheme.SolarizedDark => "Solarized Dark",
        _ => theme.ToString()
    };
}
