using System.Threading;
using Avalonia.Threading;

namespace System.Windows.Forms;

/// <summary>
/// The synchronization context an <c>await</c> inside a control's event
/// handler captures, so the continuation comes back to the UI thread instead
/// of a thread pool one. Post and Send use the same queue
/// <see cref="Application.Run"/> already pumps (Avalonia's dispatcher, see
/// Application.cs): Post is <see cref="Dispatcher.Post"/>, Send on the UI
/// thread runs the callback directly (there is nothing to wait for), and off
/// it blocks the caller on <see cref="Dispatcher.Invoke"/> until the UI
/// thread has run it, exactly as Windows' own SendMessage blocks a caller
/// until the window procedure returns.
/// </summary>
public sealed class WindowsFormsSynchronizationContext : SynchronizationContext, IDisposable
{
    private static bool s_autoInstall = true;

    public WindowsFormsSynchronizationContext()
    {
    }

    public static bool AutoInstall
    {
        get => s_autoInstall;
        set => s_autoInstall = value;
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        Dispatcher.UIThread.Post(() => d(state));
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        if (Dispatcher.UIThread.CheckAccess())
        {
            d(state);
            return;
        }

        Dispatcher.UIThread.Invoke(() => d(state));
    }

    public override SynchronizationContext CreateCopy() => new WindowsFormsSynchronizationContext();

    public void Dispose()
    {
    }

    /// <summary>Puts the thread's SynchronizationContext.Current back to what it was before this one was installed.</summary>
    public static void Uninstall()
    {
        if (Current is WindowsFormsSynchronizationContext) SetSynchronizationContext(null);
    }

    /// <summary>Called once by Application.Run: installs this context on the UI thread so a later await comes back to it.</summary>
    internal static void InstallIfNeeded()
    {
        if (s_autoInstall && Current is not WindowsFormsSynchronizationContext) SetSynchronizationContext(new WindowsFormsSynchronizationContext());
    }
}
