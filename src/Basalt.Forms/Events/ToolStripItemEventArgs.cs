namespace System.Windows.Forms;

public class ToolStripItemEventArgs : EventArgs
{
    public ToolStripItemEventArgs(ToolStripItem? item)
    {
        Item = item;
    }

    public ToolStripItem? Item { get; }
}
