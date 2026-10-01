using System.ComponentModel;

namespace System.Windows.Forms;

public class TreeViewCancelEventArgs : CancelEventArgs
{
    public TreeViewCancelEventArgs(TreeNode? node, bool cancel, TreeViewAction action)
        : base(cancel)
    {
        Node = node;
        Action = action;
    }

    public TreeViewAction Action { get; }

    public TreeNode? Node { get; }
}
