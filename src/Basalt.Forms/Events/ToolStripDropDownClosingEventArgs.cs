using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>Why a <see cref="ToolStripDropDown"/> is about to close; a handler may cancel it.</summary>
public class ToolStripDropDownClosingEventArgs : CancelEventArgs
{
    public ToolStripDropDownClosingEventArgs(ToolStripDropDownCloseReason reason)
    {
        CloseReason = reason;
    }

    public ToolStripDropDownCloseReason CloseReason { get; }
}
