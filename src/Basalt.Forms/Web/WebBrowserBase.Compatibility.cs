using System.ComponentModel;

namespace System.Windows.Forms;

public partial class WebBrowserBase
{
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new ImeMode ImeMode
    {
        get => base.ImeMode;
        set => base.ImeMode = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event UICuesEventHandler? ChangeUICues
    {
        add => base.ChangeUICues += value;
        remove => base.ChangeUICues -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? ImeModeChanged
    {
        add => base.ImeModeChanged += value;
        remove => base.ImeModeChanged -= value;
    }

    protected override void WndProc(ref Message m) => base.WndProc(ref m);
}
