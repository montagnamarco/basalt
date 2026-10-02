using System.ComponentModel;
using System.Drawing;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

// ImageIndex/ImageKey drop their [TypeConverter]/[Editor] attributes:
// ImageIndexConverter, ImageKeyConverter and System.Drawing.Design.UITypeEditor
// are not referenced by this project (no FrameworkReference to
// System.Drawing.Design, and the converters are not part of the obsolete
// family so this phase does not add them). See the worktree report.
[Obsolete("`ToolBar` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `ToolStrip` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
[DefaultProperty("Text")]
[ToolboxItem(false)]
[DesignTimeVisible(false)]
public class ToolBarButton : Component
{
    public ToolBarButton() => throw new PlatformNotSupportedException();

    public ToolBarButton(string text) => throw new PlatformNotSupportedException();

    public Menu DropDownMenu
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public bool Enabled
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public int ImageIndex
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public string ImageKey
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public string Name
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public ToolBar Parent => throw new PlatformNotSupportedException();

    public bool PartialPush
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public bool Pushed
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public Rectangle Rectangle => throw new PlatformNotSupportedException();

    public ToolBarButtonStyle Style
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public object Tag
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public string Text
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public string ToolTipText
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public bool Visible
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }
}
