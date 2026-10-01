using System.ComponentModel;

namespace System.Windows.Forms;

public class FormClosingEventArgs : CancelEventArgs
{
    public FormClosingEventArgs(CloseReason closeReason, bool cancel)
        : base(cancel)
    {
        CloseReason = closeReason;
    }

    public CloseReason CloseReason { get; }
}
