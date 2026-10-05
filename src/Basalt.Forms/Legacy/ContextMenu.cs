using System.ComponentModel;
using System.Drawing;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

[DefaultEvent("Popup")]
[Obsolete("`ContextMenu` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `ContextMenuStrip` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public class ContextMenu : Menu
{
    public ContextMenu() : base(Array.Empty<MenuItem>()) => throw new PlatformNotSupportedException();

    public ContextMenu(MenuItem[] menuItems) : base(menuItems) => throw new PlatformNotSupportedException();

    public virtual RightToLeft RightToLeft
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public Control SourceControl => throw new PlatformNotSupportedException();

    public event EventHandler? Collapse
    {
        add => throw new PlatformNotSupportedException();
        remove => throw new PlatformNotSupportedException();
    }

    public event EventHandler? Popup
    {
        add => throw new PlatformNotSupportedException();
        remove => throw new PlatformNotSupportedException();
    }

    protected virtual void OnCollapse(EventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnPopup(EventArgs e) => throw new PlatformNotSupportedException();

    protected internal virtual bool ProcessCmdKey(ref Message msg, Keys keyData, Control control) => base.ProcessCmdKey(ref msg, keyData);

    public void Show(Control control, Point pos) => throw new PlatformNotSupportedException();

    public void Show(Control control, Point pos, LeftRightAlignment alignment) => throw new PlatformNotSupportedException();
}
