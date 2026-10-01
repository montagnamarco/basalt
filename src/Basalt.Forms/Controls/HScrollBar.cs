using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>A horizontal scroll bar: Value grows from left to right.</summary>
public class HScrollBar : ScrollBar
{
    private protected override Orientation Direction => Orientation.Horizontal;

    protected override Size DefaultSize => new(80, NonClientArea.ScrollBarSize);
}
