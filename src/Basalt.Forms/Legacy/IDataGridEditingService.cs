using System.ComponentModel;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

// Binary-compatibility shim family: see DataGrid.cs for the rationale. An
// interface has no body to throw from; it only needs to carry the exact
// signature and attributes Microsoft documents.
[Obsolete("`DataGrid` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `DataGridView` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public interface IDataGridEditingService
{
    bool BeginEdit(DataGridColumnStyle gridColumn, int rowNumber);

    bool EndEdit(DataGridColumnStyle gridColumn, int rowNumber, bool shouldAbort);
}
