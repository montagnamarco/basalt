using System.Drawing;

namespace System.Windows.Forms;

public partial class TreeView
{
    private const int GlyphSize = 9;
    private const int CheckBoxSize = 13;
    private const int ImageGap = 2;

    /// <summary>The geometry of one row: the glyph, the optional check box and image, and the label.</summary>
    private readonly record struct NodeRowLayout(Rectangle Row, Rectangle PlusMinus, Rectangle CheckBox, Rectangle Image, Rectangle Label);

    /// <summary>Every node whose ancestors are all expanded, in document order: what the control could show scrolled to any position.</summary>
    internal IEnumerable<TreeNode> EnumerateExpandedOrder() => EnumerateExpandedOrder(_nodes);

    private static IEnumerable<TreeNode> EnumerateExpandedOrder(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            if (node.IsExpanded)
            {
                foreach (var descendant in EnumerateExpandedOrder(node.Nodes)) yield return descendant;
            }
        }
    }

    internal TreeNode? NextInExpandedOrder(TreeNode node)
    {
        using var sequence = EnumerateExpandedOrder().GetEnumerator();
        while (sequence.MoveNext())
        {
            if (sequence.Current != node) continue;

            return sequence.MoveNext() ? sequence.Current : null;
        }

        return null;
    }

    internal TreeNode? PreviousInExpandedOrder(TreeNode node)
    {
        TreeNode? previous = null;
        foreach (var current in EnumerateExpandedOrder())
        {
            if (current == node) return previous;

            previous = current;
        }

        return null;
    }

    /// <summary>The node's position in the expanded-order sequence, or -1 when it is not reachable (a collapsed ancestor hides it).</summary>
    private int ExpandedOrderIndex(TreeNode node)
    {
        var index = 0;
        foreach (var current in EnumerateExpandedOrder())
        {
            if (current == node) return index;
            index++;
        }

        return -1;
    }

    private TreeNode? NodeAtExpandedOrderIndex(int index)
    {
        if (index < 0) return null;

        var current = 0;
        foreach (var node in EnumerateExpandedOrder())
        {
            if (current == index) return node;
            current++;
        }

        return null;
    }

    private int ExpandedOrderCount() => EnumerateExpandedOrder().Count();

    /// <summary>The TopNode the control actually uses: the stored one when it is still reachable, else the first node.</summary>
    private TreeNode? EffectiveTopNode()
    {
        if (_topNode is { } node && ExpandedOrderIndex(node) >= 0) return node;

        return NodeAtExpandedOrderIndex(0);
    }

    private void SetTopNode(TreeNode? node)
    {
        if (node is null || ExpandedOrderIndex(node) < 0) return;

        _topNode = node;
        Invalidate();
    }

    /// <summary>The top of a node's row, in client pixels relative to the current scroll position; null when not reachable.</summary>
    internal int? RowTop(TreeNode node)
    {
        var nodeIndex = ExpandedOrderIndex(node);
        if (nodeIndex < 0) return null;

        var topIndex = ExpandedOrderIndex(EffectiveTopNode() ?? node);
        return (nodeIndex - topIndex) * ItemHeight;
    }

    internal Rectangle GetNodeLabelBounds(TreeNode node)
    {
        var top = RowTop(node);
        if (top is not { } rowTop) return Rectangle.Empty;

        return RowLayout(node, rowTop).Label;
    }

    /// <summary>Scrolls so the node's row is fully within the client area, moving TopNode by whole rows.</summary>
    internal void ScrollNodeIntoView(TreeNode node)
    {
        var nodeIndex = ExpandedOrderIndex(node);
        if (nodeIndex < 0) return;

        var visibleRows = Math.Max(VisibleCount, 1);
        var topIndex = ExpandedOrderIndex(EffectiveTopNode() ?? node);

        if (nodeIndex < topIndex) topIndex = nodeIndex;
        else if (nodeIndex > topIndex + visibleRows - 1) topIndex = nodeIndex - visibleRows + 1;

        SetTopNode(NodeAtExpandedOrderIndex(Math.Max(topIndex, 0)));
    }

    /// <summary>Recomputes whether the frame needs its vertical scroll bar, after anything that changes the row count.</summary>
    private void AdjustScrollbar()
    {
        if (_updateCount > 0) return;

        var needed = _scrollable && ExpandedOrderCount() > Math.Max(VisibleCount, 1);
        if (needed != _vScrollVisible)
        {
            _vScrollVisible = needed;
            UpdateStyles();
        }

        ClampTopNode();
    }

    /// <summary>Keeps TopNode from scrolling past the point where the last row would leave empty space below it.</summary>
    private void ClampTopNode()
    {
        if (_topNode is null) return;

        var total = ExpandedOrderCount();
        var maximum = Math.Max(total - Math.Max(VisibleCount, 1), 0);
        var current = ExpandedOrderIndex(_topNode);
        if (current < 0) return;

        var clamped = Math.Clamp(current, 0, maximum);
        if (clamped != current) _topNode = NodeAtExpandedOrderIndex(clamped);
    }

    /// <summary>The geometry of a node's row, given where its top falls in the client area.</summary>
    private NodeRowLayout RowLayout(TreeNode node, int rowTop)
    {
        var row = new Rectangle(0, rowTop, ClientSize.Width, ItemHeight);
        var level = node.Level;
        var bandStart = level * Indent;
        var centerY = row.Top + row.Height / 2;

        var glyph = new Rectangle(bandStart + (Indent - GlyphSize) / 2, centerY - GlyphSize / 2, GlyphSize, GlyphSize);

        var contentX = bandStart + Indent;

        var checkBox = Rectangle.Empty;
        if (_checkBoxes)
        {
            checkBox = new Rectangle(contentX, centerY - CheckBoxSize / 2, CheckBoxSize, CheckBoxSize);
            contentX += CheckBoxSize + ImageGap;
        }

        var image = Rectangle.Empty;
        if (_imageList is { } images)
        {
            image = new Rectangle(contentX, centerY - images.ImageSize.Height / 2, images.ImageSize.Width, images.ImageSize.Height);
            contentX += images.ImageSize.Width + ImageGap;
        }

        var textSize = TextRenderer.MeasureText(node.Text, node.NodeFont ?? Font);
        var label = new Rectangle(contentX, row.Top, textSize.Width + 4, row.Height);

        return new NodeRowLayout(row, glyph, checkBox, image, label);
    }

    /// <summary>The node under a point, and which part of its row the point falls on.</summary>
    /// <summary>Shadows Control's internal HitTest(Point) (a different, internal, hit test) with the public one Microsoft declares.</summary>
    public new TreeViewHitTestInfo HitTest(Point pt) => HitTest(pt.X, pt.Y);

    public TreeViewHitTestInfo HitTest(int x, int y)
    {
        var client = ClientSize;
        if (y < 0) return new TreeViewHitTestInfo(null, TreeViewHitTestLocations.AboveClientArea);
        if (y >= client.Height) return new TreeViewHitTestInfo(null, TreeViewHitTestLocations.BelowClientArea);
        if (x < 0) return new TreeViewHitTestInfo(null, TreeViewHitTestLocations.LeftOfClientArea);
        if (x >= client.Width) return new TreeViewHitTestInfo(null, TreeViewHitTestLocations.RightOfClientArea);

        var rowIndex = y / ItemHeight;
        var topIndex = ExpandedOrderIndex(EffectiveTopNode() ?? NodeAtExpandedOrderIndex(0)!);
        var node = NodeAtExpandedOrderIndex(topIndex + rowIndex);
        if (node is null) return new TreeViewHitTestInfo(null, TreeViewHitTestLocations.None);

        var layout = RowLayout(node, rowIndex * ItemHeight);
        if (layout.PlusMinus.Contains(x, y)) return new TreeViewHitTestInfo(node, TreeViewHitTestLocations.PlusMinus);
        if (!layout.CheckBox.IsEmpty && layout.CheckBox.Contains(x, y)) return new TreeViewHitTestInfo(node, TreeViewHitTestLocations.StateImage);
        if (!layout.Image.IsEmpty && layout.Image.Contains(x, y)) return new TreeViewHitTestInfo(node, TreeViewHitTestLocations.Image);
        if (layout.Label.Contains(x, y)) return new TreeViewHitTestInfo(node, TreeViewHitTestLocations.Label);

        return new TreeViewHitTestInfo(node, TreeViewHitTestLocations.Indent);
    }

    public TreeNode GetNodeAt(Point pt) => GetNodeAt(pt.X, pt.Y);

    public TreeNode GetNodeAt(int x, int y) => HitTest(x, y).Node!;
}
