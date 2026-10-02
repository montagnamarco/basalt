using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// In-place editing of a node's label: F2, or a click on the already
/// selected label, opens a small text box over the row; Enter commits it,
/// Escape cancels it, both guarded by the cancelable Before/After events.
/// </summary>
public partial class TreeView
{
    private TreeNode? _editingNode;
    private string _editText = string.Empty;

    [DefaultValue(false)]
    public bool LabelEdit { get; set; }

    /// <summary>The node currently showing its in-place edit box, or null.</summary>
    internal TreeNode? EditingNode => _editingNode;

    protected virtual void OnBeforeLabelEdit(NodeLabelEditEventArgs e) => ((NodeLabelEditEventHandler?)Events[s_beforeLabelEditEvent])?.Invoke(this, e);

    protected virtual void OnAfterLabelEdit(NodeLabelEditEventArgs e) => ((NodeLabelEditEventHandler?)Events[s_afterLabelEditEvent])?.Invoke(this, e);

    public event NodeLabelEditEventHandler? BeforeLabelEdit
    {
        add => Events.AddHandler(s_beforeLabelEditEvent, value);
        remove => Events.RemoveHandler(s_beforeLabelEditEvent, value);
    }

    public event NodeLabelEditEventHandler? AfterLabelEdit
    {
        add => Events.AddHandler(s_afterLabelEditEvent, value);
        remove => Events.RemoveHandler(s_afterLabelEditEvent, value);
    }

    /// <summary>Opens the edit box over the node's label, unless BeforeLabelEdit cancels it.</summary>
    internal void BeginEditNode(TreeNode node)
    {
        if (_editingNode == node) return;
        if (_editingNode is { } current) EndEditNode(current, cancel: false);

        var before = new NodeLabelEditEventArgs(node, node.Text);
        OnBeforeLabelEdit(before);
        if (before.CancelEdit) return;

        _editingNode = node;
        _editText = node.Text;
        Invalidate();
    }

    /// <summary>Closes the edit box; unless cancelled (and AfterLabelEdit does not cancel it too), the typed text becomes the node's.</summary>
    internal void EndEditNode(TreeNode node, bool cancel)
    {
        if (_editingNode != node) return;

        var typed = _editText;
        _editingNode = null;

        var after = new NodeLabelEditEventArgs(node, cancel ? null : typed);
        OnAfterLabelEdit(after);

        if (!cancel && !after.CancelEdit) node.Text = after.Label ?? typed;
        Invalidate();
    }

    /// <summary>The geometry of the in-place edit box: white, bordered, over the label it replaces.</summary>
    private void PaintLabelEdit(Graphics graphics, Rectangle label)
    {
        var textSize = TextRenderer.MeasureText(_editText.Length > 0 ? _editText : " ", Font);
        var box = new Rectangle(label.Left - 2, label.Top, Math.Max(textSize.Width + 6, label.Width + 4), label.Height);

        using var back = new SolidBrush(SystemColors.Window);
        graphics.FillRectangle(back, box);

        using var border = new Pen(SystemColors.WindowFrame);
        graphics.DrawRectangle(border, box.X, box.Y, box.Width - 1, box.Height - 1);

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(graphics, _editText, Font, box, SystemColors.WindowText, flags);
    }

    private static readonly object s_beforeLabelEditEvent = new();
    private static readonly object s_afterLabelEditEvent = new();
}
