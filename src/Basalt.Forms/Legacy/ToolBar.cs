using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

// ImeMode is left out: see StatusBar.cs for why (Control has no ImeMode
// property in this tree).
[Obsolete("`ToolBar` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `ToolStrip` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
[DefaultEvent("ButtonClick")]
[DefaultProperty("Buttons")]
public partial class ToolBar : Control
{
    public ToolBar() => throw new PlatformNotSupportedException();

    public ToolBarAppearance Appearance { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public override bool AutoSize { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public override Color BackColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public override Image? BackgroundImage { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public override ImageLayout BackgroundImageLayout { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public BorderStyle BorderStyle { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Size ButtonSize { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }

    public ToolBar.ToolBarButtonCollection Buttons => throw new PlatformNotSupportedException();

    public bool Divider { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public override DockStyle Dock { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    protected override bool DoubleBuffered { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public bool DropDownArrows { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public override Color ForeColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public ImageList ImageList { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Size ImageSize => throw new PlatformNotSupportedException();
    public override RightToLeft RightToLeft { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public bool ShowToolTips { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }

    // Control.TabStop is a plain (non-virtual) property: ToolBar
    // re-declares it only to flip its default value, hence `new`.
    public new bool TabStop { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }

    public ToolBarTextAlign TextAlign { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public bool Wrappable { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }

    // Control declares AutoSizeChanged, the colour/background events and
    // RightToLeftChanged/TextChanged as plain (non-virtual) events: ToolBar
    // re-declares them only to flip their design-time visibility, hence `new`.
    public new event EventHandler? AutoSizeChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public new event EventHandler? BackColorChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public new event EventHandler? BackgroundImageChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public new event EventHandler? BackgroundImageLayoutChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event ToolBarButtonClickEventHandler? ButtonClick { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event ToolBarButtonClickEventHandler? ButtonDropDown { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public new event EventHandler? ForeColorChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public new event PaintEventHandler? Paint { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public new event EventHandler? RightToLeftChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public new event EventHandler? TextChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }

    protected virtual void OnButtonClick(ToolBarButtonClickEventArgs e) => throw new PlatformNotSupportedException();

    protected virtual void OnButtonDropDown(ToolBarButtonClickEventArgs e) => throw new PlatformNotSupportedException();

    protected override void ScaleCore(float dx, float dy) => throw new PlatformNotSupportedException();
}
