using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>WinForms declarations that use the existing overflow drop-down behavior.</summary>
public partial class ToolStripOverflow
{
    public override LayoutEngine LayoutEngine => base.LayoutEngine;

    public override Size GetPreferredSize(Size proposedSize) => base.GetPreferredSize(proposedSize);

    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    protected override void OnLayout(LayoutEventArgs e) => base.OnLayout(e);
}
