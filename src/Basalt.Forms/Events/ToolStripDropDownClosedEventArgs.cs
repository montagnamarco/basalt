namespace System.Windows.Forms;

/// <summary>Why a <see cref="ToolStripDropDown"/> just closed.</summary>
public class ToolStripDropDownClosedEventArgs : EventArgs
{
    public ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason reason)
    {
        CloseReason = reason;
    }

    public ToolStripDropDownCloseReason CloseReason { get; }
}
