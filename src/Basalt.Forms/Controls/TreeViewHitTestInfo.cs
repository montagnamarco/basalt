namespace System.Windows.Forms;

/// <summary>What <see cref="TreeView.HitTest(Drawing.Point)"/> found at a point: the node, and which part of its row.</summary>
public class TreeViewHitTestInfo
{
    public TreeViewHitTestInfo(TreeNode? hitNode, TreeViewHitTestLocations hitLocation)
    {
        Node = hitNode;
        Location = hitLocation;
    }

    public TreeViewHitTestLocations Location { get; }

    public TreeNode? Node { get; }
}
