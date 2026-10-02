using System.ComponentModel;
using System.Drawing;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

[Obsolete("`StatusBar` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `StatusStrip` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public class StatusBarDrawItemEventArgs : DrawItemEventArgs
{
    public StatusBarDrawItemEventArgs(Graphics g, Font font, Rectangle r, int itemId, DrawItemState itemState, StatusBarPanel panel)
        : base(g, font, r, itemId, itemState) => throw new PlatformNotSupportedException();

    public StatusBarDrawItemEventArgs(Graphics g, Font font, Rectangle r, int itemId, DrawItemState itemState, StatusBarPanel panel, Color foreColor, Color backColor)
        : base(g, font, r, itemId, itemState, foreColor, backColor) => throw new PlatformNotSupportedException();

    public StatusBarPanel Panel => throw new PlatformNotSupportedException();
}
