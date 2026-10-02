using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

// ImeMode is left out: Control does not declare an ImeMode property in this
// tree (checked Control*.cs and Controls/*.cs), so there is nothing to
// re-declare with the attributes Microsoft's StatusBar.ImeMode carries.
[Obsolete("`StatusBar` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `StatusStrip` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
[DefaultEvent("PanelClick")]
[DefaultProperty("Text")]
public partial class StatusBar : Control
{
    public StatusBar() => throw new PlatformNotSupportedException();

    public override Color BackColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public override Image? BackgroundImage { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public override ImageLayout BackgroundImageLayout { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public override DockStyle Dock { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    protected override bool DoubleBuffered { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public override Font Font { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public override Color ForeColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }

    public StatusBar.StatusBarPanelCollection Panels => throw new PlatformNotSupportedException();

    public bool ShowPanels { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public bool SizingGrip { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }

    // Control.TabStop is a plain (non-virtual) property: StatusBar
    // re-declares it only to flip its default value, hence `new`.
    public new bool TabStop { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }

    // Control declares these as plain (non-virtual) events: StatusBar
    // re-declares them only to flip their design-time visibility, hence `new`.
    public new event EventHandler? BackColorChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public new event EventHandler? BackgroundImageChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public new event EventHandler? BackgroundImageLayoutChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event StatusBarDrawItemEventHandler? DrawItem { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public new event EventHandler? ForeColorChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public new event PaintEventHandler? Paint { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event StatusBarPanelClickEventHandler? PanelClick { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }

    protected virtual void OnDrawItem(StatusBarDrawItemEventArgs sbdievent) => throw new PlatformNotSupportedException();

    protected virtual void OnPanelClick(StatusBarPanelClickEventArgs e) => throw new PlatformNotSupportedException();
}
