using System.Drawing;
using System.Windows.Forms.Internal.Rendering;

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

        var color = e.ToolStrip.PortableVisualStylesEnabled && !e.ToolStrip.HasExplicitAppearanceBackColor
            ? PortableVisualStyles.Palette(e.ToolStrip.PortableVisualStylePlatform).Face
            : e.BackColor.IsEmpty ? SystemColors.Control : e.BackColor;
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        base.OnRenderToolStripBorder(e);

        // A single dark line along the edge the strip meets the rest of the form, as the classic toolbar has.
        var bounds = e.ToolStrip.ClientRectangle;
        var border = e.ToolStrip.PortableVisualStylesEnabled
            ? PortableVisualStyles.Palette(e.ToolStrip.PortableVisualStylePlatform).Border
            : SystemColors.ControlDark;
        using var pen = new Pen(border);

        if (e.ToolStrip.Dock == DockStyle.Bottom) e.Graphics.DrawLine(pen, bounds.Left, bounds.Top, bounds.Right, bounds.Top);
        else e.Graphics.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
    }

    protected override void OnRenderGrip(ToolStripGripRenderEventArgs e)
    {
        base.OnRenderGrip(e);

        if (e.ToolStrip.PortableVisualStylesEnabled)
        {
            ToolStripPortablePainting.PaintGrip(e.Graphics, e.GripBounds, e.ToolStrip,
                e.GripDisplayStyle == ToolStripGripDisplayStyle.Vertical);
            return;
        }

        ToolStripClassicPainting.DrawGripDots(e.Graphics, e.GripBounds, e.GripDisplayStyle);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        base.OnRenderSeparator(e);

        if (ToolStripPortablePainting.PaintSeparator(e.Graphics, e.Item, e.Vertical))
        {
            return;
        }

        ToolStripClassicPainting.DrawEtchedSeparator(e.Graphics, e.Item.Bounds, e.Vertical);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderButtonBackground(e);

        if (ToolStripPortablePainting.PaintItem(e.Graphics, e.Item))
        {
            return;
        }

        if (e.Item is not ToolStripButton button) return;

        ToolStripClassicPainting.DrawButtonFace(e.Graphics, button.Bounds, button.Pressed || button.Checked, button.Selected, button.Enabled);
    }

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderDropDownButtonBackground(e);

        if (ToolStripPortablePainting.PaintItem(e.Graphics, e.Item))
        {
            return;
        }

        ToolStripClassicPainting.DrawButtonFace(e.Graphics, e.Item.Bounds, e.Item.Pressed, e.Item.Selected, e.Item.Enabled);
    }

    protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderSplitButtonBackground(e);

        if (ToolStripPortablePainting.PaintItem(e.Graphics, e.Item))
        {
            return;
        }

        ToolStripClassicPainting.DrawButtonFace(e.Graphics, e.Item.Bounds, e.Item.Pressed, e.Item.Selected, e.Item.Enabled);
    }

    protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderOverflowButtonBackground(e);

        if (ToolStripPortablePainting.PaintItem(e.Graphics, e.Item))
        {
            return;
        }

        ToolStripClassicPainting.DrawButtonFace(e.Graphics, e.Item.Bounds, e.Item.Pressed, e.Item.Selected, e.Item.Enabled);
    }

    /// <summary>
    /// The generic background for an item none of the other OnRender* hooks
    /// claims (a plain ToolStripControlHost, say): the same raised/sunken
    /// face a button gets, so a custom item selected or pressed looks
    /// consistent with the rest of the strip.
    /// </summary>
    protected override void OnRenderItemBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderItemBackground(e);

        if (ToolStripPortablePainting.PaintItem(e.Graphics, e.Item))
        {
            return;
        }

        if (!e.Item.Selected && !e.Item.Pressed) return;

        ToolStripClassicPainting.DrawButtonFace(e.Graphics, e.Item.Bounds, e.Item.Pressed, e.Item.Selected, e.Item.Enabled);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderMenuItemBackground(e);

        if (ToolStripPortablePainting.PaintItem(e.Graphics, e.Item))
        {
            return;
        }

        if (e.Item.Selected)
        {
            ToolStripClassicPainting.DrawButtonFace(e.Graphics, e.Item.Bounds, pressed: false, selected: true, e.Item.Enabled);
        }
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

        if (e.ToolStrip is { PortableVisualStylesEnabled: true } strip && !e.Item.HasExplicitPortableBackColor)
        {
            ToolStripPortablePainting.PaintBackground(e.Graphics, e.Item.Bounds, strip);
            return;
        }

        using var brush = new SolidBrush(e.Item.BackColor);
        e.Graphics.FillRectangle(brush, e.Item.Bounds);
    }

    // Default item image/text/check painting lives on the base ToolStripRenderer (Microsoft's own
    // ToolStripSystemRenderer does not re-override it either); ToolStripProfessionalRenderer does.

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        base.OnRenderImageMargin(e);

        if (e.ToolStrip.PortableVisualStylesEnabled)
        {
            ToolStripPortablePainting.PaintBackground(e.Graphics, e.AffectedBounds, e.ToolStrip);
            return;
        }

        using var brush = new SolidBrush(SystemColors.Control);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }
    internal void PaintSystemArrow(ToolStripArrowRenderEventArgs e)
    {
        var bounds = e.ArrowRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var color = e.ArrowColor;
        var enabled = e.Item?.Enabled ?? true;
        if (e.Item is { Owner: { PortableVisualStylesEnabled: true } strip } item)
        {
            var palette = PortableVisualStyles.Palette(strip.PortableVisualStylePlatform);
            if (color.IsEmpty)
            {
                color = enabled ? palette.Text : palette.DisabledText;
            }
            else if (!enabled && color == item.ForeColor
                && !item.HasExplicitPortableForeColor && !strip.HasExplicitAppearanceForeColor)
            {
                color = palette.DisabledText;
            }
        }
        else if (color.IsEmpty)
        {
            color = enabled ? SystemColors.ControlText : SystemColors.GrayText;
        }

        var centerX = bounds.Left + bounds.Width / 2;
        var centerY = bounds.Top + bounds.Height / 2;
        var half = Math.Max(Math.Min(bounds.Width, bounds.Height) / 2, 1);
        Point[] points = e.Direction switch
        {
            ArrowDirection.Up => [new Point(centerX - half, centerY + half), new Point(centerX + half, centerY + half), new Point(centerX, centerY - half)],
            ArrowDirection.Down => [new Point(centerX - half, centerY - half), new Point(centerX + half, centerY - half), new Point(centerX, centerY + half)],
            ArrowDirection.Left => [new Point(centerX + half, centerY - half), new Point(centerX + half, centerY + half), new Point(centerX - half, centerY)],
            _ => [new Point(centerX - half, centerY - half), new Point(centerX - half, centerY + half), new Point(centerX + half, centerY)]
        };
        var state = e.Graphics.Save();
        try
        {
            e.Graphics.IntersectClip(bounds);
            using var brush = new SolidBrush(color);
            e.Graphics.FillPolygon(brush, points);
        }
        finally
        {
            e.Graphics.Restore(state);
        }
    }

}
