using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>A vertical scroll bar: Value grows from top to bottom.</summary>
public class VScrollBar : ScrollBar
{
    private protected override Orientation Direction => Orientation.Vertical;

    protected override Size DefaultSize => new(NonClientArea.ScrollBarSize, 80);
}
