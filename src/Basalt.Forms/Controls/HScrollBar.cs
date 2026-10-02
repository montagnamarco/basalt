using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>A horizontal scroll bar: Value grows from left to right.</summary>
public class HScrollBar : ScrollBar
{
    private protected override Orientation Direction => Orientation.Horizontal;

    protected override Size DefaultSize => new(80, NonClientArea.ScrollBarSize);

    /// <summary>
    /// Only the class name changes from ScrollBar's own CreateParams: WS_HSCROLL/WS_VSCROLL
    /// (WindowStyles.HorizontalScroll/VerticalScroll) mean something else in
    /// this codebase - a container reserving space for its own scroll bar
    /// strip (see WindowScrollBars) - not "this is a horizontal scroll bar",
    /// so setting either here would shrink this control's own client area to nothing.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ClassName = "ScrollBar";
            return parameters;
        }
    }
}
