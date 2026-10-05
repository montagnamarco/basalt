using System.Drawing;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Skins;

namespace System.Windows.Forms;

public partial class TreeView
{
    /// <summary>A press on the frame's vertical scroll bar being tracked: the offset the thumb was grabbed at.</summary>
    private int? _thumbDragOffset;

    /// <summary>The window's own vertical scroll bar is part of its frame, as a native tree view's is.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;

            if (_borderStyle == BorderStyle.Fixed3D) parameters.ExStyle |= WindowStyles.ExClientEdge;
            else if (_borderStyle == BorderStyle.FixedSingle) parameters.Style |= WindowStyles.Border;

            if (_vScrollVisible) parameters.Style |= WindowStyles.VerticalScroll;

            return parameters;
        }
    }

    /// <summary>Classic trees use window colors; styled previews retain explicit ambient colors.</summary>
    public override Color BackColor
    {
        get => ShouldSerializeBackColor() ||
            (PortableVisualStylesEnabled && Parent?.HasExplicitAppearanceBackColor == true)
            ? base.BackColor : SystemColors.Window;
        set => base.BackColor = value;
    }

    public override Color ForeColor
    {
        get => ShouldSerializeForeColor() ||
            (PortableVisualStylesEnabled && Parent?.HasExplicitAppearanceForeColor == true)
            ? base.ForeColor : SystemColors.WindowText;
        set => base.ForeColor = value;
    }

    /// <summary>Answers WM_GETDLGCODE: a tree view wants the keys that move and page its selection.</summary>
    internal override bool DialogCodeWantsKey(Keys keyData)
    {
        if (_editingNode is not null && (keyData == Keys.Return || keyData == Keys.Escape)) return true;

        return keyData switch
        {
            Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown => true,
            _ => base.DialogCodeWantsKey(keyData),
        };
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (_hideSelection) Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);

        // Leaving the tree commits a label edit in progress, as the native edit box does.
        if (_editingNode is { } editing) EndEditNode(editing, cancel: false);
        if (_hideSelection) Invalidate();
    }

    /// <summary>A press on the native control, before OnMouseDown: it hit-tests the row under the pointer.</summary>
    internal override void DefaultMouseDown(MouseEventArgs e)
    {
        if (CanSelect) Focus();
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;

        var hit = HitTest(e.Location);
        if (hit.Node is not { } node) return;

        ArmDragCandidate(node, e.Button, e.Location);

        if (hit.Location == TreeViewHitTestLocations.PlusMinus)
        {
            node.Toggle();
        }
        else if (hit.Location == TreeViewHitTestLocations.StateImage && _checkBoxes)
        {
            node.Checked = !node.Checked;
        }
        else if (e.Button == MouseButtons.Left)
        {
            var wasSelected = node == _selectedNode && hit.Location == TreeViewHitTestLocations.Label;

            if (SelectNode(node, TreeViewAction.ByMouse) && e.Clicks >= 2) node.Toggle();
            else if (wasSelected && e.Clicks == 1 && LabelEdit) BeginEditNode(node);
        }

        var arguments = new TreeNodeMouseClickEventArgs(node, e.Button, e.Clicks, e.X, e.Y);
        if (e.Clicks >= 2) OnNodeMouseDoubleClick(arguments);
        else OnNodeMouseClick(arguments);

        if (e.Button == MouseButtons.Right) node.ContextMenuStrip?.Show(this, e.Location);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        if (_editingNode is { } editing)
        {
            if (e.KeyCode == Keys.Return) EndEditNode(editing, cancel: false);
            else if (e.KeyCode == Keys.Escape) EndEditNode(editing, cancel: true);

            e.Handled = true;
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.F2:
                if (_selectedNode is { } toEdit && LabelEdit) BeginEditNode(toEdit);
                e.Handled = true;
                break;

            case Keys.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;

            case Keys.Down:
                MoveSelection(1);
                e.Handled = true;
                break;

            case Keys.Left:
                if (_rightToLeftLayout) ExpandOrSelectFirstChild();
                else CollapseOrSelectParent();
                e.Handled = true;
                break;

            case Keys.Right:
                if (_rightToLeftLayout) CollapseOrSelectParent();
                else ExpandOrSelectFirstChild();
                e.Handled = true;
                break;

            case Keys.Home:
                SelectAtExpandedOrderIndex(0);
                e.Handled = true;
                break;

            case Keys.End:
                SelectAtExpandedOrderIndex(ExpandedOrderCount() - 1);
                e.Handled = true;
                break;

            case Keys.PageUp:
                MoveSelection(-Math.Max(VisibleCount, 1));
                e.Handled = true;
                break;

            case Keys.PageDown:
                MoveSelection(Math.Max(VisibleCount, 1));
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (e.Handled) return;

        if (_editingNode is not null)
        {
            if (e.KeyChar == (char)8) _editText = _editText.Length > 0 ? _editText[..^1] : _editText;
            else if (!char.IsControl(e.KeyChar)) _editText += e.KeyChar;

            Invalidate();
            e.Handled = true;
            return;
        }

        if (_selectedNode is not { } node) return;

        switch (e.KeyChar)
        {
            case '+':
                if (node.Nodes.Count > 0) node.SetExpanded(true);
                e.Handled = true;
                break;

            case '-':
                if (node.IsExpanded) node.SetExpanded(false);
                e.Handled = true;
                break;

            case '*':
                node.ExpandAll();
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        var total = ExpandedOrderCount();
        if (total == 0) return;

        var current = _selectedNode is { } selected ? ExpandedOrderIndex(selected) : -1;
        if (current < 0) current = ExpandedOrderIndex(EffectiveTopNode()!);

        SelectAtExpandedOrderIndex(Math.Clamp(current + delta, 0, total - 1));
    }

    private void SelectAtExpandedOrderIndex(int index)
    {
        if (NodeAtExpandedOrderIndex(index) is not { } node) return;
        if (SelectNode(node, TreeViewAction.ByKeyboard)) ScrollNodeIntoView(node);
    }

    private void CollapseOrSelectParent()
    {
        if (_selectedNode is not { } node) return;

        if (node.IsExpanded && node.Nodes.Count > 0)
        {
            node.SetExpanded(false);
        }
        else if (node.Parent is { } parent)
        {
            if (SelectNode(parent, TreeViewAction.ByKeyboard)) ScrollNodeIntoView(parent);
        }
    }

    private void ExpandOrSelectFirstChild()
    {
        if (_selectedNode is not { } node || node.Nodes.Count == 0) return;

        if (!node.IsExpanded)
        {
            node.SetExpanded(true);
        }
        else if (SelectNode(node.Nodes[0], TreeViewAction.ByKeyboard))
        {
            ScrollNodeIntoView(node.Nodes[0]);
        }
    }

    /// <summary>The state of the frame's vertical scroll bar, for drawing it; null while it is not shown.</summary>
    internal override Internal.WindowScrollInfo? GetWindowScroll(bool vertical)
    {
        if (!vertical || !_vScrollVisible) return null;

        var total = ExpandedOrderCount();
        var page = Math.Max(VisibleCount, 1);
        var position = ExpandedOrderIndex(EffectiveTopNode()!);

        return new Internal.WindowScrollInfo(0, Math.Max(total - 1, 0), page, Math.Max(position, 0), Enabled && _scrollable);
    }

    internal override bool IsTrackingNonClient => _thumbDragOffset is not null;

    /// <summary>Arrows step a row, the track a page, the thumb is dragged: the frame's own scroll bar.</summary>
    internal override bool NonClientMouseDown(MouseButtons button, Point outer)
    {
        if (button != MouseButtons.Left) return false;
        if (GetWindowScroll(vertical: true) is not { Enabled: true } info) return false;

        var parameters = CreateParams;
        var bar = WindowScrollBars.Vertical(Size, parameters.Style, parameters.ExStyle);
        if (bar.IsEmpty || !bar.Contains(outer)) return false;

        var track = ScrollBarPainter.TrackBounds(Orientation.Vertical, bar);
        var thumb = info.ThumbBounds(track, vertical: true);

        if (ScrollBarPainter.LineUpButtonBounds(Orientation.Vertical, bar).Contains(outer))
        {
            StepRows(-1);
        }
        else if (ScrollBarPainter.LineDownButtonBounds(Orientation.Vertical, bar).Contains(outer))
        {
            StepRows(1);
        }
        else if (thumb.Contains(outer))
        {
            _thumbDragOffset = outer.Y - thumb.Y;
            return true;
        }
        else if (outer.Y < thumb.Top)
        {
            StepRows(-info.Page);
        }
        else
        {
            StepRows(info.Page);
        }

        return false;
    }

    internal override void NonClientMouseMove(Point outer)
    {
        if (_thumbDragOffset is not { } grab) return;
        if (GetWindowScroll(vertical: true) is not { } info) return;

        var parameters = CreateParams;
        var bar = WindowScrollBars.Vertical(Size, parameters.Style, parameters.ExStyle);
        var track = ScrollBarPainter.TrackBounds(Orientation.Vertical, bar);
        var thumb = info.ThumbBounds(track, vertical: true);

        var offset = outer.Y - track.Top - grab;
        var position = info.PositionFromThumb(offset, track, thumb.Height, vertical: true);
        SetTopNode(NodeAtExpandedOrderIndex(position));
    }

    internal override void NonClientMouseUp(Point outer) => _thumbDragOffset = null;

    internal override void DefaultCaptureLost()
    {
        _thumbDragOffset = null;
        DisarmDragCandidate();
    }

    internal override void DefaultMouseUp(MouseEventArgs e) => DisarmDragCandidate();

    private void StepRows(int delta)
    {
        var current = ExpandedOrderIndex(EffectiveTopNode()!);
        if (current < 0) return;

        var total = ExpandedOrderCount();
        var maximum = Math.Max(total - Math.Max(VisibleCount, 1), 0);
        SetTopNode(NodeAtExpandedOrderIndex(Math.Clamp(current + delta, 0, maximum)));
    }
}
