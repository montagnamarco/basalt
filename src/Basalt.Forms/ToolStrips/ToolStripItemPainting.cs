using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// What <see cref="ToolStripButton"/> and <see cref="ToolStripLabel"/> share
/// once their background is drawn: the image, the text, and where each goes
/// within <see cref="ToolStripItem.DisplayStyle"/>.
/// </summary>
internal static class ToolStripItemPainting
{
    public static void DrawImageAndText(ToolStripRenderer renderer, Graphics graphics, ToolStripItem item)
    {
        var showText = (item.DisplayStyle & ToolStripItemDisplayStyle.Text) != 0 && !string.IsNullOrEmpty(item.Text);
        var showImage = (item.DisplayStyle & ToolStripItemDisplayStyle.Image) != 0 && item.ImageToDraw is not null;

        // ContentRectangle is item-relative (it also serves layout, where that is what is wanted); painting runs on
        // the strip's own untranslated Graphics, so it needs the item's absolute position added in.
        var content = item.ContentRectangle;
        content.Offset(item.Bounds.Location);
        if (item.Pressed) content.Offset(1, 1);

        var imageSize = showImage ? item.ImageToDraw!.Size : Size.Empty;
        var textSize = showText ? TextRenderer.MeasureText(item.Text, item.Font) : Size.Empty;

        Rectangle imageRectangle, textRectangle;

        if (showImage && showText)
        {
            // Image before text, the two centred together, as the default TextImageRelation.
            var gap = 2;
            var totalWidth = imageSize.Width + gap + textSize.Width;
            var left = content.Left + Math.Max((content.Width - totalWidth) / 2, 0);

            imageRectangle = new Rectangle(left, content.Top + (content.Height - imageSize.Height) / 2, imageSize.Width, imageSize.Height);
            textRectangle = new Rectangle(imageRectangle.Right + gap, content.Top, content.Width - imageRectangle.Width - gap, content.Height);
        }
        else if (showImage)
        {
            imageRectangle = new Rectangle(
                content.Left + Math.Max((content.Width - imageSize.Width) / 2, 0),
                content.Top + Math.Max((content.Height - imageSize.Height) / 2, 0),
                imageSize.Width,
                imageSize.Height);
            textRectangle = Rectangle.Empty;
        }
        else
        {
            imageRectangle = Rectangle.Empty;
            textRectangle = content;
        }

        if (showImage)
        {
            renderer.DrawItemImage(new ToolStripItemImageRenderEventArgs(graphics, item, item.ImageToDraw, imageRectangle));
        }

        if (showText)
        {
            var format = ContentAlignments.ToTextFormat(item.TextAlign);
            renderer.DrawItemText(new ToolStripItemTextRenderEventArgs(graphics, item, item.Text, textRectangle, item.ForeColor, item.Font, format));
        }
    }
}
