namespace System.Windows.Forms;

public class TaskDialogLinkClickedEventArgs : EventArgs
{
    public TaskDialogLinkClickedEventArgs(string linkHref)
    {
        LinkHref = linkHref;
    }

    public string LinkHref { get; }
}
