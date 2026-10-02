using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Threading;

namespace System.Windows.Forms;

/// <summary>
/// Message filters, the idle and thread-modal notifications, and the
/// settings (colour mode, culture, wait cursor, visual style state) every
/// control reads from the application rather than from itself.
/// </summary>
public sealed partial class Application
{
    /// <summary>A loop a host without one of its own runs instead, while registered: see <see cref="DoEvents"/>.</summary>
    public delegate bool MessageLoopCallback();

    private static readonly List<IMessageFilter> s_filters = [];
    private static MessageLoopCallback? s_messageLoopCallback;
    private static SystemColorMode s_colorMode = SystemColorMode.Classic;
    private static bool s_useWaitCursor;
    private static VisualStyles.VisualStyleState s_visualStyleState = VisualStyles.VisualStyleState.NoneEnabled;
    private static string _safeTopLevelCaptionFormat = "{0}";

    public static void AddMessageFilter(IMessageFilter value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!s_filters.Contains(value)) s_filters.Add(value);
    }

    public static void RemoveMessageFilter(IMessageFilter value) => s_filters.Remove(value);

    /// <summary>Offers the message to every registered filter; the first that consumes it wins.</summary>
    public static bool FilterMessage(ref Message message)
    {
        foreach (var filter in s_filters.ToArray())
        {
            if (filter.PreFilterMessage(ref message)) return true;
        }

        return false;
    }

    /// <summary>
    /// A host that pumps its own messages instead of calling <see cref="Run"/>
    /// registers a callback here; <see cref="DoEvents"/> keeps calling it,
    /// alongside Avalonia's own queue, for as long as it answers true.
    /// </summary>
    public static void RegisterMessageLoop(MessageLoopCallback? callback) => s_messageLoopCallback = callback;

    public static void UnregisterMessageLoop() => s_messageLoopCallback = null;

    public static event EventHandler? EnterThreadModal;

    public static event EventHandler? LeaveThreadModal;

    internal static void RaiseEnterThreadModal() => EnterThreadModal?.Invoke(null, EventArgs.Empty);

    internal static void RaiseLeaveThreadModal() => LeaveThreadModal?.Invoke(null, EventArgs.Empty);

    public static event EventHandler? Idle;

    /// <summary>Raises Idle: called by the handful of places (ShowDialog's wait, DoEvents) that stand in for the Win32 idle message.</summary>
    public static void RaiseIdle(EventArgs e) => Idle?.Invoke(null, e);

    private static int s_idlePending;

    /// <summary>
    /// Asks for Idle to be raised once the dispatcher has nothing left of a
    /// higher priority to run: at most one such job is ever outstanding, and
    /// it does not reschedule itself, so with nothing to process this fires
    /// exactly once (from whoever first calls it) and not again, the way
    /// WinForms raises WM_ENTERIDLE once the queue it just emptied stays
    /// empty, not on a timer. Called where Basalt processes a message: the
    /// start of Run and of a modal ShowDialog, the end of
    /// <see cref="FormsInput.Guard{T}"/> (every mouse and keyboard message),
    /// and after a Timer.Tick.
    /// </summary>
    internal static void ScheduleIdle()
    {
        if (Interlocked.CompareExchange(ref s_idlePending, 1, 0) != 0) return;

        Dispatcher.UIThread.Post(() =>
        {
            s_idlePending = 0;
            RaiseIdle(EventArgs.Empty);
        }, DispatcherPriority.ApplicationIdle);
    }

    [Browsable(false)]
    public static bool AllowQuit => true;

    /// <summary>
    /// Whether the thread needs an apartment for OLE drag-and-drop and the
    /// clipboard: always answers what the runtime itself reports, since
    /// there is no COM layer underneath to ask separately.
    /// </summary>
    public static ApartmentState OleRequired() => Threading.Thread.CurrentThread.GetApartmentState();

    /// <summary>Closes every open form and, if none cancelled, restarts the process from the same executable and arguments.</summary>
    public static void Restart()
    {
        var cancelled = new System.ComponentModel.CancelEventArgs();
        Exit(cancelled);
        if (cancelled.Cancel) return;

        if (ExecutablePath is { Length: > 0 } path) Diagnostics.Process.Start(path, Environment.GetCommandLineArgs().Skip(1));
    }

    public static SystemColorMode ColorMode => s_colorMode;

    public static SystemColorMode SystemColorMode => s_colorMode;

    public static void SetColorMode(SystemColorMode systemColorMode) => s_colorMode = systemColorMode;

    /// <summary>This platform never reports a dark system theme of its own; always false.</summary>
    public static bool IsDarkModeEnabled => false;

    /// <summary>
    /// Suspending or resuming the machine is an operating-system privilege
    /// this platform's windowing layer does not expose a cross-platform way
    /// to ask for; documented as never honoured, as Windows itself would
    /// refuse it to an application without the right privilege.
    /// </summary>
    public static bool SetSuspendState(PowerState state, bool force, bool disableWakeEvent) => false;

    public static string SafeTopLevelCaptionFormat
    {
        get => _safeTopLevelCaptionFormat;
        set => _safeTopLevelCaptionFormat = value ?? "{0}";
    }

    [Browsable(false)]
    public static CultureInfo CurrentCulture
    {
        get => Threading.Thread.CurrentThread.CurrentUICulture;
        set => Threading.Thread.CurrentThread.CurrentUICulture = value ?? CultureInfo.CurrentUICulture;
    }

    [Browsable(false)]
    public static InputLanguage CurrentInputLanguage
    {
        get => InputLanguage.CurrentInputLanguage;
        set => InputLanguage.CurrentInputLanguage = value;
    }

    /// <summary>The registry is a Windows concept; there is none on this platform, so every per-user or per-machine key is null.</summary>
    [Browsable(false)]
    public static Microsoft.Win32.RegistryKey? CommonAppDataRegistry => null;

    [Browsable(false)]
    public static Microsoft.Win32.RegistryKey? UserAppDataRegistry => null;

    [DefaultValue(false)]
    public static bool UseWaitCursor
    {
        get => s_useWaitCursor;
        set => s_useWaitCursor = value;
    }

    public static VisualStyles.VisualStyleState VisualStyleState
    {
        get => s_visualStyleState;
        set => s_visualStyleState = value;
    }

    /// <summary>Processes the messages waiting, and keeps pumping a registered message loop too.</summary>
    public static void DoEvents()
    {
        AvaloniaPlatform.EnsureStarted();
        Dispatcher.UIThread.RunJobs();

        while (s_messageLoopCallback?.Invoke() == true) Dispatcher.UIThread.RunJobs();
    }
}
