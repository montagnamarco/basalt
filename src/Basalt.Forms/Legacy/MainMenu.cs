using System.ComponentModel;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

[Obsolete("`MainMenu` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `MenuStrip` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
[ToolboxItemFilter("System.Windows.Forms.MainMenu")]
public class MainMenu : Menu
{
    public MainMenu() : base(Array.Empty<MenuItem>()) => throw new PlatformNotSupportedException();

    public MainMenu(IContainer container) : base(Array.Empty<MenuItem>()) => throw new PlatformNotSupportedException();

    public MainMenu(MenuItem[] items) : base(items) => throw new PlatformNotSupportedException();

    public virtual RightToLeft RightToLeft
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public event EventHandler? Collapse
    {
        add => throw new PlatformNotSupportedException();
        remove => throw new PlatformNotSupportedException();
    }

    public virtual MainMenu CloneMenu() => throw new PlatformNotSupportedException();

    public Form GetForm() => throw new PlatformNotSupportedException();

    protected virtual void OnCollapse(EventArgs e) => throw new PlatformNotSupportedException();
}
