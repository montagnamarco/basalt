using System.ComponentModel;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

public partial class DataGrid
{
    [Obsolete("`DataGrid` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `DataGridView` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    [Flags]
    public enum HitTestType
    {
        None = 0,
        Cell = 1,
        ColumnHeader = 2,
        RowHeader = 4,
        ColumnResize = 8,
        RowResize = 16,
        Caption = 32,
        ParentRows = 64,
    }
}
