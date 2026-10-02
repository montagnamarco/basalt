using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// DrawMode lets a handler replace the label (OwnerDrawText) or the whole
/// row (OwnerDrawAll); setting DrawTreeNodeEventArgs.DrawDefault asks for
/// the normal drawing underneath or instead of what it drew.
/// </summary>
public partial class TreeView
{
    private TreeViewDrawMode _drawMode = TreeViewDrawMode.Normal;

    [DefaultValue(TreeViewDrawMode.Normal)]
    public TreeViewDrawMode DrawMode
    {
        get => _drawMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(TreeViewDrawMode));
            if (_drawMode == value) return;

            _drawMode = value;
            Invalidate();
        }
    }

    protected virtual void OnDrawNode(DrawTreeNodeEventArgs e) => ((DrawTreeNodeEventHandler?)Events[s_drawNodeEvent])?.Invoke(this, e);

    public event DrawTreeNodeEventHandler? DrawNode
    {
        add => Events.AddHandler(s_drawNodeEvent, value);
        remove => Events.RemoveHandler(s_drawNodeEvent, value);
    }

    /// <summary>The node's row state, as DrawTreeNodeEventArgs.State reports it.</summary>
    private TreeNodeStates NodeStates(TreeNode node)
    {
        var state = TreeNodeStates.Default;
        if (node.IsSelected) state |= TreeNodeStates.Selected;
        if (node.IsSelected && Focused) state |= TreeNodeStates.Focused;
        if (node.Checked) state |= TreeNodeStates.Checked;
        if (HotTracking && node == _hoverNode) state |= TreeNodeStates.Hot;

        return state;
    }

    /// <summary>
    /// Raises DrawNode for a row that opted into owner drawing; returns true
    /// when the caller must still draw its own default content (DrawDefault,
    /// or DrawMode is Normal and never raised the event at all).
    /// </summary>
    private bool RaiseDrawNode(Graphics graphics, TreeNode node, Rectangle bounds)
    {
        if (_drawMode == TreeViewDrawMode.Normal) return true;

        var e = new DrawTreeNodeEventArgs(graphics, node, bounds, NodeStates(node));
        OnDrawNode(e);
        return e.DrawDefault;
    }

    private static readonly object s_drawNodeEvent = new();
}
