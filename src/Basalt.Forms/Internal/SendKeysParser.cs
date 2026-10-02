using System.Linq;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms.Internal;

/// <summary>
/// Parses a SendKeys string (plain text, {NAME} and {NAME n} for a special
/// key repeated n times, +^% held for the next key or a (...) group, ~ for
/// Enter) and delivers every key through the same key messages a real one
/// would produce, via <see cref="Control.DispatchKeyMessage"/> on the
/// focused control of <see cref="Form.ActiveForm"/>.
/// </summary>
internal static class SendKeysParser
{
    private static readonly Dictionary<string, Keys> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENTER"] = Keys.Enter,
        ["TAB"] = Keys.Tab,
        ["ESC"] = Keys.Escape,
        ["ESCAPE"] = Keys.Escape,
        ["BACKSPACE"] = Keys.Back,
        ["BS"] = Keys.Back,
        ["BKSP"] = Keys.Back,
        ["DELETE"] = Keys.Delete,
        ["DEL"] = Keys.Delete,
        ["INSERT"] = Keys.Insert,
        ["INS"] = Keys.Insert,
        ["HOME"] = Keys.Home,
        ["END"] = Keys.End,
        ["PGUP"] = Keys.PageUp,
        ["PGDN"] = Keys.PageDown,
        ["UP"] = Keys.Up,
        ["DOWN"] = Keys.Down,
        ["LEFT"] = Keys.Left,
        ["RIGHT"] = Keys.Right,
        ["SPACE"] = Keys.Space,
        ["CAPSLOCK"] = Keys.Capital,
        ["NUMLOCK"] = Keys.NumLock,
        ["SCROLLLOCK"] = Keys.Scroll,
        ["PRTSC"] = Keys.PrintScreen,
        ["HELP"] = Keys.Help,
        ["PAUSE"] = Keys.Pause,
        ["F1"] = Keys.F1,
        ["F2"] = Keys.F2,
        ["F3"] = Keys.F3,
        ["F4"] = Keys.F4,
        ["F5"] = Keys.F5,
        ["F6"] = Keys.F6,
        ["F7"] = Keys.F7,
        ["F8"] = Keys.F8,
        ["F9"] = Keys.F9,
        ["F10"] = Keys.F10,
        ["F11"] = Keys.F11,
        ["F12"] = Keys.F12,
        ["F13"] = Keys.F13,
        ["F14"] = Keys.F14,
        ["F15"] = Keys.F15,
        ["F16"] = Keys.F16,
    };

    public static void Parse(string keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        ParseRange(keys, 0, keys.Length, Keys.None);
    }

    /// <summary>Parses from <paramref name="index"/> to a matching ')' or <paramref name="end"/>; returns the index just after it.</summary>
    private static int ParseRange(string s, int index, int end, Keys inherited)
    {
        var pending = inherited;

        while (index < end)
        {
            switch (s[index])
            {
                case '+':
                    pending |= Keys.Shift;
                    index++;
                    break;

                case '^':
                    pending |= Keys.Control;
                    index++;
                    break;

                case '%':
                    pending |= Keys.Alt;
                    index++;
                    break;

                case '~':
                    SendSpecial(Keys.Enter, pending);
                    pending = inherited;
                    index++;
                    break;

                case '(':
                    index = ParseRange(s, index + 1, end, pending);
                    pending = inherited;
                    break;

                case ')':
                    return index + 1;

                case '{':
                    index = ParseBraced(s, index, end, pending);
                    pending = inherited;
                    break;

                default:
                    SendChar(s[index], pending);
                    pending = inherited;
                    index++;
                    break;
            }
        }

        return index;
    }

    /// <summary>A {...} token: a literal brace/parenthesis/modifier character, or NAME with an optional repeat count.</summary>
    private static int ParseBraced(string s, int index, int end, Keys modifiers)
    {
        var close = s.IndexOf('}', index + 1);
        if (close < 0) throw new ArgumentException("Sent keys has an unmatched '{'.");

        var token = s.Substring(index + 1, close - index - 1).Trim();
        var spaceAt = token.LastIndexOf(' ');
        var count = 1;
        var name = token;

        if (spaceAt > 0 && int.TryParse(token[(spaceAt + 1)..], out var parsed))
        {
            name = token[..spaceAt];
            count = parsed;
        }

        for (var i = 0; i < count; i++)
        {
            if (name.Length == 1)
            {
                SendChar(name[0], modifiers);
            }
            else if (Names.TryGetValue(name, out var key))
            {
                SendSpecial(key, modifiers);
            }
            else
            {
                throw new ArgumentException($"'{name}' is not a recognized key.");
            }
        }

        return close + 1;
    }

    private static void SendChar(char c, Keys modifiers)
    {
        if (ActiveTarget() is not { } target) return;

        if (modifiers != Keys.None && char.IsLetterOrDigit(c) && c < 128)
        {
            SendSpecial((Keys)char.ToUpperInvariant(c), modifiers);
            return;
        }

        // Punctuation has no Keys code worth guessing at, but a held modifier (^!, %;) is still
        // held for it, the same way SendSpecial holds one for a named key: a handler reading
        // Control.ModifierKeys from OnKeyPress must see it too.
        var previous = FormsInput.Modifiers;
        FormsInput.Modifiers = modifiers;

        try
        {
            var message = Message.Create(target.Handle, WindowMessages.Char, (IntPtr)c, IntPtr.Zero);
            target.DispatchKeyMessage(ref message);
        }
        finally
        {
            FormsInput.Modifiers = previous;
        }
    }

    private static void SendSpecial(Keys key, Keys modifiers)
    {
        if (ActiveTarget() is not { } target) return;

        var previous = FormsInput.Modifiers;
        FormsInput.Modifiers = modifiers;

        try
        {
            FormsInputState.SuppressNextCharacter = false;

            var down = Message.Create(target.Handle, WindowMessages.KeyDown, (IntPtr)(int)key, IntPtr.Zero);
            target.DispatchKeyMessage(ref down);

            // Enter, Backspace, Tab and Escape (and Ctrl+letter) type no glyph of their own, but a
            // real keyboard still sends their control character as WM_CHAR: a text box's own
            // deletion, newline and so on run off that, not off WM_KEYDOWN.
            if (!FormsInputState.SuppressNextCharacter && KeyMapping.ControlCharacter(key, modifiers) is { } character)
            {
                var charMessage = Message.Create(target.Handle, WindowMessages.Char, (IntPtr)character, IntPtr.Zero);
                target.DispatchKeyMessage(ref charMessage);
            }

            var up = Message.Create(target.Handle, WindowMessages.KeyUp, (IntPtr)(int)key, IntPtr.Zero);
            target.DispatchKeyMessage(ref up);
        }
        finally
        {
            FormsInput.Modifiers = previous;
        }
    }

    private static Control? ActiveTarget()
    {
        // Form.ActiveForm needs the host window to have reported activation, which a headless
        // test's window manager may never do; the only (or last shown) open form is as close to
        // "the active window" as there is without one, and is right whenever there is only one.
        var open = Application.OpenForms.Cast<Form>();
        var form = Form.ActiveForm;
        if (form is null || !open.Contains(form)) form = open.LastOrDefault();

        return form is null ? null : form.WindowFocus ?? form;
    }
}
