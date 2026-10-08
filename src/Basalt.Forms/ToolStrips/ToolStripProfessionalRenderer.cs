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

        // A drop-down (a menu's own popup, a context menu, a drop-down list) gets a flat, near-white
        // fill of its own, ColorTable.ToolStripDropDownBackground: Windows does not shade a menu's body
        // with the same vertical gradient a docked toolbar or menu bar gets, and painting it with that
        // gradient regardless (as if ToolStripDropDownBackground did not exist) is what gave every
        // drop-down the toolbar's flat-grey, "classic" cast instead of the near-white a real menu has.
        if (e.ToolStrip is ToolStripDropDown)
        {
            using var dropDownBrush = new SolidBrush(ColorTable.ToolStripDropDownBackground);
            e.Graphics.FillRectangle(dropDownBrush, e.AffectedBounds);
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
            : e.ToolStrip is ToolStripDropDown ? ColorTable.MenuBorder : ColorTable.ToolStripBorder;
        using var pen = new Pen(border);

        // A drop-down (a menu's own popup, a context menu, a drop-down list) is a free-floating
        // rectangle, so every edge needs a border; an ordinary bar only meets the rest of the form
        // along its bottom edge, so only that one line is drawn.
        if (e.ToolStrip is ToolStripDropDown && bounds.Width > 0 && bounds.Height > 0)
        {
            e.Graphics.DrawRectangle(pen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
        }
        else
        {
            e.Graphics.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
        }
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
        var bounds = SeparatorBounds(e.Item);

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

        // Disabled items never highlight, even if some leftover hover kept Selected true: Windows greys
        // a disabled caption instead (ToolStripMenuItem.OnPaint already draws it that way) and never
        // paints a selection fill under it.
        if (!e.Item.Selected || !e.Item.Enabled) return;

        // A flat fill (not the three-stop gradient a hovered ToolStripButton gets): Windows 10/11 shows
        // a hovered menu item as a single pale blue with a slightly darker 1-px border, not a raised or
        // shaded button face.
        var bounds = e.Item.Bounds;
        using var fill = new SolidBrush(ColorTable.MenuItemSelected);
        e.Graphics.FillRectangle(fill, bounds);

        using var border = new Pen(ColorTable.MenuItemBorder);
        e.Graphics.DrawRectangle(border, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
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

        FillVerticalGradient3(e.Graphics, e.AffectedBounds, ColorTable.ImageMarginGradientBegin, ColorTable.ImageMarginGradientMiddle, ColorTable.ImageMarginGradientEnd);
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

        // The full rectangle, not a square confined to its smaller dimension: a submenu's own glyph is
        // narrower than it is tall (about 4x7 px at 100% scaling on Windows 10), and squashing it into a
        // symmetric square around its centre (as a single "half" radius for both axes would) is what drew
        // every arrow this renderer painted - submenu included - as a fat, equilateral triangle instead.
        var midY = bounds.Top + bounds.Height / 2;
        var midX = bounds.Left + bounds.Width / 2;

        Point[] points = e.Direction switch
        {
            ArrowDirection.Up => [new Point(bounds.Left, bounds.Bottom), new Point(bounds.Right, bounds.Bottom), new Point(midX, bounds.Top)],
            ArrowDirection.Down => [new Point(bounds.Left, bounds.Top), new Point(bounds.Right, bounds.Top), new Point(midX, bounds.Bottom)],
            ArrowDirection.Left => [new Point(bounds.Right, bounds.Top), new Point(bounds.Right, bounds.Bottom), new Point(bounds.Left, midY)],
            _ => [new Point(bounds.Left, bounds.Top), new Point(bounds.Left, bounds.Bottom), new Point(bounds.Right, midY)],
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

    /// <summary>
    /// The image margin's own three-stop gradient (begin at the top, middle at the centre, end at the
    /// bottom): the only column this renderer paints from all three of a colour table's
    /// ImageMarginGradientBegin/Middle/End, rather than just the two-stop Begin/End every other gradient
    /// here uses.
    /// </summary>
    private static void FillVerticalGradient3(Graphics graphics, Rectangle bounds, Color begin, Color middle, Color end)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var topHalf = new Rectangle(bounds.Left, bounds.Top, bounds.Width, Math.Max(bounds.Height / 2, 1));
        var bottomHalf = new Rectangle(bounds.Left, topHalf.Bottom, bounds.Width, Math.Max(bounds.Bottom - topHalf.Bottom, 1));

        FillVerticalGradient(graphics, topHalf, begin, middle);
        FillVerticalGradient(graphics, bottomHalf, middle, end);
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
