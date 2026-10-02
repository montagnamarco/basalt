using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Tracking the node under the pointer: HotTracking underlines its label,
/// NodeMouseHover fires once it settles there, and ItemDrag fires once a
/// held button moves the pointer past the drag threshold.
/// </summary>
public partial class TreeView
{
    /// <summary>The node the pointer is currently over; null when it is over none, or has left the control.</summary>
    private TreeNode? _hoverNode;

    private TreeNode? _dragCandidateNode;
    private MouseButtons _dragButton;
    private Point _dragStart;

    [DefaultValue(false)]
    public bool HotTracking { get; set; }

    /// <summary>Tracks the hovered node (for HotTracking and NodeMouseHover) and starts an ItemDrag past the drag threshold.</summary>
    internal override void DefaultMouseMove(MouseEventArgs e)
    {
        var hit = HitTest(e.Location);
        if (hit.Node != _hoverNode)
        {
            _hoverNode = hit.Node;
            if (HotTracking) Invalidate();
            if (_hoverNode is { } hovered) OnNodeMouseHover(new TreeNodeMouseHoverEventArgs(hovered));
        }

        if (_dragCandidateNode is { } candidate && (e.Button & _dragButton) == _dragButton)
        {
            var moved = new Size(e.X - _dragStart.X, e.Y - _dragStart.Y);
            if (Math.Abs(moved.Width) >= SystemInformation.DragSize.Width || Math.Abs(moved.Height) >= SystemInformation.DragSize.Height)
            {
                _dragCandidateNode = null;
                OnItemDrag(new ItemDragEventArgs(_dragButton, candidate));
            }
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverNode is not null)
        {
            _hoverNode = null;
            if (HotTracking) Invalidate();
        }
    }

    /// <summary>Arms the drag candidate for the node pressed; DefaultMouseMove turns it into ItemDrag past the threshold.</summary>
    private void ArmDragCandidate(TreeNode node, MouseButtons button, Point location)
    {
        _dragCandidateNode = node;
        _dragButton = button;
        _dragStart = location;
    }

    private void DisarmDragCandidate() => _dragCandidateNode = null;

    protected virtual void OnItemDrag(ItemDragEventArgs e) => ((ItemDragEventHandler?)Events[s_itemDragEvent])?.Invoke(this, e);

    protected virtual void OnNodeMouseHover(TreeNodeMouseHoverEventArgs e) => ((TreeNodeMouseHoverEventHandler?)Events[s_nodeMouseHoverEvent])?.Invoke(this, e);

    public event ItemDragEventHandler? ItemDrag
    {
        add => Events.AddHandler(s_itemDragEvent, value);
        remove => Events.RemoveHandler(s_itemDragEvent, value);
    }

    public event TreeNodeMouseHoverEventHandler? NodeMouseHover
    {
        add => Events.AddHandler(s_nodeMouseHoverEvent, value);
        remove => Events.RemoveHandler(s_nodeMouseHoverEvent, value);
    }

    private static readonly object s_itemDragEvent = new();
    private static readonly object s_nodeMouseHoverEvent = new();
}
