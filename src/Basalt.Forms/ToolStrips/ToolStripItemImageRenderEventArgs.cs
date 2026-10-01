using System.Drawing;

namespace System.Windows.Forms;

/// <summary>What a <see cref="ToolStripRenderer"/> needs to paint an item's image (or its check glyph).</summary>
public class ToolStripItemImageRenderEventArgs : ToolStripItemRenderEventArgs
{
    public ToolStripItemImageRenderEventArgs(Graphics g, ToolStripItem item, Rectangle imageRectangle)
        : this(g, item, null, imageRectangle)
    {
    }

    public ToolStripItemImageRenderEventArgs(Graphics g, ToolStripItem item, Image? image, Rectangle imageRectangle)
        : base(g, item)
    {
        Image = image;
        ImageRectangle = imageRectangle;
    }

    public Image? Image { get; }

    public Rectangle ImageRectangle { get; }
}
