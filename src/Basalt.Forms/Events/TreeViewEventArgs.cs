namespace System.Windows.Forms;

public class TreeViewEventArgs : EventArgs
{
    public TreeViewEventArgs(TreeNode? node)
    {
        Node = node;
    }

    public TreeViewEventArgs(TreeNode? node, TreeViewAction action)
    {
        Node = node;
        Action = action;
    }

    public TreeViewAction Action { get; }

    public TreeNode? Node { get; }
}
