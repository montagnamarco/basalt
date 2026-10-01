using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace Microsoft.VisualBasic.ApplicationServices;

/// <summary>
/// My.Application for a Windows Forms project: Run drives Initialize, Startup,
/// the splash screen, the main form and Shutdown in the order the VB
/// application framework documents, on top of System.Windows.Forms.Application.
/// </summary>
public class WindowsFormsApplicationBase : ConsoleApplicationBase
{
    private readonly ShutdownAwareContext _context = new();
    private readonly SingleInstanceGate _singleInstance = new();
    private readonly Microsoft.VisualBasic.Devices.Network _network = new();
    private long _splashShownAtTicks;

    public WindowsFormsApplicationBase()
    {
        _context.Owner = this;
        _network.NetworkAvailabilityChanged += (_, e) => NetworkAvailabilityChanged?.Invoke(this, e);
    }

    public WindowsFormsApplicationBase(AuthenticationMode authenticationMode)
        : this()
    {
        // Recorded for a future My.User wiring: this task's surface has no
        // getter for it (Microsoft's own WindowsFormsApplicationBase does not
        // expose one either), so nothing else reads it yet.
        _ = authenticationMode;
    }

    /// <summary>Lets a test run two instances with the same id as two unrelated real installations would.</summary>
    internal string? SingleInstanceIdOverride { get; set; }

    private string SingleInstanceId => SingleInstanceIdOverride ?? AppContext.BaseDirectory;

    public System.Windows.Forms.ApplicationContext ApplicationContext => _context;

    protected System.Windows.Forms.SystemColorMode ColorMode { get; set; }

    protected bool EnableVisualStyles { get; set; } = true;

    protected System.Windows.Forms.HighDpiMode HighDpiMode { get; set; } = System.Windows.Forms.HighDpiMode.DpiUnaware;

    protected bool IsSingleInstance { get; set; }

    protected System.Windows.Forms.Form? MainForm
    {
        get => _context.MainForm;
        set => _context.MainForm = value;
    }

    public int MinimumSplashScreenDisplayTime { get; set; }

    public System.Windows.Forms.FormCollection OpenForms => System.Windows.Forms.Application.OpenForms;

    public bool SaveMySettingsOnExit { get; set; }

    protected ShutdownMode ShutdownStyle { get; set; } = ShutdownMode.AfterMainFormCloses;

    public System.Windows.Forms.Form? SplashScreen { get; set; }

    /// <summary>GDI+'s compatible text rendering does not exist on this runtime: always false, as on .NET's own WinForms since .NET Core.</summary>
    protected static bool UseCompatibleTextRendering => false;

    public event ApplyApplicationDefaultsEventHandler? ApplyApplicationDefaults;

    public event Microsoft.VisualBasic.Devices.NetworkAvailableEventHandler? NetworkAvailabilityChanged;

    public event ShutdownEventHandler? Shutdown;

    public event StartupEventHandler? Startup;

    public event StartupNextInstanceEventHandler? StartupNextInstance;

    public event UnhandledExceptionEventHandler? UnhandledException;

    public void DoEvents() => System.Windows.Forms.Application.DoEvents();

    protected void HideSplashScreen()
    {
        if (SplashScreen is null) return;

        var elapsedMilliseconds = Environment.TickCount64 - _splashShownAtTicks;
        var remaining = MinimumSplashScreenDisplayTime - elapsedMilliseconds;
        if (remaining > 0) Thread.Sleep((int)remaining);

        SplashScreen.Hide();
    }

    protected virtual void OnCreateMainForm()
    {
    }

    protected virtual void OnCreateSplashScreen()
    {
    }

    protected virtual bool OnInitialize(ReadOnlyCollection<string> commandLineArgs) => true;

    protected virtual void OnRun()
    {
        if (EnableVisualStyles) System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode);

