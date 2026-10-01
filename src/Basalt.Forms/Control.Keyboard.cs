using System.ComponentModel;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// The keyboard pipeline of WinForms, driven by synthesized key messages:
/// first the control and its parents may claim a key as a command or a
/// dialog key (Tab, Enter, Escape, mnemonics), and only then does the
/// focused control receive KeyDown, KeyPress and KeyUp.
/// </summary>
public partial class Control
{
    /// <summary>Set when PreviewKeyDown or IsInputKey claimed the key for the control, skipping dialog processing.</summary>
    private bool _inputKey;

    private bool _inputChar;

    public event KeyEventHandler? KeyDown
    {
        add => Events.AddHandler(s_keyDownEvent, value);
        remove => Events.RemoveHandler(s_keyDownEvent, value);
    }

    public event KeyEventHandler? KeyUp
    {
        add => Events.AddHandler(s_keyUpEvent, value);
        remove => Events.RemoveHandler(s_keyUpEvent, value);
    }

    public event KeyPressEventHandler? KeyPress
    {
        add => Events.AddHandler(s_keyPressEvent, value);
        remove => Events.RemoveHandler(s_keyPressEvent, value);
    }

    public event PreviewKeyDownEventHandler? PreviewKeyDown
    {
        add => Events.AddHandler(s_previewKeyDownEvent, value);
        remove => Events.RemoveHandler(s_previewKeyDownEvent, value);
    }

    private static readonly object s_keyDownEvent = new();
    private static readonly object s_keyUpEvent = new();
    private static readonly object s_keyPressEvent = new();
    private static readonly object s_previewKeyDownEvent = new();

    protected virtual void OnKeyDown(KeyEventArgs e) => ((KeyEventHandler?)Events[s_keyDownEvent])?.Invoke(this, e);

    protected virtual void OnKeyUp(KeyEventArgs e) => ((KeyEventHandler?)Events[s_keyUpEvent])?.Invoke(this, e);

    protected virtual void OnKeyPress(KeyPressEventArgs e) => ((KeyPressEventHandler?)Events[s_keyPressEvent])?.Invoke(this, e);

    protected virtual void OnPreviewKeyDown(PreviewKeyDownEventArgs e) => ((PreviewKeyDownEventHandler?)Events[s_previewKeyDownEvent])?.Invoke(this, e);

    /// <summary>
    /// Runs a key message through the WinForms pipeline at this control;
    /// true when something handled it.
    /// </summary>
    internal bool DispatchKeyMessage(ref Message message)
    {
        if (WindowMessages.IsKeyDown(message.Msg))
        {
            var keyData = (Keys)(int)message.WParam | ModifierKeys;
            var preview = new PreviewKeyDownEventArgs(keyData);
            OnPreviewKeyDown(preview);
            _inputKey = preview.IsInputKey;
        }

        if (PreProcessMessage(ref message)) return true;

        return ProcessKeyMessage(ref message);
    }

    /// <summary>
    /// Gives the control and its parents the chance to treat a key as a
    /// command or a dialog key before the control receives it.
    /// </summary>
    public virtual bool PreProcessMessage(ref Message msg)
    {
        if (WindowMessages.IsKeyDown(msg.Msg))
        {
            var keyData = (Keys)(int)msg.WParam | ModifierKeys;

            if (ProcessCmdKey(ref msg, keyData)) return true;

            if (_inputKey || IsInputKey(keyData))
            {
                _inputKey = true;
                return false;
            }

            return ProcessDialogKey(keyData);
        }

        if (WindowMessages.IsChar(msg.Msg))
        {
            var character = (char)(int)msg.WParam;

            if (msg.Msg == WindowMessages.Char && IsInputChar(character))
            {
                _inputChar = true;
                return false;
            }

            return ProcessDialogChar(character);
        }

        return false;
    }

    /// <summary>The same pre-processing, saying whether the message was processed, should go to the control, or neither.</summary>
    public PreProcessControlState PreProcessControlMessage(ref Message msg)
    {
        if (PreProcessMessage(ref msg)) return PreProcessControlState.MessageProcessed;

        return _inputKey || _inputChar ? PreProcessControlState.MessageNeeded : PreProcessControlState.MessageNotNeeded;
    }

    /// <summary>A shortcut: by default the parent decides, up to the form and its menus.</summary>
    protected virtual bool ProcessCmdKey(ref Message msg, Keys keyData) => _parent?.ProcessCmdKey(ref msg, keyData) ?? false;

