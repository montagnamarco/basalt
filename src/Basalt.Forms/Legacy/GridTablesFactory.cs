using System.ComponentModel;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

[Obsolete("`DataGrid` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `DataGridView` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class GridTablesFactory
{
    // Microsoft's reference signature has no public constructor: the type is
    // a static-only utility, never meant to be instantiated.
    private GridTablesFactory() => throw new PlatformNotSupportedException();

    public static DataGridTableStyle[] CreateGridTables(DataGridTableStyle gridTable, object dataSource, string dataMember, BindingContext bindingManager) => throw new PlatformNotSupportedException();
}
