namespace System.Windows.Forms;

/// <summary>Carries a cell's edited text for a handler to parse itself; setting ParsingApplied skips the cell's own parsing.</summary>
public class DataGridViewCellParsingEventArgs : ConvertEventArgs
{
    public DataGridViewCellParsingEventArgs(int rowIndex, int columnIndex, object? value, Type? desiredType, DataGridViewCellStyle? inheritedCellStyle)
        : base(value, desiredType)
    {
        RowIndex = rowIndex;
        ColumnIndex = columnIndex;
        InheritedCellStyle = inheritedCellStyle;
    }

    public int ColumnIndex { get; }

    public DataGridViewCellStyle? InheritedCellStyle { get; set; }

    public bool ParsingApplied { get; set; }

    public int RowIndex { get; }
}
