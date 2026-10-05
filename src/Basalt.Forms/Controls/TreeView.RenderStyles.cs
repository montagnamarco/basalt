namespace System.Windows.Forms;

public partial class TreeView
{
    protected OwnerDrawPropertyBag GetItemRenderStyles(TreeNode node, int state)
    {
        ArgumentNullException.ThrowIfNull(node);
        // Native custom-draw state describes selection and focus. Those are
        // painted separately; the bag carries only the node's explicit styles.
        var styles = OwnerDrawPropertyBag.Copy(null);
        styles.BackColor = node.BackColor;
        styles.ForeColor = node.ForeColor;
        styles.Font = node.NodeFont;
        return styles;
    }
}