    /// <summary>A dialog key (Tab, arrows, Enter, Escape): by default the parent decides.</summary>
    protected virtual bool ProcessDialogKey(Keys keyData) => _parent?.ProcessDialogKey(keyData) ?? false;

    protected virtual bool ProcessDialogChar(char charCode) => _parent?.ProcessDialogChar(charCode) ?? false;

    /// <summary>
    /// Whether the control wants a key that would otherwise move the focus
    /// or press a dialog button. A plain control wants none of them, as a
    /// window that does not answer WM_GETDLGCODE.
    /// </summary>
    protected virtual bool IsInputKey(Keys keyData) => (keyData & Keys.Alt) != Keys.Alt && DialogCodeWantsKey(keyData);

    protected virtual bool IsInputChar(char charCode) => DialogCodeWantsChar(charCode);

    /// <summary>What a native control answers to WM_GETDLGCODE about a key: a plain window wants none.</summary>
    internal virtual bool DialogCodeWantsKey(Keys keyData) => false;

    /// <summary>What a native control answers to WM_GETDLGCODE about characters: an edit control wants them all.</summary>
    internal virtual bool DialogCodeWantsChar(char charCode) => false;

    /// <summary>A mnemonic (the letter after an ampersand): only controls that have one answer.</summary>
    protected internal virtual bool ProcessMnemonic(char charCode) => false;

    /// <summary>Lets a parent with KeyPreview see the key first, then raises the key event on this control.</summary>
    protected virtual bool ProcessKeyMessage(ref Message m)
    {
        if (_parent is not null && _parent.ProcessKeyPreview(ref m)) return true;

        return ProcessKeyEventArgs(ref m);
    }

    /// <summary>A parent's look at a child's key; a form with KeyPreview raises its own key events here.</summary>
    protected virtual bool ProcessKeyPreview(ref Message m) => _parent?.ProcessKeyPreview(ref m) ?? false;

    /// <summary>Raises KeyDown, KeyPress or KeyUp from the message; true when the handler handled it.</summary>
    protected virtual bool ProcessKeyEventArgs(ref Message m)
    {
        if (WindowMessages.IsChar(m.Msg))
        {
            var press = new KeyPressEventArgs((char)(int)m.WParam);
            OnKeyPress(press);
            _inputChar = false;

            if (!press.Handled) DefaultKeyPress(press);

            // A handler may replace the character, which the control then receives.
            m.WParam = press.KeyChar;

            return press.Handled;
        }

        var keyData = (Keys)(int)m.WParam | ModifierKeys;
        var arguments = new KeyEventArgs(keyData);

        if (WindowMessages.IsKeyDown(m.Msg))
        {
            OnKeyDown(arguments);
            _inputKey = false;
            if (arguments.SuppressKeyPress) FormsInputState.SuppressNextCharacter = true;

            if (!arguments.Handled) DefaultKeyDown(arguments);
        }
        else
        {
            OnKeyUp(arguments);
            if (!arguments.Handled) DefaultKeyUp(arguments);
        }

        return arguments.Handled;
    }

    protected void RaiseKeyEvent(object key, KeyEventArgs e) => ((KeyEventHandler?)Events[key])?.Invoke(this, e);

    protected void RaiseMouseEvent(object key, MouseEventArgs e) => ((MouseEventHandler?)Events[key])?.Invoke(this, e);

    protected void RaisePaintEvent(object key, PaintEventArgs e) => ((PaintEventHandler?)Events[key])?.Invoke(this, e);

    /// <summary>Whether the character follows an ampersand in the text, as a mnemonic does.</summary>
    public static bool IsMnemonic(char charCode, string? text)
    {
        if (charCode == '&' || string.IsNullOrEmpty(text)) return false;

        var position = -1;
        var upper = char.ToUpperInvariant(charCode);

        while (true)
        {
            if (position + 1 >= text.Length) return false;

            position = text.IndexOf('&', position + 1) + 1;
            if (position <= 0 || position >= text.Length) return false;

            // A doubled ampersand is a literal one.
            var candidate = char.ToUpperInvariant(text[position]);
            if (candidate == upper || char.ToUpperInvariant(char.ToLowerInvariant(text[position])) == upper) return true;
        }
    }
}

/// <summary>Keyboard state that outlives one message: a KeyDown that suppressed the KeyPress after it.</summary>
internal static class FormsInputState
{
    [ThreadStatic]
    public static bool SuppressNextCharacter;
}
