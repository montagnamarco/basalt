using Basalt.Core.Services;
using Basalt.Core.Settings;

namespace Basalt.Shell;

/// <summary>
/// Debug &gt; Exception Settings: which exceptions stop the debugger. Kept in the IDE settings, handed to the
/// session when the window opens and again whenever the user changes it, so a session under way follows at once.
/// </summary>
public partial class MainWindow
{
    /// <summary>The choice the settings hold.</summary>
    internal static ExceptionBreaks ExceptionBreaksOf(IdeSettings settings) =>
        new(settings.Debug.BreakWhenExceptionThrown, settings.Debug.BreakOnUserUnhandledException);

    /// <summary>
    /// Gives the session the saved choice, when there is one to give. Left alone when both boxes are off, so that a
    /// session nobody configured sends the debugger nothing and netcoredbg keeps its own behaviour; the choice is
    /// only sent once the user has turned something on (and from then on, off again too).
    /// </summary>
    private void ApplySavedExceptionBreaks()
    {
        var saved = ExceptionBreaksOf(_settingsStore.Load());

        if (!saved.WhenThrown && !saved.WhenUserUnhandled) return;

        Guarded.Run(() => _debug.SetExceptionBreaksAsync(saved), ViewModel.WriteOutput, "debug");
    }

    private async Task ShowExceptionSettingsAsync()
    {
        var settings = _settingsStore.Load();
        var dialog = new ExceptionSettingsDialog(ExceptionBreaksOf(settings));

        await dialog.ShowDialog(this);

        if (dialog.Result is { } chosen) await ChangeExceptionBreaksAsync(chosen);
    }

    /// <summary>Saves a choice and sends it to the session, running or not.</summary>
    internal async Task ChangeExceptionBreaksAsync(ExceptionBreaks chosen)
    {
        var settings = _settingsStore.Load();

        settings.Debug.BreakWhenExceptionThrown = chosen.WhenThrown;
        settings.Debug.BreakOnUserUnhandledException = chosen.WhenUserUnhandled;
        _settingsStore.Save(settings);

        await _debug.SetExceptionBreaksAsync(chosen);
    }
}
