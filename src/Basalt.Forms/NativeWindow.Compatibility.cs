namespace System.Windows.Forms;

public partial class NativeWindow
{
    protected virtual void WndProc(ref Message m) => DefWndProc(ref m);

    protected virtual void WmDpiChangedAfterParent(ref Message m) => WndProc(ref m);

    protected virtual void WmDpiChangedBeforeParent(ref Message m) => WndProc(ref m);
}
