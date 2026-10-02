using System.Drawing;

namespace System.Windows.Forms;

public class DrawTreeNodeEventArgs : EventArgs
{
    public DrawTreeNodeEventArgs(Graphics graphics, TreeNode? node, Rectangle bounds, TreeNodeStates state)
    {
        Graphics = graphics;
        Node = node;
        Bounds = bounds;
        State = state;
    }

    public Rectangle Bounds { get; }

    public bool DrawDefault { get; set; }

    public Graphics Graphics { get; }

    public TreeNode? Node { get; }

    public TreeNodeStates State { get; }
}
