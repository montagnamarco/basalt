using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>WinForms declarations backed by the existing drop-down layout and painting behavior.</summary>
public partial class ToolStripDropDownMenu
{
    public override LayoutEngine LayoutEngine => base.LayoutEngine;

    public new ToolStripLayoutStyle LayoutStyle
    {
        get => base.LayoutStyle;
        set => base.LayoutStyle = value;
    }

    protected override Size MaxItemSize => base.MaxItemSize;

    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    protected override void OnFontChanged(EventArgs e) => base.OnFontChanged(e);

    protected override void OnLayout(LayoutEventArgs e) => base.OnLayout(e);

    protected override void OnPaintBackground(PaintEventArgs e) => base.OnPaintBackground(e);
}
