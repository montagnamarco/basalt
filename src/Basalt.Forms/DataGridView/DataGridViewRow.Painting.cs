using System.Drawing;

namespace System.Windows.Forms;

public partial class DataGridViewRow
{
    protected virtual void DrawFocus(Graphics graphics, Rectangle clipBounds, Rectangle bounds, int rowIndex,
        DataGridViewElementStates rowState, DataGridViewCellStyle cellStyle, bool cellsPaintSelectionBackground)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        DataGridView?.DrawRowFocusCore(graphics, bounds);
    }

    /// <summary>Paints the row through overridable header and cell hooks.</summary>
    protected virtual void Paint(Graphics graphics, Rectangle clipBounds, Rectangle rowBounds, int rowIndex,
        DataGridViewElementStates rowState, bool isFirstDisplayedRow, bool isLastVisibleRow)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        PaintHeader(graphics, clipBounds, rowBounds, rowIndex, rowState, isFirstDisplayedRow,
            isLastVisibleRow, DataGridViewPaintParts.All);
        PaintCells(graphics, clipBounds, rowBounds, rowIndex, rowState, isFirstDisplayedRow,
            isLastVisibleRow, DataGridViewPaintParts.All);
    }

    protected virtual void PaintCells(Graphics graphics, Rectangle clipBounds, Rectangle rowBounds, int rowIndex,
        DataGridViewElementStates rowState, bool isFirstDisplayedRow, bool isLastVisibleRow,
        DataGridViewPaintParts paintParts)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        DataGridView?.PaintRowCellsCore(graphics, rowIndex, clipBounds, paintParts);
    }

    protected virtual void PaintHeader(Graphics graphics, Rectangle clipBounds, Rectangle rowBounds, int rowIndex,
        DataGridViewElementStates rowState, bool isFirstDisplayedRow, bool isLastVisibleRow,
        DataGridViewPaintParts paintParts)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (DataGridView is not { RowHeadersVisible: true } grid) return;
        var bounds = grid.GetCellDisplayRectangle(-1, rowIndex, cutOverflow: false);
        grid.PaintRowHeaderCore(graphics, rowIndex, bounds, paintParts);
    }

    internal void PaintCore(Graphics graphics, Rectangle clipBounds, Rectangle rowBounds, int rowIndex,
        DataGridViewElementStates rowState, bool isFirstDisplayedRow, bool isLastVisibleRow) =>
        Paint(graphics, clipBounds, rowBounds, rowIndex, rowState, isFirstDisplayedRow, isLastVisibleRow);
}
