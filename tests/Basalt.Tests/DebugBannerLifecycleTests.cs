using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace Basalt.Tests;

/// <summary>
/// The banner going away again.
/// </summary>
/// <remarks>
/// It was raised when a session began and never lowered when one ended: the
/// strip stayed up over a program that had already stopped, which is worse
/// than never having shown one — the window says something is running when
/// nothing is.
/// </remarks>
public class DebugBannerLifecycleTests
{
    [AvaloniaFact]
    public async Task TakesTheBannerDownWhenDebuggingStops()
    {
        using var host = new TestWindow();
        await host.SettleAsync(2);

        host.Window.ShowStartingBannerForTests("MyApp.dll");

        var banner = host.Window.FindControl<Border>("DebugBanner")!;
        Assert.True(banner.IsVisible, "the banner should be up while starting");

        await host.Window.StopDebuggingForTests();

        Assert.False(banner.IsVisible, "the banner should go once nothing is being debugged");
    }

    [AvaloniaFact]
    public async Task ForgetsWhatItWasDebugging()
    {
        // Otherwise the next session opens showing the name of the last one
        // until its own is known.
        using var host = new TestWindow();
        await host.SettleAsync(2);

        host.Window.ShowStartingBannerForTests("Old.dll");
        await host.Window.StopDebuggingForTests();

        host.Window.ShowDebugStateForTests();

        var detail = host.Window.FindControl<TextBlock>("DebugBannerDetail")!;

        Assert.True(string.IsNullOrEmpty(detail.Text) || detail.Text != "Old.dll");
    }
}