        System.Windows.Forms.Application.Run(ApplicationContext);
    }

    protected virtual void OnShutdown() => Shutdown?.Invoke(this, EventArgs.Empty);

    protected virtual bool OnStartup(StartupEventArgs eventArgs)
    {
        Startup?.Invoke(this, eventArgs);

        return !eventArgs.Cancel;
    }

    protected virtual void OnStartupNextInstance(StartupNextInstanceEventArgs eventArgs) =>
        StartupNextInstance?.Invoke(this, eventArgs);

    protected virtual bool OnUnhandledException(UnhandledExceptionEventArgs e)
    {
        UnhandledException?.Invoke(this, e);

        return true;
    }

    public void Run(string[] commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);

        InternalCommandLine = new ReadOnlyCollection<string>(commandLine);

        if (IsSingleInstance && !_singleInstance.TryBecomePrimary(SingleInstanceId, CommandLineArgs, OnCommandLineForwarded))
        {
            // Another instance is already running and now has our command line: this process goes no further.
            return;
        }

        System.Windows.Forms.Application.ThreadException += OnApplicationThreadException;

        try
        {
            if (!OnInitialize(CommandLineArgs)) return;

            // As WinForms: Shutdown follows a run that happened, not a startup that was cancelled.
            if (OnStartup(new StartupEventArgs(CommandLineArgs)))
            {
                RaiseApplyApplicationDefaults();

                OnCreateSplashScreen();
                ShowSplashScreen();

                if (MainForm is null) OnCreateMainForm();

                HideSplashScreen();

                if (MainForm is null) throw new NoStartupFormException();

                OnRun();
                OnShutdown();
            }
        }
        finally
        {
            System.Windows.Forms.Application.ThreadException -= OnApplicationThreadException;
            _singleInstance.Dispose();
        }
    }

    protected void ShowSplashScreen()
    {
        if (SplashScreen is null) return;

        _splashShownAtTicks = Environment.TickCount64;
        SplashScreen.Show();
    }

    /// <summary>Raised on the pipe listener thread: StartupNextInstance belongs on the UI thread, like every other My.Application event.</summary>
    private void OnCommandLineForwarded(ReadOnlyCollection<string> args) =>
        Dispatcher.UIThread.Post(() => RaiseStartupNextInstance(args));

    private void RaiseStartupNextInstance(ReadOnlyCollection<string> args)
    {
        var eventArgs = new StartupNextInstanceEventArgs(args, bringToForegroundFlag: true);

        OnStartupNextInstance(eventArgs);

        if (eventArgs.BringToForeground && MainForm is { } form) form.Activate();
    }

    private void OnApplicationThreadException(object? sender, System.Threading.ThreadExceptionEventArgs e)
    {
        var unhandled = new UnhandledExceptionEventArgs(exitApplication: true, e.Exception);

        OnUnhandledException(unhandled);

        if (unhandled.ExitApplication) System.Windows.Forms.Application.Exit();
    }

    /// <summary>Lets a handler change ColorMode, HighDpiMode and the splash's minimum display time before anything is created.</summary>
    private void RaiseApplyApplicationDefaults()
    {
        var e = new ApplyApplicationDefaultsEventArgs
        {
            ColorMode = ColorMode,
            Font = System.Windows.Forms.Control.DefaultFont,
            HighDpiMode = HighDpiMode,
            MinimumSplashScreenDisplayTime = MinimumSplashScreenDisplayTime,
        };

        ApplyApplicationDefaults?.Invoke(this, e);

        ColorMode = e.ColorMode;
        HighDpiMode = e.HighDpiMode;
        MinimumSplashScreenDisplayTime = e.MinimumSplashScreenDisplayTime;
    }

    /// <summary>
    /// The ApplicationContext System.Windows.Forms.Application.Run loops on. Its
    /// default ends the loop when MainForm closes (ShutdownMode.AfterMainFormCloses);
    /// this override also supports AfterAllFormsClose by waiting for every form
    /// that was open when MainForm closed to close too.
    /// </summary>
    private sealed class ShutdownAwareContext : System.Windows.Forms.ApplicationContext
    {
        public WindowsFormsApplicationBase? Owner;

        protected override void OnMainFormClosed(object? sender, EventArgs e)
        {
            if (Owner?.ShutdownStyle != ShutdownMode.AfterAllFormsClose)
            {
                base.OnMainFormClosed(sender, e);
                return;
            }

            CheckAllFormsClosed();
        }

        private void CheckAllFormsClosed()
        {
            var openForms = System.Windows.Forms.Application.OpenForms;

            if (openForms.Count == 0)
            {
                ExitThreadCore();
                return;
            }

            foreach (System.Windows.Forms.Form form in openForms)
            {
                form.FormClosed -= OnAnyFormClosed;
                form.FormClosed += OnAnyFormClosed;
            }
        }

        private void OnAnyFormClosed(object? sender, System.Windows.Forms.FormClosedEventArgs e) => CheckAllFormsClosed();
    }
}
