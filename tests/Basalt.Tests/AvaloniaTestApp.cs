using Avalonia;
using Avalonia.Headless;
using Basalt.Shell;

[assembly: AvaloniaTestApplication(typeof(Basalt.Tests.AvaloniaTestApp))]

namespace Basalt.Tests;

/// <summary>
/// Application used by the headless tests: it is the IDE's real App, with only
/// the platform swapped for the headless one.
///
/// Rebuilding themes and styles by hand would exercise a configuration the IDE
/// does not use, letting missing themes slip through — such as AvaloniaEdit's,
/// without which the editor stays template-less and invisible in the window.
/// </summary>
/// <summary>
/// Keeps the tests out of the settings of whoever runs them.
///
/// The IDE writes to ~/.basalt/settings.json, and a test that opens a window
/// writes there too: the suite was filling a developer's own list of recent
/// solutions, which no assertion would ever have caught.
/// </summary>
public static class TestSettingsPath
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Redirect()
    {
        var folder = Path.Combine(
            Path.GetTempPath(), "basalt-tests", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(folder);

        Environment.SetEnvironmentVariable(
            "BASALT_SETTINGS", Path.Combine(folder, "settings.json"));
    }
}

public class AvaloniaTestApp : App
{
    public override void OnFrameworkInitializationCompleted()
    {
        // The tests create their own windows: the startup MainWindow is not needed.
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AvaloniaTestApp>()
            // UseSkia so drawing really happens: without a rasteriser a
            // RenderTargetBitmap comes back empty, and a test that only
            // measured its size would pass over a blank icon.
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
