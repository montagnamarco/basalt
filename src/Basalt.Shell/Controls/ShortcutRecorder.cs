using Avalonia.Controls;
using Avalonia.Input;

namespace Basalt.Shell.Controls;

/// <summary>
/// A box that records the combination pressed into it.
///
/// Typing a shortcut by hand means knowing how the IDE spells it; pressing it
/// does not. Modifiers alone are ignored, since holding Ctrl on the way to a
/// key is not a shortcut.
/// </summary>
public sealed class ShortcutRecorder : TextBox
{
    public ShortcutRecorder()
    {
        IsReadOnly = true;
        PlaceholderText = "Press a shortcut";

        // The key has to be seen before the editor turns it into text, and
        // before any parent acts on it as a shortcut of its own.
        AddHandler(KeyDownEvent, OnRecordingKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Raised when a combination has been pressed.</summary>
    public event EventHandler<string>? Recorded;

    /// <summary>The combination as the registry spells it.</summary>
    public string Gesture
    {
        get => Text ?? "";
        set => Text = value;
    }

    internal void RecordForTests(Key key, KeyModifiers modifiers) =>
        Record(key, modifiers);

    private void OnRecordingKey(object? sender, KeyEventArgs e)
    {
        // Escape gives up without recording; Backspace clears the shortcut.
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Back or Key.Delete && e.KeyModifiers == KeyModifiers.None)
        {
            Text = "";
            Recorded?.Invoke(this, "");
            e.Handled = true;
            return;
        }

        if (IsModifier(e.Key))
        {
            // Holding Ctrl on the way to a key is not itself a shortcut.
            e.Handled = true;
            return;
        }

        Record(e.Key, e.KeyModifiers);
        e.Handled = true;
    }

    private void Record(Key key, KeyModifiers modifiers)
    {
        var gesture = Describe(key, modifiers);

        Text = gesture;
        Recorded?.Invoke(this, gesture);
    }

    private static bool IsModifier(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt
        or Key.LWin or Key.RWin;

    /// <summary>
    /// Spells a combination the way the registry does.
    ///
    /// The command modifier is written as "Ctrl" on every platform: what a Mac
    /// user presses is Cmd, and the platform substitutes it, but one spelling
    /// means settings carry between machines.
    /// </summary>
    internal static string Describe(Key key, KeyModifiers modifiers)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta))
            parts.Add("Ctrl");

        if (modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");

        parts.Add(NameOf(key));

        return string.Join("+", parts);
    }

    /// <summary>The key as a gesture spells it.</summary>
    private static string NameOf(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemMinus => "-",
        Key.OemPlus => "+",
        Key.Oem2 => "/",
        Key.Oem3 => "`",
        _ => key.ToString()
    };
}
