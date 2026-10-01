using System.Drawing;

namespace System.Windows.Forms;

/// <summary>What a <see cref="ToolStripRenderer"/> needs to paint a separator's etched line.</summary>
public class ToolStripSeparatorRenderEventArgs : ToolStripItemRenderEventArgs
{
    public ToolStripSeparatorRenderEventArgs(Graphics g, ToolStripSeparator separator, bool vertical)
        : base(g, separator)
    {
        Vertical = vertical;
    }

    public bool Vertical { get; }
}
