using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>A vertical scroll bar: Value grows from top to bottom.</summary>
public class VScrollBar : ScrollBar
{
    private protected override Orientation Direction => Orientation.Vertical;

    protected override Size DefaultSize => new(NonClientArea.ScrollBarSize, 80);

    /// <summary>Only the class name changes from ScrollBar's own CreateParams; see HScrollBar's CreateParams for why no WS_VSCROLL bit is set here.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ClassName = "ScrollBar";
            return parameters;
        }
    }

    /// <summary>A vertical scroll bar never mirrors: Microsoft hides RightToLeft here for the same reason.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? RightToLeftChanged
    {
        add => base.RightToLeftChanged += value;
        remove => base.RightToLeftChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override RightToLeft RightToLeft
    {
        get => base.RightToLeft;
        set => base.RightToLeft = value;
    }
}
