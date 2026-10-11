using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Basalt.Core.Settings;
using Basalt.Shell;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// A command a plugin added, driven the way a person would reach it: found in
/// the command palette, and run from its shortcut. Saving settings is the
/// journey step that comes between, because the window rebuilds its command
/// registry from the settings when it does.
/// </summary>
public sealed class UserJourneyPluginCommandTests
{
    private const string CommandId = "sample.sayHello";
    private const string CommandTitle = "Say Hello From Plugin";
    private const string Gesture = "Ctrl+Shift+J";

    /// <summary>Settings as the Settings window would save them: the plugin command given a shortcut.</summary>
    private static IdeSettings SettingsWithShortcut()
    {
        var settings = new SettingsStore().Load();
        settings.Keyboard.Shortcuts[CommandId] = Gesture;
        return settings;
    }

    private static void PressKey(TopLevel window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }

    private static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, int maxPasses = 100)
    {
        for (var i = 0; i < maxPasses; i++)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }
        return condition();
    }

    private static async Task<T?> WaitForOwnedAsync<T>(TestWindow host, int maxPasses = 100)
        where T : Window
    {
        for (var i = 0; i < maxPasses; i++)
        {
            var found = host.Window.OwnedWindows.OfType<T>().FirstOrDefault();
            if (found is not null) return found;
            await host.SettleAsync(1);
        }
        return host.Window.OwnedWindows.OfType<T>().FirstOrDefault();
    }

    /// <summary>
    /// Ctrl+Shift+P finds the plugin's command after settings were saved.
    /// </summary>
    /// <remarks>
    /// Before the fix, saving settings rebuilt the command registry from the
    /// settings alone, so the command the plugin had added was gone from the
    /// palette until the IDE was restarted.
    /// </remarks>
    [AvaloniaFact]
    public async Task APluginCommandStaysInThePaletteAfterSettingsAreSaved()
    {
        using var host = new TestWindow();
        await host.SettleAsync(10);
        var w = host.Window;

        w.AddPluginCommandForTests(CommandId, CommandTitle, () => { });
        w.ApplySettingsForTests(SettingsWithShortcut());

        PressKey(w, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.P);

        var palette = await WaitForOwnedAsync<CommandPalette>(host);
        Assert.NotNull(palette);

        palette!.KeyTextInput("say hello from plugin");
        await host.SettleAsync(3);

        Assert.Contains(palette.Matches, command => command.Id == CommandId);
    }

    /// <summary>
    /// The shortcut a user gave a plugin command still runs it after settings
    /// were saved.
    /// </summary>
    /// <remarks>
    /// Before the fix, the shortcut was kept in the settings but the command it
    /// names was not in the rebuilt registry, so the key press matched nothing.
    /// </remarks>
    [AvaloniaFact]
    public async Task APluginCommandsShortcutStillRunsItAfterSettingsAreSaved()
    {
        using var host = new TestWindow();
        await host.SettleAsync(10);
        var w = host.Window;

        var runs = 0;
        w.AddPluginCommandForTests(CommandId, CommandTitle, () => runs++);
        w.ApplySettingsForTests(SettingsWithShortcut());

        PressKey(w, Key.J, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.J);

        Assert.True(await WaitUntilAsync(host, () => runs == 1),
            "The shortcut did not run the plugin's command after settings were saved.");
    }
}
