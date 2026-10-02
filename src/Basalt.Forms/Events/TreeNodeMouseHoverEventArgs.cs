namespace System.Windows.Forms;

public class TreeNodeMouseHoverEventArgs : EventArgs
{
    public TreeNodeMouseHoverEventArgs(TreeNode? node)
    {
        Node = node;
    }

    public TreeNode? Node { get; }
}
