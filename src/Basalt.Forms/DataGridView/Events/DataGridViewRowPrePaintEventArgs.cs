using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Raised right before DataGridView paints one row's header and cells: a handler that sets
/// Handled skips the grid's own painting of the row entirely, otherwise it can still call
/// PaintHeader/PaintCells (or their Background/Content halves) to draw the default look before
/// adding its own content on top - exactly the same helpers DataGridViewRowPostPaintEventArgs
/// offers for painting again afterwards.
/// </summary>
public class DataGridViewRowPrePaintEventArgs : HandledEventArgs
{
    private readonly System.Windows.Forms.DataGridView _dataGridView;
    private readonly int _rowIndex;

    internal DataGridViewRowPrePaintEventArgs(
        System.Windows.Forms.DataGridView dataGridView,
        Graphics graphics,
        Rectangle clipBounds,
        Rectangle rowBounds,
        int rowIndex,
        DataGridViewElementStates rowState,
        string? errorText,
        DataGridViewCellStyle inheritedRowStyle,
        bool isFirstDisplayedRow,
        bool isLastVisibleRow)
    {
        _dataGridView = dataGridView;
        _rowIndex = rowIndex;
        Graphics = graphics;
        ClipBounds = clipBounds;
        RowBounds = rowBounds;
        RowIndex = rowIndex;
        State = rowState;
        ErrorText = errorText;
        InheritedRowStyle = inheritedRowStyle;
        IsFirstDisplayedRow = isFirstDisplayedRow;
        IsLastVisibleRow = isLastVisibleRow;
        PaintParts = DataGridViewPaintParts.All;
    }

    public Rectangle ClipBounds { get; set; }

    public string? ErrorText { get; }

    public Graphics Graphics { get; }

    public DataGridViewCellStyle InheritedRowStyle { get; }

    public bool IsFirstDisplayedRow { get; }

    public bool IsLastVisibleRow { get; }

    public DataGridViewPaintParts PaintParts { get; set; }

    public Rectangle RowBounds { get; }

    public int RowIndex { get; }

    public DataGridViewElementStates State { get; }

    public void DrawFocus(Rectangle bounds, bool cellsPaintSelectionBackground) => _dataGridView.DrawRowFocusCore(Graphics, bounds);

    public void PaintCells(Rectangle clipBounds, DataGridViewPaintParts paintParts) => _dataGridView.PaintRowCellsCore(Graphics, _rowIndex, clipBounds, paintParts);

    public void PaintCellsBackground(Rectangle clipBounds, bool cellsPaintSelectionBackground)
    {
        var parts = DataGridViewPaintParts.Background | DataGridViewPaintParts.Border;
        if (cellsPaintSelectionBackground) parts |= DataGridViewPaintParts.SelectionBackground;

        PaintCells(clipBounds, parts);
    }

    public void PaintCellsContent(Rectangle clipBounds) => PaintCells(clipBounds, DataGridViewPaintParts.ContentForeground | DataGridViewPaintParts.ErrorIcon);

    public void PaintHeader(bool paintSelectionBackground) => PaintHeader(paintSelectionBackground ? DataGridViewPaintParts.All : DataGridViewPaintParts.All & ~DataGridViewPaintParts.SelectionBackground);

    public void PaintHeader(DataGridViewPaintParts paintParts) => _dataGridView.PaintRowHeaderCore(Graphics, _rowIndex, RowBounds, paintParts);
}
