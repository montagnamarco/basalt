using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

// Split across DataGrid.cs (ctor, properties, events) and
// DataGrid.Methods.cs (methods) to keep each file under ~250 lines; the
// nested HitTestInfo/HitTestType live in their own files.
[Obsolete("`DataGrid` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `DataGridView` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
[DefaultProperty("DataSource")]
[DefaultEvent("Navigate")]
[ComplexBindingProperties("DataSource", "DataMember")]
public partial class DataGrid : Control, ISupportInitialize, IDataGridEditingService
{
    public DataGrid() => throw new PlatformNotSupportedException();

    public bool AllowNavigation { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public bool AllowSorting { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Color AlternatingBackColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Color BackgroundColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public override Image? BackgroundImage { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public override ImageLayout BackgroundImageLayout { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public BorderStyle BorderStyle { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Color CaptionBackColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Font CaptionFont { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Color CaptionForeColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public string CaptionText { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public bool CaptionVisible { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public bool ColumnHeadersVisible { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public DataGridCell CurrentCell { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public int CurrentRowIndex { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public override Cursor Cursor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public string DataMember { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public object DataSource { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public int FirstVisibleColumn => throw new PlatformNotSupportedException();
    public bool FlatMode { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Color GridLineColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public DataGridLineStyle GridLineStyle { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Color HeaderBackColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Font HeaderFont { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Color HeaderForeColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    protected ScrollBar HorizScrollBar => throw new PlatformNotSupportedException();

    public object this[int rowIndex, int columnIndex]
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public object this[DataGridCell cell]
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    public Color LinkColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Color LinkHoverColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    protected CurrencyManager ListManager { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Color ParentRowsBackColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Color ParentRowsForeColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public DataGridParentRowsLabelStyle ParentRowsLabelStyle { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public bool ParentRowsVisible { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public int PreferredColumnWidth { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public int PreferredRowHeight { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public bool ReadOnly { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public int RowHeaderWidth { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public bool RowHeadersVisible { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Color SelectionBackColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public Color SelectionForeColor { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    public GridTableStylesCollection TableStyles => throw new PlatformNotSupportedException();
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text { get => throw new PlatformNotSupportedException(); set => throw new PlatformNotSupportedException(); }
    protected ScrollBar VertScrollBar => throw new PlatformNotSupportedException();
    public int VisibleColumnCount => throw new PlatformNotSupportedException();
    public int VisibleRowCount => throw new PlatformNotSupportedException();

    public event EventHandler? AllowNavigationChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event EventHandler? BackButtonClick { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event EventHandler? BackgroundColorChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    // Control declares these three as plain (non-virtual) events: DataGrid
    // re-declares them only to flip their design-time visibility, hence `new`.
    public new event EventHandler? BackgroundImageChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public new event EventHandler? BackgroundImageLayoutChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event EventHandler? BorderStyleChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event EventHandler? CaptionVisibleChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event EventHandler? CurrentCellChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public new event EventHandler? CursorChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event EventHandler? DataSourceChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event EventHandler? FlatModeChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event NavigateEventHandler? Navigate { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event EventHandler? ParentRowsLabelStyleChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event EventHandler? ParentRowsVisibleChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event EventHandler? ReadOnlyChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    protected event EventHandler? RowHeaderClick { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event EventHandler? Scroll { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public event EventHandler? ShowParentDetailsButtonClick { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
    public new event EventHandler? TextChanged { add => throw new PlatformNotSupportedException(); remove => throw new PlatformNotSupportedException(); }
}
