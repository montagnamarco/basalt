using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>
/// The bar across the bottom of a form: a row of <see cref="ToolStripStatusLabel"/>s,
/// one of which may spring to take up whatever width the others leave, with
/// an optional sizing grip in the trailing corner.
/// </summary>
public class StatusStrip : ToolStrip
{
    private bool _sizingGrip = true;

    public StatusStrip()
    {
        Dock = DockStyle.Bottom;
        GripStyle = ToolStripGripStyle.Hidden;
        Stretch = true;
        ShowItemToolTips = false;
    }

    [DefaultValue(false)]
    [Browsable(false)]
    public bool CanOverflow { get; set; }

    protected override DockStyle DefaultDock => DockStyle.Bottom;

    protected override Padding DefaultPadding => new(1, 0, 16, 0);

    protected override bool DefaultShowItemToolTips => false;

    protected override Size DefaultSize => new(200, 22);

    [DefaultValue(false)]
    public new bool ShowItemToolTips
    {
        get => base.ShowItemToolTips;
        set => base.ShowItemToolTips = value;
    }

    /// <summary>Whether the bar stretches to fill its container's width, as a status bar always does.</summary>
    [DefaultValue(true)]
    public bool Stretch { get; set; } = true;

    /// <summary>The square in the bottom trailing corner the grip (or, when hidden, nothing) occupies.</summary>
    [Browsable(false)]
    public Rectangle SizeGripBounds
    {
        get
        {
            var client = ClientRectangle;
            const int side = 16;

            return new Rectangle(client.Right - side, client.Bottom - side, side, side);
        }
    }

    /// <summary>Whether the classic diagonal grip dots are drawn in the trailing corner.</summary>
    [DefaultValue(true)]
    public bool SizingGrip
    {
        get => _sizingGrip;
        set
        {
            if (value == _sizingGrip) return;

            _sizingGrip = value;
            Invalidate();
        }
    }

    protected internal override ToolStripItem CreateDefaultItem(string? text, Image? image, EventHandler? onClick) =>
        new ToolStripStatusLabel(text, image, onClick);

    /// <summary>After the ordinary left-to-right layout, a Spring label grows to use whatever width is left.</summary>
    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        ApplySpringLayout();
    }

    private void ApplySpringLayout()
    {
        List<ToolStripStatusLabel>? springLabels = null;
        var usedWidth = 0;

        foreach (ToolStripItem item in Items)
        {
            if (!item.Available) continue;

            usedWidth += item.Bounds.Width + item.Margin.Horizontal;

            if (item is ToolStripStatusLabel { Spring: true } label)
            {
                springLabels ??= [];
                springLabels.Add(label);
            }
        }

        if (springLabels is null) return;

        var display = DisplayRectangle;
        var trailingLimit = _sizingGrip ? display.Right - SizeGripBounds.Width : display.Right;
        var extra = Math.Max(trailingLimit - display.Left - usedWidth, 0);
        var share = extra / springLabels.Count;
        var shift = 0;

        foreach (ToolStripItem item in Items)
        {
            if (!item.Available) continue;

            var bounds = item.Bounds;
            bounds.X += shift;

            if (item is ToolStripStatusLabel { Spring: true })
            {
                bounds.Width += share;
                shift += share;
            }

            ((IArrangedElement)item).SetBounds(bounds, BoundsSpecified.None);
        }
    }

    /// <summary>ToolStrip.OnPaint's hook for something painted on top of everything else: the sizing grip.</summary>
    internal override void PaintTrailingDecoration(PaintEventArgs e)
    {
        if (!_sizingGrip) return;

        Renderer.DrawStatusStripSizingGrip(new ToolStripRenderEventArgs(e.Graphics, this, SizeGripBounds, BackColor));
        ControlPaint.DrawSizeGrip(e.Graphics, BackColor, SizeGripBounds);
    }
}
