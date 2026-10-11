using Avalonia.Input;

namespace Basalt.Shell;

/// <summary>
/// Turns the registry's spelling of a shortcut ("Ctrl+Shift+H") into the
/// <see cref="KeyGesture"/> a menu item shows.
///
/// The menus used to carry their own literal gestures, so switching the
/// keyboard scheme changed what the keys did while the menus went on showing
/// the old ones. Reading the registry is what keeps the two in step: the same
/// string the key handler matches is the one the menu prints.
/// </summary>
internal static class MenuShortcuts
{
    /// <summary>
    /// The gesture for a registry spelling, or null when there is none a menu
    /// can show.
    ///
    /// A chord such as "Ctrl+K, Ctrl+D" has no <see cref="KeyGesture"/>: a
    /// menu item holds one key, so the item shows no shortcut rather than the
    /// first half of a chord, which would promise a key that does nothing on
    /// its own.
    /// </summary>
    /// <param name="gesture">The registry's spelling, or null for no shortcut.</param>
    /// <param name="commandModifier">What "Ctrl" stands for: Cmd on macOS, Ctrl elsewhere.</param>
    public static KeyGesture? ToGesture(string? gesture, KeyModifiers commandModifier)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return null;

        var text = gesture.Trim();

        // "Ctrl+," is Settings, and its comma is a key. A chord's comma is
        // followed by the next key, after a space.
        if (text.Contains(", ", StringComparison.Ordinal)) return null;

        // The key is what follows the last '+', except for the '+' key itself,
        // whose spelling ends in one ("Ctrl++").
        var lastPlus = text.LastIndexOf('+');
        var keyName = text.EndsWith("++", StringComparison.Ordinal) ? "+" : text[(lastPlus + 1)..];
        var modifierText = keyName == "+" && text.EndsWith("++", StringComparison.Ordinal)
            ? text[..^2]
            : text[..Math.Max(lastPlus, 0)];

        var key = KeyFor(keyName);
        if (key is null) return null;

        var modifiers = KeyModifiers.None;

        if (modifierText.Length > 0)
        {
            foreach (var part in modifierText.Split('+'))
            {
                var modifier = ModifierFor(part, commandModifier);
                if (modifier is null) return null;
                modifiers |= modifier.Value;
            }
        }

        return new KeyGesture(key.Value, modifiers);
    }

    private static KeyModifiers? ModifierFor(string name, KeyModifiers commandModifier) => name switch
    {
        "Ctrl" => commandModifier,
        "Shift" => KeyModifiers.Shift,
        "Alt" => KeyModifiers.Alt,
        _ => null
    };

    /// <summary>
    /// The key a registry spelling names. The spellings are the ones
    /// <see cref="ShortcutRecorder"/> writes, so every shortcut the recorder
    /// can record can be shown.
    /// </summary>
    private static Key? KeyFor(string name)
    {
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
            return Key.D0 + (name[0] - '0');

        switch (name)
        {
            case ",": return Key.OemComma;
            case ".": return Key.OemPeriod;
            case "-": return Key.OemMinus;
            case "+": return Key.OemPlus;
            case "/": return Key.Oem2;
            case "`": return Key.Oem3;
        }

        return Enum.TryParse<Key>(name, ignoreCase: false, out var key) ? key : null;
    }
}
