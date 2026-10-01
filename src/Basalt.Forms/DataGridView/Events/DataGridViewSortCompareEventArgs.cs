using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// Carries the two cell values Sort is about to compare; a handler sets SortResult and Handled
/// to override the grid's own comparison for that pair.
/// </summary>
public class DataGridViewSortCompareEventArgs : HandledEventArgs
{
    public DataGridViewSortCompareEventArgs(DataGridViewColumn dataGridViewColumn, object? cellValue1, object? cellValue2, int rowIndex1, int rowIndex2)
    {
        Column = dataGridViewColumn;
        CellValue1 = cellValue1;
        CellValue2 = cellValue2;
        RowIndex1 = rowIndex1;
        RowIndex2 = rowIndex2;
    }

    public object? CellValue1 { get; }

    public object? CellValue2 { get; }

    public DataGridViewColumn Column { get; }

    public int RowIndex1 { get; }

    public int RowIndex2 { get; }

    public int SortResult { get; set; }
}
