using System.ComponentModel;
using System.Windows.Forms.Design;

namespace System.Windows.Forms;

public abstract partial class AxHost
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

    /// <summary>ActiveX component pages require the COM object AxHost cannot create here.</summary>
    public class AxComponentEditor : WindowsFormsComponentEditor
    {
        public override bool EditComponent(ITypeDescriptorContext? context, object component, IWin32Window? owner) =>
            throw new PlatformNotSupportedException("ActiveX component pages require a Windows COM object.");
    }
}
