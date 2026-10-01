using Avalonia;
using Avalonia.Threading;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>
/// Starts Avalonia the first time a window is needed, unless the process
/// already runs it (an Avalonia application hosting forms, or tests on the
/// headless platform). Avalonia here is only the platform: windows, input,
/// clipboard and screens; nothing of its controls or themes is used.
/// </summary>
internal static class AvaloniaPlatform
{
    private static readonly Lock Gate = new();

    public static void EnsureStarted()
    {
        if (Avalonia.Application.Current is not null) return;

        lock (Gate)
        {
            if (Avalonia.Application.Current is not null) return;

            AppBuilder.Configure<FormsApplication>().UsePlatformDetect().SetupWithoutStarting();
        }
    }

    public static Dispatcher Dispatcher => Dispatcher.UIThread;

    /// <summary>The Avalonia application Basalt.Forms starts when nothing else did; it adds nothing.</summary>
    private sealed class FormsApplication : Avalonia.Application
    {
    }
}
