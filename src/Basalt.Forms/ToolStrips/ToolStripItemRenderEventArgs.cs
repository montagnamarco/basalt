using System.Drawing;

namespace System.Windows.Forms;

/// <summary>What a <see cref="ToolStripRenderer"/> needs to paint one item's background.</summary>
public class ToolStripItemRenderEventArgs : EventArgs
{
    public ToolStripItemRenderEventArgs(Graphics g, ToolStripItem item)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(item);

        Graphics = g;
        Item = item;
    }

    public Graphics Graphics { get; }

    public ToolStripItem Item { get; }

    public ToolStrip? ToolStrip => Item.Owner;
}
