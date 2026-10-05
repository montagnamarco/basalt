using System.Drawing;
using System.Drawing.Drawing2D;

namespace System.Windows.Forms;

/// <summary>
/// The "Office" look: soft blue gradients on hover, pressed and checked,
/// instead of the classic renderer's raised/sunken bevels. Every colour
/// comes from a <see cref="ProfessionalColorTable"/>.
/// </summary>
public class ToolStripProfessionalRenderer : ToolStripRenderer
{
    private readonly bool _usePortablePalette;

    public ToolStripProfessionalRenderer()
        : this(new ProfessionalColorTable())
    {
        _usePortablePalette = true;
    }

    public ToolStripProfessionalRenderer(ProfessionalColorTable professionalColorTable)
    {
        ArgumentNullException.ThrowIfNull(professionalColorTable);

        ColorTable = professionalColorTable;
    }

    public ProfessionalColorTable ColorTable { get; }

    public bool RoundedEdges { get; set; }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        base.OnRenderToolStripBackground(e);

        if (_usePortablePalette && e.ToolStrip.PortableVisualStylesEnabled)
        {
            ToolStripPortablePainting.PaintBackground(e.Graphics, e.AffectedBounds, e.ToolStrip);
            return;
        }

        FillVerticalGradient(e.Graphics, e.AffectedBounds, ColorTable.ToolStripGradientBegin, ColorTable.ToolStripGradientEnd);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        base.OnRenderToolStripBorder(e);

