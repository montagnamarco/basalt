using System.Drawing;
using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms;

internal static class ToolStripPortablePainting
{
    internal static bool PaintItem(Graphics graphics, ToolStripItem item)
    {
        if (item.Owner is not { PortableVisualStylesEnabled: true } strip)
        {
            return false;
        }

        if (item.HasExplicitPortableBackColor || strip.HasExplicitAppearanceBackColor)
        {
            using var background = new SolidBrush(item.BackColor);
            graphics.FillRectangle(background, item.Bounds);
            var active = item.Enabled && (item.Selected || item.Pressed || item is ToolStripButton { Checked: true });
            if (active && item.Bounds.Width > 1 && item.Bounds.Height > 1)
            {
                var palette = PortableVisualStyles.Palette(strip.PortableVisualStylePlatform);
                using var outline = new Pen(palette.Accent);
                var bounds = item.Bounds;
                graphics.DrawRectangle(outline, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
                if ((item.Pressed || item is ToolStripButton { Checked: true }) && bounds.Width > 3 && bounds.Height > 3)
                {
                    graphics.DrawRectangle(outline, bounds.X + 1, bounds.Y + 1, bounds.Width - 3, bounds.Height - 3);
                }
            }
            return true;
        }

        var checkedItem = item is ToolStripButton { Checked: true };
        if (item.Enabled && (item.Selected || item.Pressed || checkedItem))
        {
            var palette = PortableVisualStyles.Palette(strip.PortableVisualStylePlatform);
            PortableVisualStyles.PaintButton(graphics, item.Bounds, palette, strip.PortableVisualStylePlatform,
                item.Selected, item.Pressed || checkedItem, item.Enabled, isDefault: false);
        }
        return true;
    }

    /// <summary>Gap between a separator line's ends and the item's edges, so the line stops short
    /// of the strip border the way a native separator does instead of touching it.</summary>
    private const int SeparatorEndInset = 3;

    internal static bool PaintSeparator(Graphics graphics, ToolStripItem item, bool vertical)
    {
        if (item.Owner is not { PortableVisualStylesEnabled: true } strip)
        {
            return false;
        }
        var bounds = item.Bounds;
        using var line = new Pen(PortableVisualStyles.Palette(strip.PortableVisualStylePlatform).Border);
        if (vertical)
        {
            var x = bounds.Left + bounds.Width / 2;
            graphics.DrawLine(line, x, bounds.Top + SeparatorEndInset, x, bounds.Bottom - SeparatorEndInset);
        }
        else
        {
            var y = bounds.Top + bounds.Height / 2;
            graphics.DrawLine(line, bounds.Left + SeparatorEndInset, y, bounds.Right - SeparatorEndInset, y);
        }
        return true;
    }

    /// <summary>Margin from the grip's own edges before the first pair of dots, matching the
    /// inset a native move-handle grip leaves around its dot pattern.</summary>
    private const int GripDotMargin = 3;

    /// <summary>Spacing between successive dot pairs along the grip, reproducing the
    /// 4px dot-to-dot pitch of a native move-handle grip's two staggered columns.</summary>
    private const int GripDotPitch = 4;

    internal static void PaintGrip(Graphics graphics, Rectangle bounds, ToolStrip strip, bool vertical)
    {
        using var dots = new SolidBrush(PortableVisualStyles.Palette(strip.PortableVisualStylePlatform).Border);
        for (var offset = GripDotMargin; offset < (vertical ? bounds.Height : bounds.Width) - GripDotMargin; offset += GripDotPitch)
        {
            var x = vertical ? bounds.Left + bounds.Width / 2 : bounds.Left + offset;
            var y = vertical ? bounds.Top + offset : bounds.Top + bounds.Height / 2;
            graphics.FillRectangle(dots, x, y, 1, 1);
            graphics.FillRectangle(dots, x + 2, y + (vertical ? 0 : 2), 1, 1);
        }
    }

    /// <summary>Distance of the size grip's diagonal lines from the bottom-right corner: three
    /// parallel strokes, 4px apart (the pitch native grips use), starting 4px in and ending
    /// 12px in so the grip fits the 16px-ish corner a StatusStrip reserves for it.</summary>
    private const int SizeGripFirstOffset = 4;
    private const int SizeGripLastOffset = 12;
    private const int SizeGripStep = 4;

    /// <summary>Inset of each stroke's far end from the bottom and right edges, keeping the
    /// anti-aliased line inside the control's bounds.</summary>
    private const int SizeGripEdgeInset = 2;

    internal static void PaintSizeGrip(Graphics graphics, Rectangle bounds, ToolStrip strip)
    {
        using var line = new Pen(PortableVisualStyles.Palette(strip.PortableVisualStylePlatform).Border);
        for (var offset = SizeGripFirstOffset; offset <= SizeGripLastOffset; offset += SizeGripStep)
        {
            graphics.DrawLine(line, bounds.Right - offset, bounds.Bottom - SizeGripEdgeInset, bounds.Right - SizeGripEdgeInset, bounds.Bottom - offset);
        }
    }

    internal static Color Background(ToolStrip strip) => strip.HasExplicitAppearanceBackColor
        ? strip.BackColor
        : PortableVisualStyles.Palette(strip.PortableVisualStylePlatform).Face;

    internal static void PaintBackground(Graphics graphics, Rectangle bounds, ToolStrip strip)
    {
        using var background = new SolidBrush(Background(strip));
        graphics.FillRectangle(background, bounds);
    }
}
