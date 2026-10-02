using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Threading;

namespace System.Windows.Forms;

/// <summary>
/// The application: its message loop, its open forms and its settings. The
/// loop is Avalonia's dispatcher, started on the thread that calls Run.
/// </summary>
public sealed partial class Application
{
    private static readonly FormCollection s_openForms = new();
    private static CancellationTokenSource? s_loop;
    private static UnhandledExceptionMode s_exceptionMode = UnhandledExceptionMode.Automatic;
    private static bool s_exiting;

    private Application()
    {
    }

    /// <summary>The font set with SetDefaultFont, which controls use when they set none.</summary>
    internal static Font? DefaultFontOverride { get; private set; }

    /// <summary>Sets the font controls use by default.</summary>
    public static void SetDefaultFont(Font font)
    {
        ArgumentNullException.ThrowIfNull(font);

        DefaultFontOverride = font.IsSystemFont ? font : (Font)font.Clone();
    }

    [Browsable(false)]
    public static FormCollection OpenForms => s_openForms;

    /// <summary>Whether a message loop is running.</summary>
    public static bool MessageLoop => s_loop is not null;

    /// <summary>Runs the message loop until <see cref="ExitThread"/> or <see cref="Exit()"/>.</summary>
    public static void Run() => Run(new ApplicationContext());

    /// <summary>Shows the form and runs the message loop until it closes.</summary>
    public static void Run(Form mainForm) => Run(new ApplicationContext(mainForm));

    public static void Run(ApplicationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (s_loop is not null) throw new InvalidOperationException("Starting a second message loop on a single thread is not a valid operation. Use Form.ShowDialog instead.");

        AvaloniaPlatform.EnsureStarted();

        var previousContext = SynchronizationContext.Current;

        using var loop = new CancellationTokenSource();
        s_loop = loop;
        s_exiting = false;

        void EndLoop(object? sender, EventArgs e) => loop.Cancel();

        context.ThreadExit += EndLoop;

        // Everything after s_loop is set runs inside the try: a failure here must not leave the thread
        // believing a loop is still running, which would refuse every later Run.
        try
        {
            WindowsFormsSynchronizationContext.InstallIfNeeded();

            // The queue starts empty: one Idle, as WinForms raises when the loop first finds it so.
            ScheduleIdle();

            if (context.MainForm is { } mainForm) mainForm.Visible = true;

            if (!loop.IsCancellationRequested) Dispatcher.UIThread.MainLoop(loop.Token);
        }
        finally
        {
            context.ThreadExit -= EndLoop;
            s_loop = null;

            // Only ours to put back: WinForms' AutoInstall-driven install is undone the same way it was done.
            SynchronizationContext.SetSynchronizationContext(previousContext);

            ThreadExit?.Invoke(null, EventArgs.Empty);
            context.Dispose();
        }
    }

    /// <summary>
    /// Asks every open form to close, with CloseReason.ApplicationExitCall;
    /// if none cancels, they close and the message loop ends.
    /// </summary>
    public static void Exit() => Exit(null);

    public static void Exit(CancelEventArgs? e)
    {
        if (s_exiting) return;

        s_exiting = true;

        try
        {
            foreach (var form in s_openForms.Snapshot())
            {
                if (!form.CloseCore(CloseReason.ApplicationExitCall))
                {
                    if (e is not null) e.Cancel = true;

                    return;
                }
            }

            ApplicationExit?.Invoke(null, EventArgs.Empty);
            s_loop?.Cancel();
        }
        finally
        {
            s_exiting = false;
        }
    }

    /// <summary>Ends the message loop without closing the forms.</summary>
    public static void ExitThread() => s_loop?.Cancel();

    public static event EventHandler? ApplicationExit;

    public static event EventHandler? ThreadExit;

    /// <summary>Exceptions thrown by event handlers in the message loop; without a handler, the default dialog.</summary>
    public static event ThreadExceptionEventHandler? ThreadException;

    /// <summary>Whether an exception in a handler goes to ThreadException instead of ending the process.</summary>
    internal static bool HandlesThreadExceptions => s_exceptionMode != UnhandledExceptionMode.ThrowException;

    public static void SetUnhandledExceptionMode(UnhandledExceptionMode mode) => SetUnhandledExceptionMode(mode, true);

    public static void SetUnhandledExceptionMode(UnhandledExceptionMode mode, bool threadScope) => s_exceptionMode = mode;

    /// <summary>
    /// Reports an exception from the message loop: to the ThreadException
    /// handlers if there are any, otherwise to standard error, and the
    /// application goes on, as after the default dialog's Continue.
    /// </summary>
    public static void OnThreadException(Exception t)
    {
        if (ThreadException is { } handler)
        {
            handler(Thread.CurrentThread, new ThreadExceptionEventArgs(t));
            return;
        }

        Console.Error.WriteLine($"Unhandled exception in a Windows Forms event handler: {t}");
    }

    /// <summary>Visual styles are not used: controls take their classic look from the skin.</summary>
    public static void EnableVisualStyles()
    {
    }

    public static bool RenderWithVisualStyles => false;

    public static bool UseVisualStyles => false;

    public static void SetCompatibleTextRenderingDefault(bool defaultValue) => UseCompatibleTextRendering = defaultValue;

    internal static bool UseCompatibleTextRendering { get; private set; }

    /// <summary>Accepted: the forms are drawn at 96 logical dpi and scaled by the system, whatever the mode.</summary>
    public static bool SetHighDpiMode(HighDpiMode highDpiMode)
    {
        HighDpiMode = highDpiMode;

        return true;
    }

    public static HighDpiMode HighDpiMode { get; private set; } = HighDpiMode.DpiUnaware;

    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, AppDomain.CurrentDomain.FriendlyName);

    public static string StartupPath => Path.GetDirectoryName(ExecutablePath) ?? AppContext.BaseDirectory;

    public static string? CompanyName => EntryAttribute<AssemblyCompanyAttribute>()?.Company ?? EntryNamespace();

    public static string? ProductName => EntryAttribute<AssemblyProductAttribute>()?.Product ?? EntryNamespace();

    public static string ProductVersion =>
        EntryAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? "1.0.0.0";

    /// <summary>The per-user data folder: company, product and version under the user's application data.</summary>
    public static string UserAppDataPath => DataPath(Environment.SpecialFolder.ApplicationData);

    public static string LocalUserAppDataPath => DataPath(Environment.SpecialFolder.LocalApplicationData);

    public static string CommonAppDataPath => DataPath(Environment.SpecialFolder.CommonApplicationData);

    private static string DataPath(Environment.SpecialFolder folder)
    {
        var path = Path.Combine(Environment.GetFolderPath(folder), CompanyName ?? string.Empty, ProductName ?? string.Empty, ProductVersion);
        Directory.CreateDirectory(path);

        return path;
    }

    private static T? EntryAttribute<T>() where T : Attribute => Assembly.GetEntryAssembly()?.GetCustomAttribute<T>();

    /// <summary>WinForms falls back to the namespace of the entry point's type.</summary>
    private static string? EntryNamespace() => Assembly.GetEntryAssembly()?.EntryPoint?.DeclaringType?.Namespace;
}
