using System.ComponentModel;

namespace System.Windows.Forms;

public partial class TreeNode
{
    private ContextMenuStrip? _contextMenuStrip;

    [DefaultValue(null)]
    public virtual ContextMenuStrip? ContextMenuStrip
    {
        get => _contextMenuStrip;
        set => _contextMenuStrip = value;
    }

    /// <summary>Native tree-item handles do not exist in Basalt's painted TreeView.</summary>
    public IntPtr Handle => throw new PlatformNotSupportedException("TreeNode has no native tree-item handle in the painted TreeView backend.");

    public static TreeNode? FromHandle(TreeView tree, IntPtr handle)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (handle == IntPtr.Zero) return null;
        throw new PlatformNotSupportedException("TreeNode has no native tree-item handle in the painted TreeView backend.");
    }
}
