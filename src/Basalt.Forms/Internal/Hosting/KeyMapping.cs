using Avalonia.Input;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>
/// Translates Avalonia's keys, which follow WPF's numbering, into WinForms'
/// virtual-key codes. Command on macOS is Control, so ⌘C is the Ctrl+C
/// applications already handle.
/// </summary>
internal static class KeyMapping
{
    private static readonly Dictionary<Key, Keys> Special = new()
    {
        [Key.Back] = Keys.Back,
        [Key.Tab] = Keys.Tab,
        [Key.Enter] = Keys.Enter,
        [Key.Pause] = Keys.Pause,
        [Key.CapsLock] = Keys.CapsLock,
        [Key.Escape] = Keys.Escape,
        [Key.Space] = Keys.Space,
        [Key.PageUp] = Keys.PageUp,
        [Key.PageDown] = Keys.PageDown,
        [Key.End] = Keys.End,
        [Key.Home] = Keys.Home,
        [Key.Left] = Keys.Left,
        [Key.Up] = Keys.Up,
        [Key.Right] = Keys.Right,
        [Key.Down] = Keys.Down,
        [Key.PrintScreen] = Keys.PrintScreen,
        [Key.Insert] = Keys.Insert,
        [Key.Delete] = Keys.Delete,
        [Key.Help] = Keys.Help,
        [Key.LWin] = Keys.LWin,
        [Key.RWin] = Keys.RWin,
        [Key.Apps] = Keys.Apps,
        [Key.Sleep] = Keys.Sleep,
        [Key.Multiply] = Keys.Multiply,
        [Key.Add] = Keys.Add,
        [Key.Separator] = Keys.Separator,
        [Key.Subtract] = Keys.Subtract,
        [Key.Decimal] = Keys.Decimal,
        [Key.Divide] = Keys.Divide,
        [Key.NumLock] = Keys.NumLock,
        [Key.Scroll] = Keys.Scroll,
        [Key.LeftShift] = Keys.ShiftKey,
        [Key.RightShift] = Keys.ShiftKey,
        [Key.LeftCtrl] = Keys.ControlKey,
        [Key.RightCtrl] = Keys.ControlKey,
        [Key.LeftAlt] = Keys.Menu,
        [Key.RightAlt] = Keys.Menu,
        [Key.OemSemicolon] = Keys.OemSemicolon,
        [Key.OemPlus] = Keys.Oemplus,
        [Key.OemComma] = Keys.Oemcomma,
        [Key.OemMinus] = Keys.OemMinus,
        [Key.OemPeriod] = Keys.OemPeriod,
        [Key.OemQuestion] = Keys.OemQuestion,
        [Key.OemTilde] = Keys.Oemtilde,
        [Key.OemOpenBrackets] = Keys.OemOpenBrackets,
        [Key.OemPipe] = Keys.OemPipe,
        [Key.OemCloseBrackets] = Keys.OemCloseBrackets,
        [Key.OemQuotes] = Keys.OemQuotes,
        [Key.Oem8] = Keys.Oem8,
        [Key.OemBackslash] = Keys.OemBackslash,
    };

    /// <summary>The WinForms key code; Keys.None for a key WinForms has no code for.</summary>
    public static Keys ToKeys(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return Keys.A + (key - Key.A);
        if (key is >= Key.D0 and <= Key.D9) return Keys.D0 + (key - Key.D0);
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return Keys.NumPad0 + (key - Key.NumPad0);
        if (key is >= Key.F1 and <= Key.F24) return Keys.F1 + (key - Key.F1);

        // The Command keys are the macOS Control keys.
        if (OperatingSystem.IsMacOS() && key is Key.LWin or Key.RWin) return Keys.ControlKey;

        return Special.GetValueOrDefault(key, Keys.None);
    }

    public static Keys ToModifiers(KeyModifiers modifiers)
    {
        var keys = Keys.None;
        if ((modifiers & KeyModifiers.Shift) != 0) keys |= Keys.Shift;
        if ((modifiers & KeyModifiers.Control) != 0) keys |= Keys.Control;
        if ((modifiers & KeyModifiers.Alt) != 0) keys |= Keys.Alt;
        if (OperatingSystem.IsMacOS() && (modifiers & KeyModifiers.Meta) != 0) keys |= Keys.Control;

        return keys;
    }

    /// <summary>
    /// The character Windows sends as WM_CHAR for a key that types no text:
    /// Enter, Backspace, Tab, Escape, and Ctrl with a letter.
    /// </summary>
    public static char? ControlCharacter(Keys keyCode, Keys modifiers)
    {
        if ((modifiers & Keys.Alt) != 0) return null;

        if ((modifiers & Keys.Control) != 0)
        {
            if (keyCode is >= Keys.A and <= Keys.Z) return (char)(keyCode - Keys.A + 1);

            return null;
        }

        return keyCode switch
        {
            Keys.Enter => '\r',
            Keys.Back => '\b',
            Keys.Tab => '\t',
            Keys.Escape => '\u001b',
            _ => null,
        };
    }
}
