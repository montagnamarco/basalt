using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A thin etched line that divides groups of items on a <see cref="ToolStrip"/>.</summary>
public class ToolStripSeparator : ToolStripItem
{
    public override bool CanSelect => false;

    protected override Padding DefaultMargin => Padding.Empty;

    protected override Size DefaultSize => new(6, 6);

    public override bool Enabled
    {
        get => true;
        set
        {
            // A separator is never disabled: Microsoft's own ignores the setter too.
        }
    }

    public override Image? Image
    {
        get => null;
        set
        {
        }
    }

    public override string? Text
    {
        get => null;
        set
        {
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var owner = Owner;
        if (owner is null) return;

        var vertical = owner.Orientation == Orientation.Horizontal;
        owner.Renderer.DrawSeparator(new ToolStripSeparatorRenderEventArgs(e.Graphics, this, vertical));

        base.OnPaint(e);
    }
}
