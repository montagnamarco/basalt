namespace System.Windows.Forms;

/// <summary>Carries a cell's raw value for a handler to format itself; setting FormattingApplied skips the cell's own formatting.</summary>
public class DataGridViewCellFormattingEventArgs : ConvertEventArgs
{
    public DataGridViewCellFormattingEventArgs(int columnIndex, int rowIndex, object? value, Type? desiredType, DataGridViewCellStyle cellStyle)
        : base(value, desiredType)
    {
        ColumnIndex = columnIndex;
        RowIndex = rowIndex;
        CellStyle = cellStyle;
    }

    public DataGridViewCellStyle CellStyle { get; set; }

    public int ColumnIndex { get; }

    public bool FormattingApplied { get; set; }

    public int RowIndex { get; }
}
