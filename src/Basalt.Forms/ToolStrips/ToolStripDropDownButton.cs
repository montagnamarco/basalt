using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A toolbar button that always shows a drop-down arrow and opens its menu on click.</summary>
public class ToolStripDropDownButton : ToolStripDropDownItem
{
    private const int ArrowWidth = 11;

    public ToolStripDropDownButton()
    {
    }

    public ToolStripDropDownButton(string? text)
        : base(text, null, (EventHandler?)null)
    {
    }

    public ToolStripDropDownButton(Image? image)
        : base(null, image, (EventHandler?)null)
    {
    }

    public ToolStripDropDownButton(string? text, Image? image)
        : base(text, image, (EventHandler?)null)
    {
    }

    public ToolStripDropDownButton(string? text, Image? image, EventHandler? onClick)
        : base(text, image, onClick)
    {
    }

    public ToolStripDropDownButton(string? text, Image? image, params ToolStripItem[]? dropDownItems)
        : base(text, image, dropDownItems)
    {
    }

    public ToolStripDropDownButton(string? text, Image? image, EventHandler? onClick, string? name)
        : base(text, image, onClick, name)
    {
    }

    [DefaultValue(true)]
    public bool AutoToolTip { get; set; } = true;

    protected override bool DefaultAutoToolTip => true;

    [DefaultValue(true)]
    public bool ShowDropDownArrow { get; set; } = true;

    /// <summary>A plain click toggles the drop-down, as there is nothing else for the button to do.</summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (HasDropDown && DropDown.IsOpen) HideDropDown();
        else ShowDropDown();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var renderer = Owner?.Renderer;
        if (renderer is null)
        {
            base.OnPaint(e);
            return;
        }

        renderer.DrawDropDownButtonBackground(new ToolStripItemRenderEventArgs(e.Graphics, this));
        ToolStripItemPainting.DrawImageAndText(renderer, e.Graphics, this);

        if (ShowDropDownArrow)
        {
            var bounds = Bounds;
            var arrowRect = new Rectangle(bounds.Right - ArrowWidth, bounds.Top, ArrowWidth, bounds.Height);
            renderer.DrawArrow(new ToolStripArrowRenderEventArgs(e.Graphics, this, Rectangle.Inflate(arrowRect, -3, -3), ForeColor, ArrowDirection.Down));
        }

        base.OnPaint(e);
    }
}
