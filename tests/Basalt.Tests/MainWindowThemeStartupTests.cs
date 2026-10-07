using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Basalt.Core.Settings;
using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>
/// Whether a theme saved before the window existed is the one it opens in.
/// </summary>
/// <remarks>
/// <see cref="ScreenshotProbe"/>'s MainDarkScreenshot writes Dark to the
/// settings file and then opens a window, the way a real session that was
/// closed in the dark theme restarts. <c>ApplySettings</c> (and the
/// <c>ApplyTheme</c> it calls) only ran from the Settings window's own
/// SettingsChanged event, which nothing raises at startup: a fresh
/// <see cref="MainWindow"/> stayed in App.axaml's hard-coded Light variant
/// regardless of what was on disk. This is proven, not hypothesised: these
/// tests fail on the pre-fix code (confirmed by temporarily removing the
/// constructor's ApplyTheme call and re-running) and pass with it restored.
/// </remarks>
[Collection(SettingsFileCollection.Name)]
public sealed class MainWindowThemeStartupTests : IDisposable
{
    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-theme-startup-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    // Restored on Dispose, same as ThemeScope: these tests are the one place
    // outside it that sets Application.Current's RequestedThemeVariant
    // directly (through a real MainWindow), and leaving Dark behind would
    // make the next test that assumes Light fail depending on run order.
    private readonly ThemeVariant? _previousVariant =
        Application.Current?.RequestedThemeVariant;

    public MainWindowThemeStartupTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);
        if (Application.Current is { } app) app.RequestedThemeVariant = _previousVariant;
        try { File.Delete(_settings); } catch (IOException) { }
    }

    [AvaloniaFact]
    public void OpensInTheDarkThemeWhenThatWasSavedBeforeStartup()
    {
        var store = new SettingsStore(_settings);
        var settings = store.Load();
        settings.Appearance.Theme = AppTheme.Dark;
        store.Save(settings);

        using var host = new TestWindow();

        var background = Assert.IsType<SolidColorBrush>(host.Window.Background);
        Assert.Equal(Color.Parse("#1F1F1F"), background.Color);
    }

    [AvaloniaFact]
    public void StillOpensInTheLightThemeWhenThatIsWhatWasSaved()
    {
        var store = new SettingsStore(_settings);
        var settings = store.Load();
        settings.Appearance.Theme = AppTheme.Light;
        store.Save(settings);

        using var host = new TestWindow();

        var background = Assert.IsType<SolidColorBrush>(host.Window.Background);
        Assert.Equal(Colors.White, background.Color);
    }
}
