using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Basalt.Core.Commands;
using Basalt.Core.Settings;
using Basalt.Shell;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>Recording a shortcut by pressing it.</summary>
public class ShortcutRecorderTests
{
    [AvaloniaFact]
    public void RecordsWhatWasPressed()
    {
        var recorder = new ShortcutRecorder();

        recorder.RecordForTests(Key.S, KeyModifiers.Control);

        Assert.Equal("Ctrl+S", recorder.Gesture);
    }

    [AvaloniaFact]
    public void ReportsWhatItRecorded()
    {
        var recorder = new ShortcutRecorder();

        string? reported = null;
        recorder.Recorded += (_, gesture) => reported = gesture;

        recorder.RecordForTests(Key.F5, KeyModifiers.Shift);

        Assert.Equal("Shift+F5", reported);
    }

    [Theory]
    [InlineData(Key.S, KeyModifiers.Control, "Ctrl+S")]
    [InlineData(Key.S, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+S")]
    [InlineData(Key.F12, KeyModifiers.None, "F12")]
    [InlineData(Key.D1, KeyModifiers.Control, "Ctrl+1")]
    [InlineData(Key.OemComma, KeyModifiers.Control, "Ctrl+,")]
    [InlineData(Key.OemPeriod, KeyModifiers.Control, "Ctrl+.")]
    public void SpellsACombinationTheWayTheRegistryDoes(
        Key key, KeyModifiers modifiers, string expected)
    {
        // The two have to agree, or a recorded shortcut matches nothing.
        Assert.Equal(expected, ShortcutRecorder.Describe(key, modifiers));
    }

    [Fact]
    public void WritesTheCommandModifierTheSameOnEveryPlatform()
    {
        // A Mac user presses Cmd, but one spelling means settings carry
        // between machines.
        Assert.Equal("Ctrl+S", ShortcutRecorder.Describe(Key.S, KeyModifiers.Meta));
        Assert.Equal("Ctrl+S", ShortcutRecorder.Describe(Key.S, KeyModifiers.Control));
    }

    [Fact]
    public void EveryDefaultShortcutCanBeRecorded()
    {
        // If the recorder cannot spell a default, a user pressing that
        // combination would appear to be assigning something new.
        var registry = IdeCommands.CreateRegistry();

        var unspellable = registry.All
            .Select(c => registry.GestureFor(c.Id))
            .Where(g => g is { Length: > 0 })
            .Where(g => !g!.Contains(',')) // Chords are a separate matter.
            .Where(g => !CanBeRecorded(g!))
            .ToList();

        Assert.Empty(unspellable);
    }

    /// <summary>Whether the recorder can produce this gesture from a key press.</summary>
    private static bool CanBeRecorded(string gesture)
    {
        // "Ctrl++" is Ctrl and the plus key, and splitting on '+' throws the
        // key away: the separator and the key are the same character. The
        // trailing one is taken off first so the rest splits cleanly.
        var plusIsTheKey = gesture.EndsWith("++", StringComparison.Ordinal);

        var parts = (plusIsTheKey ? gesture[..^1] : gesture)
            .Split('+', StringSplitOptions.RemoveEmptyEntries);

        var keyName = plusIsTheKey ? "+" : parts[^1];

        var modifiers = KeyModifiers.None;

        foreach (var part in plusIsTheKey ? parts : parts[..^1])
        {
            modifiers |= part switch
            {
                "Ctrl" => KeyModifiers.Control,
                "Shift" => KeyModifiers.Shift,
                "Alt" => KeyModifiers.Alt,
                _ => KeyModifiers.None
            };
        }

        var key = keyName switch
        {
            "," => Key.OemComma,
            "." => Key.OemPeriod,
            "-" => Key.OemMinus,
            "+" => Key.OemPlus,
            "/" => Key.Oem2,
            "`" => Key.Oem3,
            _ when keyName.Length == 1 && char.IsDigit(keyName[0]) =>
                Key.D0 + (keyName[0] - '0'),
            _ => Enum.TryParse<Key>(keyName, out var parsed) ? parsed : Key.None
        };

        return key != Key.None && ShortcutRecorder.Describe(key, modifiers) == gesture;
    }
}

/// <summary>Shortcuts surviving a restart, and taking effect when changed.</summary>
public sealed class ShortcutPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-shortcuts", Guid.NewGuid().ToString("N"));

    private SettingsStore Store => new(Path.Combine(_root, "settings.json"));

    public ShortcutPersistenceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void KeepsAShortcutTheUserAssigned()
    {
        var store = Store;

        var settings = store.Load();
        settings.Keyboard.Shortcuts["file.save"] = "Ctrl+Alt+S";
        store.Save(settings);

        var reloaded = new SettingsStore(Path.Combine(_root, "settings.json")).Load();

        Assert.Equal("Ctrl+Alt+S", reloaded.Keyboard.Shortcuts["file.save"]);
    }

    [Fact]
    public void KeepsTheChosenScheme()
    {
        var store = Store;

        var settings = store.Load();
        settings.Keyboard.Scheme = "VisualStudio";
        store.Save(settings);

        Assert.Equal("VisualStudio",
            new SettingsStore(Path.Combine(_root, "settings.json")).Load().Keyboard.Scheme);
    }

    [Fact]
    public void StartsFromTheDefaultScheme()
    {
        Assert.Equal("Basalt", Store.Load().Keyboard.Scheme);
        Assert.Empty(Store.Load().Keyboard.Shortcuts);
    }

    [Fact]
    public void AReassignedShortcutRunsTheCommandAndTheOldOneDoesNot()
    {
        // The point of making them configurable: the new keys work and the
        // old ones stop.
        var registry = IdeCommands.CreateRegistry();

        registry.ApplyCustomisations(new Dictionary<string, string?>
        {
            [IdeCommands.FileSave] = "Ctrl+Alt+S"
        });

        Assert.Equal(IdeCommands.FileSave, registry.ForGesture("Ctrl+Alt+S")?.Id);
        Assert.Null(registry.ForGesture("Ctrl+S"));
    }

    [Fact]
    public void ChangingTheSchemeKeepsWhatTheUserAssigned()
    {
        // Otherwise choosing a scheme would quietly undo their own choices.
        var registry = IdeCommands.CreateRegistry(KeyboardScheme.VisualStudio);

        registry.ApplyCustomisations(new Dictionary<string, string?>
        {
            [IdeCommands.FileSave] = "Ctrl+Alt+S"
        });

        Assert.Equal("Ctrl+Alt+S", registry.GestureFor(IdeCommands.FileSave));

        // And the scheme's own default still applies to everything else.
        Assert.Equal("Ctrl+Y", registry.GestureFor(IdeCommands.EditRedo));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
