using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Basalt.Core.Localization;
using Basalt.Core.Services;
using Basalt.Core.Settings;
using Basalt.Shell;

namespace Basalt.Tests;

public sealed partial class UserJourneyDebuggerTests2
{
    // ==================================================================
    // Break when an exception is thrown. A Try/Catch handles the exception, so
    // with the choice off the program runs on to its end; with it on the debugger
    // stops at the Throw, and the banner and the Output say what was thrown.
    // ==================================================================
    [AvaloniaFact]
    public async Task WithBreakWhenThrownOnAHandledExceptionStopsAtTheThrowAndSaysWhatWasThrown()
    {
        Assert.SkipWhen(AdapterMissing, "netcoredbg is not installed.");

        using var host = await OpenProgramAsync(NetCoreDebugTargets.CatchingThrower);
        var w = host.Window;

        try
        {
            await w.ChangeExceptionBreaksAsync(new ExceptionBreaks(WhenThrown: true, WhenUserUnhandled: false));

            // Kept for the next run of the IDE: the settings file holds it.
            var saved = new SettingsStore(_settings).Load().Debug;
            Assert.True(saved.BreakWhenExceptionThrown);
            Assert.False(saved.BreakOnUserUnhandledException);

            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);
            await PauseAtAsync(host, NetCoreDebugTargets.CatchingThrowLine);

            var session = SessionOf(w);
            Assert.Equal("System.InvalidOperationException: boom handled", session.StoppedException);
            Assert.True(session.StoppedOnThrow);

            var banner = w.FindControl<TextBlock>("DebugBannerText")!.Text;
            var detail = w.FindControl<TextBlock>("DebugBannerDetail")!.Text;

            Assert.Equal(L(StringKeys.DebugBannerExceptionThrown), banner);
            Assert.Contains("System.InvalidOperationException: boom handled", detail, StringComparison.Ordinal);
            Assert.Contains("Program.vb:5", detail, StringComparison.Ordinal);
            Assert.Contains("boom handled", OutputOf(w), StringComparison.Ordinal);
        }
        finally
        {
            await w.StopDebuggingForTests();
        }
    }

    [AvaloniaFact]
    public async Task WithBreakWhenThrownOffAHandledExceptionDoesNotStop()
    {
        Assert.SkipWhen(AdapterMissing, "netcoredbg is not installed.");

        using var host = await OpenProgramAsync(NetCoreDebugTargets.CatchingThrower);
        var w = host.Window;
        var session = SessionOf(w);
        var stops = 0;
        session.Paused += (_, _) => stops++;

        try
        {
            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);

            Assert.True(await PumpUntilAsync(host,
                () => ConsoleTextOf(w).Contains("basalt-finished", StringComparison.Ordinal),
                TimeSpan.FromSeconds(90)),
                "The program did not run to its end: " + StateOf(w, ProgramPath));

            Assert.True(await PumpUntilAsync(host, () => !session.IsRunning, TimeSpan.FromSeconds(30)));
            Assert.Equal(0, stops);
        }
        finally
        {
            await w.StopDebuggingForTests();
        }
    }

    [AvaloniaFact]
    public async Task TurningBreakWhenThrownOnDuringASessionTakesEffectAtOnce()
    {
        Assert.SkipWhen(AdapterMissing, "netcoredbg is not installed.");

        using var host = await OpenProgramAsync(NetCoreDebugTargets.CatchingThrower);
        var w = host.Window;

        try
        {
            // Stopped before the Try, with the choice off.
            SetBreakpointWithF9(w, ProgramPath, 3);
            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);
            await PauseAtAsync(host, 3);
            Assert.Null(SessionOf(w).StoppedException);

            await w.ChangeExceptionBreaksAsync(new ExceptionBreaks(true, false));

            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);
            await PauseAtAsync(host, NetCoreDebugTargets.CatchingThrowLine);

            Assert.True(SessionOf(w).StoppedOnThrow);
        }
        finally
        {
            await w.StopDebuggingForTests();
        }
    }

    [AvaloniaFact]
    public async Task TheDebugMenuOffersExceptionSettingsAndAttachToProcess()
    {
        using var host = new TestWindow();
        await host.SettleAsync(5);

        var debug = host.Window.MenuEntriesForTests().Single(e => e.Header == L(StringKeys.MenuDebug));

        Assert.Contains(debug.Children!, e => e.Header == L(StringKeys.MenuDebugExceptionSettings));
        Assert.Contains(debug.Children!, e => e.Header == L(StringKeys.MenuDebugAttachToProcess));
    }

    [AvaloniaFact]
    public async Task ExceptionSettingsStartsFromTheSavedChoiceAndReturnsWhatWasTicked()
    {
        var dialog = new ExceptionSettingsDialog(new ExceptionBreaks(WhenThrown: false, WhenUserUnhandled: true));

        Assert.False(dialog.WhenThrownBoxForTests.IsChecked);
        Assert.True(dialog.UserUnhandledBoxForTests.IsChecked);

        dialog.WhenThrownBoxForTests.IsChecked = true;
        dialog.UserUnhandledBoxForTests.IsChecked = false;
        dialog.Accept();

        Assert.Equal(new ExceptionBreaks(true, false), dialog.Result);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task TheSavedChoiceIsGivenToTheSessionWhenTheWindowOpens()
    {
        // The settings of a previous run: break when thrown.
        var settings = new SettingsStore(_settings);
        var saved = settings.Load();
        saved.Debug.BreakWhenExceptionThrown = true;
        settings.Save(saved);

        Assert.SkipWhen(AdapterMissing, "netcoredbg is not installed.");

        using var host = await OpenProgramAsync(NetCoreDebugTargets.CatchingThrower);
        var w = host.Window;

        try
        {
            PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);
            await PauseAtAsync(host, NetCoreDebugTargets.CatchingThrowLine);
        }
        finally
        {
            await w.StopDebuggingForTests();
        }
    }

    [AvaloniaFact]
    public async Task AWindowWhoseUserNeverChoseGivesTheSessionNoChoiceToSend()
    {
        // Left null, the session sends the debugger no setExceptionBreakpoints at all: netcoredbg keeps its own
        // behaviour, which is to stop on an unhandled exception.
        using var host = new TestWindow();
        await host.SettleAsync(5);

        var field = typeof(Basalt.Shell.ViewModels.DebugSession)
            .GetField("_exceptionBreaks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        Assert.Null(field.GetValue(SessionOf(host.Window)));
    }

    [AvaloniaFact]
    public async Task F5IsRefusedWithAMessageWhileATestRunIsInProgress()
    {
        using var host = await OpenProgramAsync(NetCoreDebugTargets.CatchingThrower);
        var w = host.Window;

        typeof(MainWindow).GetField("_testRunInProgress", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(w, true);

        PressKey(w, Key.F5, RawInputModifiers.None, PhysicalKey.F5);
        await host.SettleAsync(10);

        Assert.Contains(L(StringKeys.DebugTestRunInProgress), OutputOf(w), StringComparison.Ordinal);
        Assert.False(SessionOf(w).IsRunning);
    }
}
