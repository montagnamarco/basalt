namespace System.Windows.Forms;

/// <summary>
/// TreeView declares these hooks in the WinForms API. Control already owns
/// handle lifetime, keyboard/mouse event delivery and buffering; node-specific
/// input remains in TreeView.Input and TreeView.Hover.
/// </summary>
public partial class TreeView
{
    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    protected override void CreateHandle() => base.CreateHandle();

    protected override void Dispose(bool disposing) => base.Dispose(disposing);

    protected override void OnHandleCreated(EventArgs e) => base.OnHandleCreated(e);

    protected override void OnHandleDestroyed(EventArgs e) => base.OnHandleDestroyed(e);

    protected override void OnKeyUp(KeyEventArgs e) => base.OnKeyUp(e);

    protected override void OnMouseHover(EventArgs e) => base.OnMouseHover(e);

    protected override bool DoubleBuffered
    {
        get => base.DoubleBuffered;
        set => base.DoubleBuffered = value;
    }
}
