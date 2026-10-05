using System.ComponentModel;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

[Obsolete("`Menu` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `ToolStripDropDown` and `ToolStripDropDownMenu` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
[ListBindable(false)]
[ToolboxItemFilter("System.Windows.Forms")]
public abstract partial class Menu : Component
{
    public const int FindHandle = 0;
    public const int FindShortcut = 1;

    protected Menu(MenuItem[] items) => throw new PlatformNotSupportedException();

    public IntPtr Handle => throw new PlatformNotSupportedException();

    public virtual bool IsParent => throw new PlatformNotSupportedException();

    public MenuItem MdiListItem => throw new PlatformNotSupportedException();

    public Menu.MenuItemCollection MenuItems => throw new PlatformNotSupportedException();

    public string Name
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public object Tag
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    protected void CloneMenu(Menu menuSrc) => throw new PlatformNotSupportedException();

    protected virtual IntPtr CreateMenuHandle() => throw new PlatformNotSupportedException();

    protected internal virtual bool ProcessCmdKey(ref Message msg, Keys keyData) => throw new PlatformNotSupportedException("Legacy menus are not supported. Use ToolStrip menus.");

    public MenuItem FindMenuItem(int type, IntPtr value) => throw new PlatformNotSupportedException();

    protected int FindMergePosition(int mergeOrder) => throw new PlatformNotSupportedException();

    public ContextMenu GetContextMenu() => throw new PlatformNotSupportedException();

    public MainMenu GetMainMenu() => throw new PlatformNotSupportedException();

    public virtual void MergeMenu(Menu menuSrc) => throw new PlatformNotSupportedException();
}
