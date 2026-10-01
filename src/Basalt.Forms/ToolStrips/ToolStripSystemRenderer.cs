using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The classic Windows 2000 toolbar look: a flat grey strip, buttons that
/// raise on hover and sink when pressed or checked, etched separators, and
/// a column (or row) of grip dots at the leading edge.
/// </summary>
public class ToolStripSystemRenderer : ToolStripRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        base.OnRenderToolStripBackground(e);

        using var brush = new SolidBrush(e.BackColor.IsEmpty ? SystemColors.Control : e.BackColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        base.OnRenderToolStripBorder(e);

        // A single dark line along the edge the strip meets the rest of the form, as the classic toolbar has.
        var bounds = e.ToolStrip.ClientRectangle;
        using var pen = new Pen(SystemColors.ControlDark);

        if (e.ToolStrip.Dock == DockStyle.Bottom) e.Graphics.DrawLine(pen, bounds.Left, bounds.Top, bounds.Right, bounds.Top);
        else e.Graphics.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
    }

    protected override void OnRenderGrip(ToolStripGripRenderEventArgs e)
    {
        base.OnRenderGrip(e);

        ToolStripClassicPainting.DrawGripDots(e.Graphics, e.GripBounds, e.GripDisplayStyle);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        base.OnRenderSeparator(e);

        ToolStripClassicPainting.DrawEtchedSeparator(e.Graphics, e.Item.Bounds, e.Vertical);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderButtonBackground(e);

        if (e.Item is not ToolStripButton button) return;

        ToolStripClassicPainting.DrawButtonFace(e.Graphics, button.Bounds, button.Pressed || button.Checked, button.Selected, button.Enabled);
    }

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderDropDownButtonBackground(e);

        ToolStripClassicPainting.DrawButtonFace(e.Graphics, e.Item.Bounds, e.Item.Pressed, e.Item.Selected, e.Item.Enabled);
    }

    protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderSplitButtonBackground(e);

        ToolStripClassicPainting.DrawButtonFace(e.Graphics, e.Item.Bounds, e.Item.Pressed, e.Item.Selected, e.Item.Enabled);
    }

    protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderOverflowButtonBackground(e);

        ToolStripClassicPainting.DrawButtonFace(e.Graphics, e.Item.Bounds, e.Item.Pressed, e.Item.Selected, e.Item.Enabled);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderMenuItemBackground(e);

        if (e.Item.Selected) ToolStripClassicPainting.DrawButtonFace(e.Graphics, e.Item.Bounds, pressed: false, selected: true, e.Item.Enabled);
    }

    protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderLabelBackground(e);

        // A label never raises or sinks: it only ever shows its own (or the strip's) back colour.
        if (e.Item.BackColor == e.ToolStrip?.BackColor) return;

        using var brush = new SolidBrush(e.Item.BackColor);
        e.Graphics.FillRectangle(brush, e.Item.Bounds);
    }

    protected override void OnRenderToolStripStatusLabelBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderToolStripStatusLabelBackground(e);

        using var brush = new SolidBrush(e.Item.BackColor);
        e.Graphics.FillRectangle(brush, e.Item.Bounds);
    }

    // Default item image/text/check painting lives on the base ToolStripRenderer (Microsoft's own
    // ToolStripSystemRenderer does not re-override it either); ToolStripProfessionalRenderer does.

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        base.OnRenderImageMargin(e);

        using var brush = new SolidBrush(SystemColors.Control);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }
}
