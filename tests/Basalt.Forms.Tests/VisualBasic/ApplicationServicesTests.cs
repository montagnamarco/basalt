using System.Collections.ObjectModel;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.VisualBasic.ApplicationServices;

namespace Basalt.Forms.Tests.VisualBasic;

/// <summary>
/// WindowsFormsApplicationBase, the model behind My.Application: the order of
/// Run's steps, startup cancellation, the shutdown styles and unhandled exceptions.
/// Run blocks, so each test closes its forms from posted work, with a fallback
/// that closes everything after a few seconds so a bug fails instead of hanging.
/// </summary>
[Collection(ApplicationVisualStyleCollection.Name)]
public class ApplicationServicesTests : IDisposable
{
    private readonly bool _originalVisualStyles = Application.PortableVisualStylesRequested;

    public void Dispose() => Application.PortableVisualStylesRequested = _originalVisualStyles;

    private sealed class RecordingApp : WindowsFormsApplicationBase
    {
        public List<string> Steps { get; } = [];

        public Func<Form>? CreateMainForm { get; init; }

        public bool CancelStartup { get; init; }

        public ShutdownMode Style
        {
            get => ShutdownStyle;
            set => ShutdownStyle = value;
        }

        public Form? Main => MainForm;

        public bool SingleInstance
        {
            get => IsSingleInstance;
            set => IsSingleInstance = value;
        }

        protected override bool OnInitialize(ReadOnlyCollection<string> commandLineArgs)
        {
            Steps.Add("initialize");
            return base.OnInitialize(commandLineArgs);
        }

        protected override bool OnStartup(StartupEventArgs eventArgs)
        {
            Steps.Add("startup");
            eventArgs.Cancel = CancelStartup;
            return base.OnStartup(eventArgs);
        }

        protected override void OnCreateMainForm()
        {
            Steps.Add("create main form");
            MainForm = CreateMainForm?.Invoke() ?? new Form();
            MainForm.Shown += (_, _) => Steps.Add("shown");
        }

        protected override void OnShutdown()
        {
            Steps.Add("shutdown");
            base.OnShutdown();
        }
    }

    /// <summary>
    /// Closes every open form after a delay, so a test that should have ended Run by itself fails
    /// instead of hanging. Disposed when the test ends: left running, it would close the forms of
    /// whichever test runs next.
    /// </summary>
    private static IDisposable SafetyNet()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            foreach (var form in Application.OpenForms.Cast<Form>().ToArray()) form.Close();
        };
        timer.Start();

        return new Stopper(timer);
    }

    private sealed class Stopper(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }

    /// <summary>AfterAllFormsClose waits for every open form, so forms other tests left open must go first.</summary>
    private static void CloseLeftoverForms()
    {
        foreach (var form in Application.OpenForms.Cast<Form>().ToArray()) form.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Run_initializes_starts_creates_and_shows_the_main_form_then_shuts_down_after_it_closes()
    {
        var app = new RecordingApp();
        app.Startup += (_, _) => Dispatcher.UIThread.Post(() => app.Main?.Close(), DispatcherPriority.Background);
        using var safetyNet = SafetyNet();

        app.Run([]);

        Assert.Equal(["initialize", "startup", "create main form", "shown", "shutdown"], app.Steps);
    }

    [AvaloniaFact]
    public void Cancelling_Startup_ends_Run_without_a_main_form_or_a_shutdown()
    {
        var app = new RecordingApp { CancelStartup = true };
        using var safetyNet = SafetyNet();

        app.Run([]);

        Assert.Equal(["initialize", "startup"], app.Steps);
    }

    [AvaloniaFact]
    public void AfterMainFormCloses_ends_Run_even_with_another_form_still_open()
    {
        var other = new Form();
        var app = new RecordingApp { Style = ShutdownMode.AfterMainFormCloses };
        app.Startup += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            other.Show();
            app.Main?.Close();
        }, DispatcherPriority.Background);
        using var safetyNet = SafetyNet();

        app.Run([]);

        Assert.Contains("shutdown", app.Steps);
        Assert.True(other.Visible);
        other.Close();
    }

    [AvaloniaFact]
    public void AfterAllFormsClose_keeps_running_until_the_last_form_closes()
    {
        CloseLeftoverForms();
        var other = new Form();
        var app = new RecordingApp { Style = ShutdownMode.AfterAllFormsClose };
        var mainClosedWhileOtherOpen = false;
        app.Startup += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            other.Show();
            app.Main?.Close();
            mainClosedWhileOtherOpen = !app.Steps.Contains("shutdown");
            Dispatcher.UIThread.Post(other.Close, DispatcherPriority.Background);
        }, DispatcherPriority.Background);
        using var safetyNet = SafetyNet();

        app.Run([]);

        Assert.True(mainClosedWhileOtherOpen);
        Assert.Equal("shutdown", app.Steps[^1]);
    }

    [AvaloniaFact]
    public void An_exception_in_a_handler_raises_UnhandledException_and_ExitApplication_false_keeps_running()
    {
        var app = new RecordingApp();
        var failures = 0;
        var stillRunningAfterwards = false;
        app.UnhandledException += (_, e) =>
        {
            failures++;
            e.ExitApplication = false;

            // Looked at once the handler has returned: with ExitApplication false the main form is still up.
            DispatcherTimer.RunOnce(
                () =>
                {
                    stillRunningAfterwards = app.Main is { IsDisposed: false, Visible: true };
                    app.Main?.Close();
                },
                TimeSpan.FromMilliseconds(50));
        };
        var timer = new System.Windows.Forms.Timer { Interval = 10 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            throw new InvalidOperationException("from a Tick handler");
        };
        app.Startup += (_, _) => timer.Start();
        using var safetyNet = SafetyNet();

        app.Run([]);

        Assert.Equal(1, failures);
        Assert.True(stillRunningAfterwards);
        Assert.Equal("shutdown", app.Steps[^1]);
        timer.Dispose();
    }

    [Fact]
    public void AssemblyInfo_reads_the_assemblys_name_and_version()
    {
        var assembly = typeof(ApplicationServicesTests).Assembly;
        var info = new AssemblyInfo(assembly);

        Assert.Equal("Basalt.Forms.Tests", info.AssemblyName);
        Assert.Equal(assembly.GetName().Version, info.Version);
        Assert.False(string.IsNullOrEmpty(info.Title));
    }

    [AvaloniaFact]
    public void A_second_instance_forwards_its_command_line_to_the_first()
    {
        var id = "basalt-test-" + Guid.NewGuid().ToString("N");
        var first = new RecordingApp { SingleInstance = true, SingleInstanceIdOverride = id };
        var second = new RecordingApp { SingleInstance = true, SingleInstanceIdOverride = id };
        ReadOnlyCollection<string>? forwarded = null;
        Task? secondRun = null;

        first.StartupNextInstance += (_, e) =>
        {
            forwarded = e.CommandLine;
            first.Main?.Close();
        };
        first.Startup += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            // Started from a worker thread, as a second process would be: Run must return there at once.
            secondRun = Task.Run(() => second.Run(["--open", "file.txt"]));
        }, DispatcherPriority.Background);
        using var safetyNet = SafetyNet();

        first.Run(["--first"]);

        // The first instance may hear the forwarded line before the second's Run has quite returned: wait for it.
        Assert.NotNull(secondRun);
        Assert.True(secondRun.Wait(TimeSpan.FromSeconds(5)), "the second instance's Run did not return");
        Assert.Equal(["--open", "file.txt"], forwarded);
        Assert.DoesNotContain("initialize", second.Steps);
    }
}