        var bounds = e.ToolStrip.ClientRectangle;
        var border = _usePortablePalette && e.ToolStrip.PortableVisualStylesEnabled
            ? Internal.Rendering.PortableVisualStyles.Palette(e.ToolStrip.PortableVisualStylePlatform).Border
            : ColorTable.ToolStripBorder;
        using var pen = new Pen(border);
        e.Graphics.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
    }

    protected override void OnRenderGrip(ToolStripGripRenderEventArgs e)
    {
        base.OnRenderGrip(e);

        if (_usePortablePalette && e.ToolStrip.PortableVisualStylesEnabled)
        {
            ToolStripPortablePainting.PaintGrip(e.Graphics, e.GripBounds, e.ToolStrip,
                e.GripDisplayStyle == ToolStripGripDisplayStyle.Vertical);
            return;
        }

        using var light = new SolidBrush(ColorTable.GripLight);
        using var dark = new SolidBrush(ColorTable.GripDark);
        DrawGripDots(e.Graphics, e.GripBounds, e.GripDisplayStyle, dark, light);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        base.OnRenderSeparator(e);

        if (_usePortablePalette && ToolStripPortablePainting.PaintSeparator(e.Graphics, e.Item, e.Vertical))
        {
            return;
        }

        using var dark = new Pen(ColorTable.SeparatorDark);
        using var light = new Pen(ColorTable.SeparatorLight);
        var bounds = e.Item.Bounds;

        if (e.Vertical)
        {
            var x = bounds.Left + bounds.Width / 2;
            e.Graphics.DrawLine(dark, x, bounds.Top, x, bounds.Bottom);
            e.Graphics.DrawLine(light, x + 1, bounds.Top, x + 1, bounds.Bottom);
        }
        else
        {
            var y = bounds.Top + bounds.Height / 2;
            e.Graphics.DrawLine(dark, bounds.Left, y, bounds.Right, y);
            e.Graphics.DrawLine(light, bounds.Left, y + 1, bounds.Right, y + 1);
        }
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderButtonBackground(e);

        if (_usePortablePalette && ToolStripPortablePainting.PaintItem(e.Graphics, e.Item))
        {
            return;
        }

        if (e.Item is ToolStripButton { Checked: true } button && !button.Pressed && !button.Selected)
        {
            FillGradientButton(e.Graphics, e.Item.Bounds, ColorTable.ButtonCheckedGradientBegin, ColorTable.ButtonCheckedGradientMiddle, ColorTable.ButtonCheckedGradientEnd, ColorTable.ButtonCheckedHighlightBorder);
            return;
        }

        DrawInteractiveBackground(e.Graphics, e.Item);
    }

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderDropDownButtonBackground(e);

        if (_usePortablePalette && ToolStripPortablePainting.PaintItem(e.Graphics, e.Item))
        {
            return;
        }
        DrawInteractiveBackground(e.Graphics, e.Item);
    }

    protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderSplitButtonBackground(e);

        if (_usePortablePalette && ToolStripPortablePainting.PaintItem(e.Graphics, e.Item))
        {
            return;
        }
        DrawInteractiveBackground(e.Graphics, e.Item);
    }

    protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderOverflowButtonBackground(e);

        if (_usePortablePalette && ToolStripPortablePainting.PaintItem(e.Graphics, e.Item))
        {
            return;
        }

        FillVerticalGradient(e.Graphics, e.Item.Bounds, ColorTable.OverflowButtonGradientBegin, ColorTable.OverflowButtonGradientEnd);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderMenuItemBackground(e);

        if (_usePortablePalette && ToolStripPortablePainting.PaintItem(e.Graphics, e.Item))
        {
            return;
        }

        if (!e.Item.Selected) return;

        FillGradientButton(e.Graphics, e.Item.Bounds, ColorTable.MenuItemSelectedGradientBegin, ColorTable.MenuItemSelected, ColorTable.MenuItemSelectedGradientEnd, ColorTable.MenuItemBorder);
    }

    /// <summary>The item's own image, drawn at the rectangle the layout (or a menu item's own paint) worked out.</summary>
    /// <summary>Nothing extra beyond the base drawing: the Office look does not change how image or text paint.</summary>
    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) => base.OnRenderItemImage(e);

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) => base.OnRenderItemText(e);

    /// <summary>A soft highlighted square in the image margin, as Office shows a checked menu item.</summary>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        base.OnRenderItemCheck(e);

        if (_usePortablePalette && e.ToolStrip is { PortableVisualStylesEnabled: true })
        {
            return;
        }

        var bounds = e.ImageRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var background = e.Item.Pressed ? ColorTable.CheckPressedBackground
            : e.Item.Selected ? ColorTable.CheckSelectedBackground
            : ColorTable.CheckBackground;

        using var face = new SolidBrush(background);
        e.Graphics.FillRectangle(face, bounds);

        using var border = new Pen(ColorTable.ButtonSelectedBorder);
        e.Graphics.DrawRectangle(border, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);

        ControlPaint.DrawMenuGlyph(e.Graphics, Rectangle.Inflate(bounds, -2, -2), MenuGlyph.Checkmark, SystemColors.ControlText, Color.Empty);
    }

    protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderLabelBackground(e);
        if (_usePortablePalette && e.ToolStrip is { PortableVisualStylesEnabled: true }
            && e.Item.HasExplicitPortableBackColor)
        {
            using var background = new SolidBrush(e.Item.BackColor);
            e.Graphics.FillRectangle(background, e.Item.Bounds);
        }
    }

    protected override void OnRenderToolStripStatusLabelBackground(ToolStripItemRenderEventArgs e)
    {
        base.OnRenderToolStripStatusLabelBackground(e);

        if (_usePortablePalette && e.Item?.Owner is { PortableVisualStylesEnabled: true } strip)
        {
            if (e.Item.HasExplicitPortableBackColor)
            {
                using var brush = new SolidBrush(e.Item.BackColor);
                e.Graphics.FillRectangle(brush, e.Item.Bounds);
            }
            else
            {
                ToolStripPortablePainting.PaintBackground(e.Graphics, e.Item.Bounds, strip);
            }
            return;
        }

        FillVerticalGradient(e.Graphics, e.Item.Bounds, ColorTable.StatusStripGradientBegin, ColorTable.StatusStripGradientEnd);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        base.OnRenderImageMargin(e);

        if (_usePortablePalette && e.ToolStrip.PortableVisualStylesEnabled)
        {
            ToolStripPortablePainting.PaintBackground(e.Graphics, e.AffectedBounds, e.ToolStrip);
            return;
        }

        FillVerticalGradient(e.Graphics, e.AffectedBounds, ColorTable.ImageMarginGradientBegin, ColorTable.ImageMarginGradientEnd);
    }

    /// <summary>The band a strip is dragged into: the same soft gradient as the strip itself, unless a handler already painted it.</summary>
    protected override void OnRenderToolStripPanelBackground(ToolStripPanelRenderEventArgs e)
    {
        base.OnRenderToolStripPanelBackground(e);

        if (e.Handled) return;

        var bounds = e.ToolStripPanel.ClientRectangle;
        FillVerticalGradient(e.Graphics, bounds, ColorTable.ToolStripPanelGradientBegin, ColorTable.ToolStripPanelGradientEnd);

        // As in WinForms: the renderer owns the background once it painted it,
        // otherwise the panel's BackColor fill runs afterwards and covers the gradient.
        e.Handled = true;
    }

    /// <summary>The form's own content area in the middle of a ToolStripContainer: plain, as Office leaves it.</summary>
    protected override void OnRenderToolStripContentPanelBackground(ToolStripContentPanelRenderEventArgs e)
    {
        base.OnRenderToolStripContentPanelBackground(e);

        if (e.Handled) return;

        using var brush = new SolidBrush(ColorTable.ToolStripContentPanelGradientEnd);
        e.Graphics.FillRectangle(brush, e.ToolStripContentPanel.ClientRectangle);

        // Same contract as the panel band: painted here, so the base BackColor fill must not follow.
        e.Handled = true;
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        base.OnRenderArrow(e);

        var bounds = e.ArrowRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var cx = bounds.Left + bounds.Width / 2;
        var cy = bounds.Top + bounds.Height / 2;
        var half = Math.Max(Math.Min(bounds.Width, bounds.Height) / 2, 1);

        Point[] points = e.Direction switch
        {
            ArrowDirection.Up => [new Point(cx - half, cy + half), new Point(cx + half, cy + half), new Point(cx, cy - half)],
            ArrowDirection.Down => [new Point(cx - half, cy - half), new Point(cx + half, cy - half), new Point(cx, cy + half)],
            ArrowDirection.Left => [new Point(cx + half, cy - half), new Point(cx + half, cy + half), new Point(cx - half, cy)],
            _ => [new Point(cx - half, cy - half), new Point(cx - half, cy + half), new Point(cx + half, cy)],
        };

        var arrowColor = e.ArrowColor;
        if (arrowColor.IsEmpty)
        {
            if (_usePortablePalette && e.Item?.Owner is { PortableVisualStylesEnabled: true } strip)
            {
                var palette = Internal.Rendering.PortableVisualStyles.Palette(strip.PortableVisualStylePlatform);
                arrowColor = e.Item?.Enabled == true ? palette.Text : palette.DisabledText;
            }
            else
            {
                arrowColor = SystemColors.ControlText;
            }
        }
        using var brush = new SolidBrush(arrowColor);
        e.Graphics.FillPolygon(brush, points);
    }

    /// <summary>The hover/pressed gradient every clickable item (button, drop-down, split, overflow) shares.</summary>
    private void DrawInteractiveBackground(Graphics graphics, ToolStripItem item)
    {
        if (item.Pressed)
        {
            FillGradientButton(graphics, item.Bounds, ColorTable.ButtonPressedGradientBegin, ColorTable.ButtonPressedGradientMiddle, ColorTable.ButtonPressedGradientEnd, ColorTable.ButtonPressedBorder);
        }
        else if (item.Selected && item.Enabled)
        {
            FillGradientButton(graphics, item.Bounds, ColorTable.ButtonSelectedGradientBegin, ColorTable.ButtonSelectedGradientMiddle, ColorTable.ButtonSelectedGradientEnd, ColorTable.ButtonSelectedBorder);
        }
    }

    private void FillGradientButton(Graphics graphics, Rectangle bounds, Color begin, Color middle, Color end, Color border)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using (var brush = new LinearGradientBrush(bounds, begin, end, LinearGradientMode.Vertical))
        {
            graphics.FillRectangle(brush, bounds);
        }

        using var pen = new Pen(border);
        var edge = Rectangle.Inflate(bounds, -1, -1);
        if (edge.Width > 0 && edge.Height > 0) graphics.DrawRectangle(pen, edge.Left, edge.Top, edge.Width, edge.Height);
    }

    private static void FillVerticalGradient(Graphics graphics, Rectangle bounds, Color begin, Color end)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using var brush = new LinearGradientBrush(bounds, begin, end, LinearGradientMode.Vertical);
        graphics.FillRectangle(brush, bounds);
    }

    private static void DrawGripDots(Graphics graphics, Rectangle bounds, ToolStripGripDisplayStyle displayStyle, Brush dark, Brush light)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        const int spacing = 3;

        if (displayStyle == ToolStripGripDisplayStyle.Vertical)
        {
            var x = bounds.Left + Math.Max(bounds.Width / 2 - 1, 0);

            for (var y = bounds.Top + 2; y + 1 < bounds.Bottom - 1; y += spacing)
            {
                graphics.FillRectangle(dark, x, y, 1, 1);
                graphics.FillRectangle(light, x + 1, y + 1, 1, 1);
            }
        }
        else
        {
            var y = bounds.Top + Math.Max(bounds.Height / 2 - 1, 0);

            for (var x = bounds.Left + 2; x + 1 < bounds.Right - 1; x += spacing)
            {
                graphics.FillRectangle(dark, x, y, 1, 1);
                graphics.FillRectangle(light, x + 1, y + 1, 1, 1);
            }
        }
    }
}
