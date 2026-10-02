namespace System.Windows.Forms;

/// <summary>
/// Carries the style instance that was attached to a scope (DefaultCellStyle, a column's or row's
/// own DefaultCellStyle, a cell's own Style...) and that scope. Raised when the whole style object
/// is replaced; a mutation of one of its own properties afterwards is not separately tracked, since
/// DataGridViewCellStyle itself (outside this package's files) carries no change notification.
/// </summary>
public class DataGridViewCellStyleContentChangedEventArgs : EventArgs
{
    internal DataGridViewCellStyleContentChangedEventArgs(DataGridViewCellStyle cellStyle, DataGridViewCellStyleScopes cellStyleScope)
    {
        CellStyle = cellStyle;
        CellStyleScope = cellStyleScope;
    }

    public DataGridViewCellStyle CellStyle { get; }

    public DataGridViewCellStyleScopes CellStyleScope { get; }
}
