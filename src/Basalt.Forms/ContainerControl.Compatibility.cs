namespace System.Windows.Forms;

public partial class ContainerControl
{
    // Preserve the declared override slots used by designer reflection and
    // derived containers while retaining the shared control implementation.
    protected override void AdjustFormScrollbars(bool displayScrollbars) => base.AdjustFormScrollbars(displayScrollbars);
    protected override void Dispose(bool disposing) => base.Dispose(disposing);
    protected override void OnCreateControl() => base.OnCreateControl();
    protected override void OnFontChanged(EventArgs e) => base.OnFontChanged(e);
    protected override void OnLayout(LayoutEventArgs e) => base.OnLayout(e);
    protected override void OnMove(EventArgs e) => base.OnMove(e);
    protected override void OnParentChanged(EventArgs e) => base.OnParentChanged(e);
    protected override void OnResize(EventArgs e) => base.OnResize(e);
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => base.ProcessCmdKey(ref msg, keyData);
    protected override CreateParams CreateParams => base.CreateParams;
    protected override bool CanEnableIme => base.CanEnableIme;
}
