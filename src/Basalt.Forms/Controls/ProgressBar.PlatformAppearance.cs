using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms;

public partial class ProgressBar
{
    private void PaintPortableProgress(Graphics graphics)
    {
        var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform);
        var bounds = ClientRectangle;
        if (bounds.Width < 3 || bounds.Height < 3)
        {
            return;
        }

        using var background = new SolidBrush(HasExplicitAppearanceBackColor ? BackColor : palette.Face);
        using var border = new Pen(palette.Border);
        graphics.FillRectangle(background, bounds);
        graphics.DrawRectangle(border, 0, 0, bounds.Width - 1, bounds.Height - 1);
        var interior = Rectangle.Inflate(bounds, -2, -2);
        if (interior.Width <= 0 || interior.Height <= 0)
        {
            return;
        }

        var color = !Enabled ? palette.DisabledText : HasExplicitAppearanceForeColor ? ForeColor
            : PortableVisualStylePlatform == PortableVisualStylePlatform.Windows ? Color.FromArgb(6, 176, 37) : palette.Accent;
        using Brush fill = PortableVisualStylePlatform == PortableVisualStylePlatform.Windows && !HasExplicitAppearanceForeColor && Enabled
            ? new LinearGradientBrush(interior, Color.FromArgb(160, 225, 170), color, LinearGradientMode.Vertical)
            : new SolidBrush(color);

        if (_style == ProgressBarStyle.Marquee)
        {
            var width = Math.Max(interior.Width / MarqueeBlockFraction, 1);
            var x = interior.Left - width + _marqueeOffset % (interior.Width + width);
            var visible = Rectangle.Intersect(interior, new Rectangle(x, interior.Top, width, interior.Height));
            if (!visible.IsEmpty)
            {
                graphics.FillRectangle(fill, visible);
            }
        }
        else
        {
            var width = (int)(interior.Width * Fraction());
            if (width > 0)
            {
                // Modern profiles use a continuous fill; value and hit geometry stay in logical pixels.
                graphics.FillRectangle(fill, interior.Left, interior.Top, width, interior.Height);
            }
        }
    }
}
