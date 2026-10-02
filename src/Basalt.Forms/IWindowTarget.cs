using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>The window procedure of a control, which Control.WindowTarget lets an application replace.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IWindowTarget
{
    void OnHandleChange(IntPtr newHandle);

    void OnMessage(ref Message m);
}
