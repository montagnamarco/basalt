using System.Drawing;

namespace System.Windows.Forms;

/// <summary>What a <see cref="ToolStripRenderer"/> needs to paint the drag grip.</summary>
public class ToolStripGripRenderEventArgs : ToolStripRenderEventArgs
{
    public ToolStripGripRenderEventArgs(Graphics g, ToolStrip toolStrip)
        : base(g, toolStrip)
    {
    }

    public Rectangle GripBounds => ToolStrip.GripRectangle;

    public ToolStripGripDisplayStyle GripDisplayStyle => ToolStrip.GripDisplayStyle;

    public ToolStripGripStyle GripStyle => ToolStrip.GripStyle;
}
