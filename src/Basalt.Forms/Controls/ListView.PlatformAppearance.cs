using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms;

public partial class ListView
{
    private Color SelectionBackColor => CollectionVisualAppearance.SelectionField(this, Focused);

    private Color SelectionForeColor => CollectionVisualAppearance.SelectionText(this, Focused);

    private Color ItemTextColor(ListViewItem.ListViewSubItem subItem) =>
        !Enabled && PortableVisualStylesEnabled
            ? CollectionVisualAppearance.Palette(this).DisabledText
            : subItem.ForeColor;

    private void PaintPortableHeaderFace(Graphics graphics, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var palette = CollectionVisualAppearance.Palette(this);
        using Brush face = PortableVisualStylePlatform == PortableVisualStylePlatform.Windows
            ? new LinearGradientBrush(bounds, Color.White, palette.Face, LinearGradientMode.Vertical)
            : new SolidBrush(palette.Face);
        using var divider = new Pen(palette.Border);
        graphics.FillRectangle(face, bounds);
        graphics.DrawLine(divider, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
        graphics.DrawLine(divider, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
    }
}
