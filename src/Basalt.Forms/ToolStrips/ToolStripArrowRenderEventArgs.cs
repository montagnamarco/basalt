using System.Drawing;

namespace System.Windows.Forms;

/// <summary>What a <see cref="ToolStripRenderer"/> needs to paint a drop-down arrow glyph.</summary>
public class ToolStripArrowRenderEventArgs : EventArgs
{
    public ToolStripArrowRenderEventArgs(Graphics g, ToolStripItem? toolStripItem, Rectangle arrowRectangle, Color arrowColor, ArrowDirection arrowDirection)
    {
        ArgumentNullException.ThrowIfNull(g);

        Graphics = g;
        Item = toolStripItem;
        ArrowRectangle = arrowRectangle;
        ArrowColor = arrowColor;
        Direction = arrowDirection;
    }

    public Color ArrowColor { get; set; }

    public Rectangle ArrowRectangle { get; set; }

    public ArrowDirection Direction { get; set; }

    public Graphics Graphics { get; }

    public ToolStripItem? Item { get; }
}
