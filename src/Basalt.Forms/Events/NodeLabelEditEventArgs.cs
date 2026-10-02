namespace System.Windows.Forms;

public class NodeLabelEditEventArgs : EventArgs
{
    public NodeLabelEditEventArgs(TreeNode? node)
    {
        Node = node;
    }

    public NodeLabelEditEventArgs(TreeNode? node, string? label)
    {
        Node = node;
        Label = label;
    }

    public bool CancelEdit { get; set; }

    public string? Label { get; }

    public TreeNode? Node { get; }
}
