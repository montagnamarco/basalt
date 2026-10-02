using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// One cell's paint pass, raised right before DataGridView paints it itself: a handler that sets
/// Handled skips the grid's own painting entirely, otherwise it can still call Paint/PaintBackground/
/// PaintContent to draw parts of the default look before adding its own on top.
/// </summary>
public class DataGridViewCellPaintingEventArgs : HandledEventArgs
{
    private readonly System.Windows.Forms.DataGridView _dataGridView;
    private readonly DataGridViewCell? _cell;
    private readonly DataGridViewAdvancedBorderStyle _advancedBorderStyle;

    internal DataGridViewCellPaintingEventArgs(
        System.Windows.Forms.DataGridView dataGridView,
        DataGridViewCell? cell,
        Graphics graphics,
        Rectangle clipBounds,
        Rectangle cellBounds,
        int rowIndex,
        int columnIndex,
        DataGridViewElementStates cellState,
        object? value,
        object? formattedValue,
        string? errorText,
        DataGridViewCellStyle cellStyle,
        DataGridViewAdvancedBorderStyle? advancedBorderStyle,
        DataGridViewPaintParts paintParts)
    {
        _dataGridView = dataGridView;
        _cell = cell;
        Graphics = graphics;
        ClipBounds = clipBounds;
        CellBounds = cellBounds;
        RowIndex = rowIndex;
        ColumnIndex = columnIndex;
        State = cellState;
        Value = value;
        FormattedValue = formattedValue;
        ErrorText = errorText;
        CellStyle = cellStyle;
        _advancedBorderStyle = advancedBorderStyle ?? new DataGridViewAdvancedBorderStyle();
        AdvancedBorderStyle = advancedBorderStyle;
        PaintParts = paintParts;
    }

    public DataGridViewAdvancedBorderStyle? AdvancedBorderStyle { get; }

    public Rectangle CellBounds { get; }

    public DataGridViewCellStyle? CellStyle { get; }

    public Rectangle ClipBounds { get; }

    public int ColumnIndex { get; }

    public string? ErrorText { get; }

    public object? FormattedValue { get; }

    public Graphics? Graphics { get; }

    public DataGridViewPaintParts PaintParts { get; }

    public int RowIndex { get; }

    public DataGridViewElementStates State { get; }

    public object? Value { get; }

    /// <summary>Paints the parts selected by paintParts, clipped to clipBounds, exactly as the grid would have painted this cell on its own.</summary>
    public void Paint(Rectangle clipBounds, DataGridViewPaintParts paintParts) =>
        _cell?.PaintCore(Graphics!, clipBounds, CellBounds, RowIndex, State, Value, FormattedValue, CellStyle!, _advancedBorderStyle, paintParts);

    /// <summary>Paints only the background (and, when cellsPaintSelectionBackground, the selection highlight) and the border.</summary>
    public void PaintBackground(Rectangle clipBounds, bool cellsPaintSelectionBackground)
    {
        var parts = DataGridViewPaintParts.Background | DataGridViewPaintParts.Border;
        if (cellsPaintSelectionBackground) parts |= DataGridViewPaintParts.SelectionBackground;

        Paint(clipBounds, parts);
    }

    /// <summary>Paints only the formatted value and the error icon, leaving the background and border untouched.</summary>
    public void PaintContent(Rectangle clipBounds) => Paint(clipBounds, DataGridViewPaintParts.ContentForeground | DataGridViewPaintParts.ErrorIcon);
}
