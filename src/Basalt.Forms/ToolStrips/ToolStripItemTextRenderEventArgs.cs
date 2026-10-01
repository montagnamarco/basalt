using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>What a <see cref="ToolStripRenderer"/> needs to paint an item's text.</summary>
public class ToolStripItemTextRenderEventArgs : ToolStripItemRenderEventArgs
{
    public ToolStripItemTextRenderEventArgs(
        Graphics g, ToolStripItem item, string? text, Rectangle textRectangle, Color textColor, Font? textFont, TextFormatFlags format)
        : base(g, item)
    {
        Text = text;
        TextRectangle = textRectangle;
        TextColor = textColor;
        TextFont = textFont;
        TextFormat = format;
    }

    public ToolStripItemTextRenderEventArgs(
        Graphics g, ToolStripItem item, string? text, Rectangle textRectangle, Color textColor, Font? textFont, ContentAlignment textAlign)
        : this(g, item, text, textRectangle, textColor, textFont, ContentAlignments.ToTextFormat(textAlign))
    {
    }

    public string? Text { get; set; }

    public Color TextColor { get; set; }

    public ToolStripTextDirection TextDirection { get; set; }

    public Font? TextFont { get; set; }

    public TextFormatFlags TextFormat { get; set; }

    public Rectangle TextRectangle { get; set; }
}
