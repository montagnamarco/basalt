using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Basalt.Forms.Tests.Hosting.HeadlessApp))]

namespace Basalt.Forms.Tests.Hosting;

/// <summary>
/// The headless platform the window tests run on, drawing with Skia so that
/// frames can be captured and their pixels read. Basalt.Forms finds Avalonia
/// already running and uses it instead of starting its own.
/// </summary>
public sealed class HeadlessApp : Avalonia.Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
