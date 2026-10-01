using System.Drawing;
using System.Drawing.Imaging;

namespace System.Windows.Forms;

public static partial class ControlPaint
{
    /// <summary>
    /// Luminosity (the ITU-R BT.601 weights, the usual "grayscale" colour
    /// matrix) collapses the image to grey, and the translation row then
    /// lifts every channel a third of the way to white: the washed-out look
    /// Windows gives a disabled toolbar icon.
    /// </summary>
    private static readonly ColorMatrix DisabledImageMatrix = new(new float[][]
    {
        [0.299f, 0.299f, 0.299f, 0f, 0f],
        [0.587f, 0.587f, 0.587f, 0f, 0f],
        [0.114f, 0.114f, 0.114f, 0f, 0f],
        [0f, 0f, 0f, 1f, 0f],
        [0.333f, 0.333f, 0.333f, 0f, 1f],
    });

    public static void DrawImageDisabled(Graphics graphics, Image image, int x, int y, Color background)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(image);

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(DisabledImageMatrix);

        var width = image.Width;
        var height = image.Height;

        graphics.DrawImage(image, new Rectangle(x, y, width, height), 0, 0, width, height, GraphicsUnit.Pixel, attributes);
    }

    /// <summary>
    /// Drawn twice, as Windows embosses disabled text: a highlight copy in
    /// ControlLightLight one pixel down and right, then the real copy in
    /// ControlDark at the actual position - always those two colours, not
    /// whatever <paramref name="color"/> the caller asked for, because that
    /// is what makes disabled text look the same everywhere.
    /// </summary>
    public static void DrawStringDisabled(Graphics graphics, string s, Font font, Color color, RectangleF layoutRectangle, StringFormat format)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        using var highlight = new SolidBrush(SystemColors.ControlLightLight);
        using var shadow = new SolidBrush(SystemColors.ControlDark);

        var offset = new RectangleF(layoutRectangle.X + 1, layoutRectangle.Y + 1, layoutRectangle.Width, layoutRectangle.Height);

        graphics.DrawString(s, font, highlight, offset, format);
        graphics.DrawString(s, font, shadow, layoutRectangle, format);
    }

    /// <summary>The IDeviceContext overload of the same embossed text, through TextRenderer (always available here, so there is no Graphics.DrawString fallback to fall back to).</summary>
    public static void DrawStringDisabled(IDeviceContext dc, string s, Font font, Color color, Rectangle layoutRectangle, TextFormatFlags format)
    {
        ArgumentNullException.ThrowIfNull(dc);

        var offset = new Rectangle(layoutRectangle.X + 1, layoutRectangle.Y + 1, layoutRectangle.Width, layoutRectangle.Height);

        TextRenderer.DrawText(dc, s, font, offset, SystemColors.ControlLightLight, format);
        TextRenderer.DrawText(dc, s, font, layoutRectangle, SystemColors.ControlDark, format);
    }
}
