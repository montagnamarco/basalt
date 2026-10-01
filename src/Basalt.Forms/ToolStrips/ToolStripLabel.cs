using System.Drawing;

namespace System.Windows.Forms;

/// <summary>Plain text (and/or an image) on a <see cref="ToolStrip"/>, not a button: it never looks pressed.</summary>
public class ToolStripLabel : ToolStripItem
{
    public ToolStripLabel()
    {
    }

    public ToolStripLabel(string? text)
        : base(text, null, null)
    {
    }

    public ToolStripLabel(Image? image)
        : base(null, image, null)
    {
    }

    public ToolStripLabel(string? text, Image? image)
        : base(text, image, null)
    {
    }

    public ToolStripLabel(string? text, Image? image, bool isLink)
        : base(text, image, null)
    {
    }

    public ToolStripLabel(string? text, Image? image, bool isLink, EventHandler? onClick)
        : base(text, image, onClick)
    {
    }

    public ToolStripLabel(string? text, Image? image, bool isLink, EventHandler? onClick, string? name)
        : base(text, image, onClick, name)
    {
    }

    /// <summary>A label never takes the mouse down/press look: it has nothing to click for.</summary>
    public override bool CanSelect => false;

    protected override void OnPaint(PaintEventArgs e)
    {
        var renderer = Owner?.Renderer;
        if (renderer is null)
        {
            base.OnPaint(e);
            return;
        }

        renderer.DrawLabelBackground(new ToolStripItemRenderEventArgs(e.Graphics, this));
        ToolStripItemPainting.DrawImageAndText(renderer, e.Graphics, this);

        base.OnPaint(e);
    }
}
