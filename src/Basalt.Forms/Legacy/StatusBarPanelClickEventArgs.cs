using System.ComponentModel;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

[Obsolete("`StatusBar` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `StatusStrip` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public class StatusBarPanelClickEventArgs : MouseEventArgs
{
    public StatusBarPanelClickEventArgs(StatusBarPanel statusBarPanel, MouseButtons button, int clicks, int x, int y)
        : base(button, clicks, x, y, 0) => throw new PlatformNotSupportedException();

    public StatusBarPanel StatusBarPanel => throw new PlatformNotSupportedException();
}
