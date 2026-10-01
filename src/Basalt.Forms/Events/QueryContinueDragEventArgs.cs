namespace System.Windows.Forms;

public class QueryContinueDragEventArgs : EventArgs
{
    public QueryContinueDragEventArgs(int keyState, bool escapePressed, DragAction action)
    {
        KeyState = keyState;
        EscapePressed = escapePressed;
        Action = action;
    }

    public DragAction Action { get; set; }

    public bool EscapePressed { get; }

    public int KeyState { get; }
}
