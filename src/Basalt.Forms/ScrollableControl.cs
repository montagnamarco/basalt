using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A control whose children can be scrolled into view, laid out inside its padding.</summary>
public class ScrollableControl : Control
{
    public ScrollableControl()
    {
        SetStyle(ControlStyles.ContainerControl, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint, false);
    }

    /// <summary>The client area less the padding: where children are docked and anchored.</summary>
    [Browsable(false)]
    public override Rectangle DisplayRectangle
    {
        get
        {
            var client = ClientRectangle;
            var padding = Padding;

            return new Rectangle(
                client.X + padding.Left,
                client.Y + padding.Top,
                Math.Max(client.Width - padding.Horizontal, 0),
                Math.Max(client.Height - padding.Vertical, 0));
        }
    }
}
