namespace System.Windows.Forms;

/// <summary>A key with its modifiers, as KeyDown and KeyUp report it.</summary>
public class KeyEventArgs : EventArgs
{
    private bool _suppressKeyPress;

    public KeyEventArgs(Keys keyData)
    {
        KeyData = keyData;
    }

    public virtual bool Alt => (KeyData & Keys.Alt) == Keys.Alt;

    public bool Control => (KeyData & Keys.Control) == Keys.Control;

    public virtual bool Shift => (KeyData & Keys.Shift) == Keys.Shift;

    public bool Handled { get; set; }

    /// <summary>The key without modifiers; a modifier pressed alone is reported as itself.</summary>
    public Keys KeyCode
    {
        get
        {
            var code = KeyData & Keys.KeyCode;

            // An undefined value is no key at all, as in WinForms.
            return Enum.IsDefined(code) ? code : Keys.None;
        }
    }

    public int KeyValue => (int)(KeyData & Keys.KeyCode);

    public Keys KeyData { get; }

    public Keys Modifiers => KeyData & Keys.Modifiers;

    /// <summary>Stops the KeyPress that would follow; suppressing implies handled.</summary>
    public bool SuppressKeyPress
    {
        get => _suppressKeyPress;
        set
        {
            _suppressKeyPress = value;
            Handled = value;
        }
    }
}

/// <summary>A character typed, after the keyboard layout and dead keys did their work.</summary>
public class KeyPressEventArgs : EventArgs
{
    public KeyPressEventArgs(char keyChar)
    {
        KeyChar = keyChar;
    }

    public char KeyChar { get; set; }

    public bool Handled { get; set; }
}

/// <summary>A key about to reach a control, which may claim it as an input key before dialog processing.</summary>
public class PreviewKeyDownEventArgs : EventArgs
{
    public PreviewKeyDownEventArgs(Keys keyData)
    {
        KeyData = keyData;
    }

    public bool Alt => (KeyData & Keys.Alt) == Keys.Alt;

    public bool Control => (KeyData & Keys.Control) == Keys.Control;

    public bool Shift => (KeyData & Keys.Shift) == Keys.Shift;

    public Keys KeyCode
    {
        get
        {
            var code = KeyData & Keys.KeyCode;

            return Enum.IsDefined(code) ? code : Keys.None;
        }
    }

    public int KeyValue => (int)(KeyData & Keys.KeyCode);

    public Keys KeyData { get; }

    public Keys Modifiers => KeyData & Keys.Modifiers;

    public bool IsInputKey { get; set; }
}
