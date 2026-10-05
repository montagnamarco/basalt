using System.ComponentModel;

namespace System.Windows.Forms;

public partial class StatusStrip
{
    public override DockStyle Dock { get => base.Dock; set => base.Dock = value; }
    public new ToolStripGripStyle GripStyle { get => base.GripStyle; set => base.GripStyle = value; }
    public new ToolStripLayoutStyle LayoutStyle { get => base.LayoutStyle; set => base.LayoutStyle = value; }
    public new Padding Padding { get => base.Padding; set => base.Padding = value; }
    public new event EventHandler? PaddingChanged { add => base.PaddingChanged += value; remove => base.PaddingChanged -= value; }
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();
    protected override void Dispose(bool disposing) => base.Dispose(disposing);
    protected override void OnPaintBackground(PaintEventArgs e) => base.OnPaintBackground(e);
    protected virtual void OnSpringTableLayoutCore() => ApplySpringLayout();
    protected override void SetDisplayedItems() => PerformLayout();
}
