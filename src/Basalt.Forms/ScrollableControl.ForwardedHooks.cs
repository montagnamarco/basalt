using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// WinForms declares these hooks on ScrollableControl. Control already applies
/// their changes to layout, painting, visibility and scaling; the existing
/// ScrollableControl.OnLayout then updates its scrollable area.
/// </summary>
public partial class ScrollableControl
{
    protected override void OnPaddingChanged(EventArgs e) => base.OnPaddingChanged(e);

    protected override void OnPaintBackground(PaintEventArgs e) => base.OnPaintBackground(e);

    protected override void OnRightToLeftChanged(EventArgs e) => base.OnRightToLeftChanged(e);

    protected override void OnVisibleChanged(EventArgs e) => base.OnVisibleChanged(e);

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) => base.ScaleControl(factor, specified);

    protected override void ScaleCore(float dx, float dy) => base.ScaleCore(dx, dy);
}
