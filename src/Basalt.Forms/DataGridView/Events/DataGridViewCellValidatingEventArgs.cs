using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// Carries the formatted value about to leave the current cell; a handler sets Cancel to keep
/// editing there. Microsoft gives this type no public constructor: only the grid itself raises it.
/// </summary>
public class DataGridViewCellValidatingEventArgs : CancelEventArgs
{
    internal DataGridViewCellValidatingEventArgs(int columnIndex, int rowIndex, object? formattedValue)
    {
        ColumnIndex = columnIndex;
        RowIndex = rowIndex;
        FormattedValue = formattedValue;
    }

    public int ColumnIndex { get; }

    public object? FormattedValue { get; }

    public int RowIndex { get; }
}
