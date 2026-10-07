using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// Focus rectangles and mnemonic underlines. Windows hides them until the
/// keyboard is used when the user asked it to; Basalt always shows both,
/// as Windows does with "underline access keys" turned on, so the painting
/// of every control can rely on the same answer.
/// </summary>
public partial class Control
{
    private static readonly object s_changeUICuesEvent = new();

    protected virtual bool ShowFocusCues => true;

    protected virtual bool ShowKeyboardCues => true;

    [Category("Behavior")]
    public event UICuesEventHandler? ChangeUICues
    {
        add => Events.AddHandler(s_changeUICuesEvent, value);
        remove => Events.RemoveHandler(s_changeUICuesEvent, value);
    }

    protected virtual void OnChangeUICues(UICuesEventArgs e) => ((UICuesEventHandler?)Events[s_changeUICuesEvent])?.Invoke(this, e);
}
