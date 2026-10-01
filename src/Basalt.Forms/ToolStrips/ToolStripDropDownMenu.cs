using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A <see cref="ToolStripDropDown"/> that reserves a margin on its leading
/// edge for item images and check marks: the body of a menu (a
/// <see cref="ToolStripMenuItem"/>'s submenu, or a <see cref="ContextMenuStrip"/>),
/// as opposed to a plain list of items with nothing in that margin.
/// </summary>
public class ToolStripDropDownMenu : ToolStripDropDown
{
    private bool _showImageMargin = true;
    private bool _showCheckMargin;

    /// <summary>How wide the image margin is: enough for the strip's own image scaling size, plus a little air.</summary>
    internal int ImageMarginWidth => _showImageMargin || _showCheckMargin ? ImageScalingSize.Width + 8 : 0;

    [DefaultValue(false)]
    public bool ShowCheckMargin
    {
        get => _showCheckMargin;
        set
        {
            if (value == _showCheckMargin) return;

            _showCheckMargin = value;
            DoLayout(this, nameof(ShowCheckMargin));
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool ShowImageMargin
    {
        get => _showImageMargin;
        set
        {
            if (value == _showImageMargin) return;

            _showImageMargin = value;
            DoLayout(this, nameof(ShowImageMargin));
            Invalidate();
        }
    }

    protected override Padding DefaultPadding => new(1, 2, 1, 2);

    /// <summary>Items are placed to the right of the image margin.</summary>
    public override Rectangle DisplayRectangle
    {
        get
        {
            var rectangle = base.DisplayRectangle;
            var margin = ImageMarginWidth;

            return new Rectangle(rectangle.Left + margin, rectangle.Top, Math.Max(rectangle.Width - margin, 0), rectangle.Height);
        }
    }

    /// <summary>The strip's own preferred width grows by the image margin: items are measured within DisplayRectangle alone.</summary>
    internal override Size GetPreferredSizeCore(Size proposedSize)
    {
        var size = base.GetPreferredSizeCore(proposedSize);

        return new Size(size.Width + ImageMarginWidth, size.Height);
    }

    protected internal override ToolStripItem CreateDefaultItem(string? text, Image? image, EventHandler? onClick) =>
        new ToolStripMenuItem(text, image, onClick);

    /// <summary>
    /// The image margin's own look, painted right after the strip's ordinary
    /// background fill (ToolStrip.OnPaint's hook for it) and before the
    /// items, which are laid out outside it (see DisplayRectangle).
    /// </summary>
    internal override void PaintLeadingMargin(PaintEventArgs e)
    {
        var margin = ImageMarginWidth;
        if (margin <= 0) return;

        var client = ClientRectangle;
        var marginBounds = new Rectangle(client.Left, client.Top, margin, client.Height);

        Renderer.DrawImageMargin(new ToolStripRenderEventArgs(e.Graphics, this, marginBounds, BackColor));
    }
}
