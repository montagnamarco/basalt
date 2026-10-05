using System.Drawing;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Rendering;
using System.Windows.Forms.Internal.Skins;

namespace System.Windows.Forms;

public partial class Control
{
    private void PaintPortableNonClient(Graphics graphics, int style, int extendedStyle)
    {
        if (!PortableVisualStylesEnabled)
        {
            return;
        }

        var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform);
        var hasCaption = (style & WindowStyles.Caption) == WindowStyles.Caption;
        var hasEdge = (extendedStyle & (WindowStyles.ExClientEdge | WindowStyles.ExStaticEdge)) != 0;
        var hasBorder = (style & WindowStyles.Border) != 0;
        var explicitTextAppearance = this is TextBoxBase && HasExplicitAppearanceBackColor;
        if (!hasCaption && !explicitTextAppearance && (hasBorder || hasEdge) && Width >= 4 && Height >= 4)
        {
            using var outline = new Pen(Focused ? palette.Accent : palette.Border);
            graphics.DrawRectangle(outline, 0, 0, Width - 1, Height - 1);
            if (hasEdge)
            {
                using var inner = new Pen(BackColor);
                graphics.DrawRectangle(inner, 1, 1, Width - 3, Height - 3);
            }
        }

        PaintPortableWindowScrollBar(graphics, style, extendedStyle, vertical: true, palette);
        PaintPortableWindowScrollBar(graphics, style, extendedStyle, vertical: false, palette);
        var corner = WindowScrollBars.Corner(Size, style, extendedStyle);
        if (!corner.IsEmpty)
        {
            using var background = new SolidBrush(palette.Surface);
            graphics.FillRectangle(background, corner);
        }
    }

    private void PaintPortableWindowScrollBar(Graphics graphics, int style, int extendedStyle,
        bool vertical, PortableVisualStylePalette palette)
    {
        var bounds = vertical ? WindowScrollBars.Vertical(Size, style, extendedStyle)
            : WindowScrollBars.Horizontal(Size, style, extendedStyle);
        if (bounds.IsEmpty)
        {
            return;
        }

        var orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        var info = GetWindowScroll(vertical) ?? new WindowScrollInfo(0, 0, 0, 0, false);
        var track = ScrollBarPainter.TrackBounds(orientation, bounds);
        ScrollBarPainter.DrawPortable(graphics, orientation, bounds, info.ThumbBounds(track, vertical),
            ScrollBarPainter.PressedPart.None, info.Enabled, palette, PortableVisualStylePlatform);
    }
}
