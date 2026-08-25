using Basalt.Core.Settings;
using Avalonia.Styling;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Basalt.Shell;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // A path passed on the command line opens that solution right away.
            var target = desktop.Args?.FirstOrDefault(a => !a.StartsWith('-'));
            // The theme is chosen before the window is built, so it opens in
            // the theme the user picked rather than flashing the other one.
            ApplySavedTheme();

            desktop.MainWindow = new MainWindow(File.Exists(target) ? target : null);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Applies the theme the user last chose.
    ///
    /// Settings that cannot be read fall back to the light theme, which is the
    /// default the rest of the IDE assumes.
    /// </summary>
    private void ApplySavedTheme()
    {
        var settings = new SettingsStore().Load();

        RequestedThemeVariant =
            IdeThemes.VariantFor(settings.Appearance.Theme) ?? ThemeVariant.Default;
    }
}