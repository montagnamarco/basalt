using System.Drawing;

namespace System.Windows.Forms;

/// <summary>What a <see cref="ToolStripRenderer"/> needs to paint the strip itself: its background or its border.</summary>
public class ToolStripRenderEventArgs : EventArgs
{
    public ToolStripRenderEventArgs(Graphics g, ToolStrip toolStrip)
        : this(g, toolStrip, toolStrip.ClientRectangle, toolStrip.BackColor)
    {
    }

    public ToolStripRenderEventArgs(Graphics g, ToolStrip toolStrip, Rectangle affectedBounds, Color backColor)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(toolStrip);

        Graphics = g;
        ToolStrip = toolStrip;
        AffectedBounds = affectedBounds;
        BackColor = backColor;
    }

    public Rectangle AffectedBounds { get; }

    public Color BackColor { get; }

    /// <summary>Where this strip touches the next one in a shared row; empty without a ToolStripPanel in this round.</summary>
    public Rectangle ConnectedArea => Rectangle.Empty;

    public Graphics Graphics { get; }

    public ToolStrip ToolStrip { get; }
}
