using System.ComponentModel;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

[Obsolete("`Menu` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `ToolStripDropDown` and `ToolStripDropDownMenu` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
[ToolboxItem(false)]
[DesignTimeVisible(false)]
[DefaultEvent("Click")]
[DefaultProperty("Text")]
public class MenuItem : Menu
{
    public MenuItem() : base(Array.Empty<MenuItem>()) => throw new PlatformNotSupportedException();

    public MenuItem(string text) : base(Array.Empty<MenuItem>()) => throw new PlatformNotSupportedException();

    public MenuItem(string text, EventHandler onClick) : base(Array.Empty<MenuItem>()) => throw new PlatformNotSupportedException();

    public MenuItem(string text, MenuItem[] items) : base(items) => throw new PlatformNotSupportedException();

    public MenuItem(string text, EventHandler onClick, Shortcut shortcut) : base(Array.Empty<MenuItem>()) => throw new PlatformNotSupportedException();

    public MenuItem(MenuMerge mergeType, int mergeOrder, Shortcut shortcut, string text, EventHandler onClick, EventHandler onPopup, EventHandler onSelect, MenuItem[] items) : base(items) => throw new PlatformNotSupportedException();

    public bool BarBreak
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public bool Break
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public bool Checked
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public bool DefaultItem
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public bool Enabled
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public int Index
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public override bool IsParent => throw new PlatformNotSupportedException();

    public bool MdiList
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    protected int MenuID => throw new PlatformNotSupportedException();

    public int MergeOrder
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public MenuMerge MergeType
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public char Mnemonic => throw new PlatformNotSupportedException();

    public bool OwnerDraw
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public Menu Parent => throw new PlatformNotSupportedException();

    public bool RadioCheck
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public Shortcut Shortcut
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public bool ShowShortcut
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public string Text
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public bool Visible
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public event EventHandler? Click { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event DrawItemEventHandler? DrawItem { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event MeasureItemEventHandler? MeasureItem { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event EventHandler? Popup { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event EventHandler? Select { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }

    public virtual MenuItem CloneMenu() => throw new PlatformNotSupportedException();

    protected void CloneMenu(MenuItem itemSrc) => throw new PlatformNotSupportedException();

    public virtual MenuItem MergeMenu() => throw new PlatformNotSupportedException();

    public void MergeMenu(MenuItem itemSrc) => throw new PlatformNotSupportedException();

    protected virtual void OnClick(EventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnDrawItem(DrawItemEventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnInitMenuPopup(EventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnMeasureItem(MeasureItemEventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnPopup(EventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnSelect(EventArgs e) => throw new PlatformNotSupportedException();

    public void PerformClick() => throw new PlatformNotSupportedException();

    public virtual void PerformSelect() => throw new PlatformNotSupportedException();
}
