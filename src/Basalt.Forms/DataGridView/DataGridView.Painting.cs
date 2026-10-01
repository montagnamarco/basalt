using System.Drawing;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>
/// OnPaint for the classic look: the background, the column/row headers as raised buttons with
/// a sort glyph, grid lines between cells, alternating row colour, the selection highlight and
/// the current cell's focus rectangle. Frozen columns and scroll bars are left out this round:
/// every column and row is drawn, clipped to the client area.
/// </summary>
public partial class DataGridView
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;

        PaintBackground(graphics, e.ClipRectangle, ClientRectangle);

        if (RowHeadersVisible && ColumnHeadersVisible) PaintTopLeftCorner(graphics);
        if (ColumnHeadersVisible) PaintColumnHeaders(graphics);

        PaintRows(graphics);

        base.OnPaint(e);
    }

    protected virtual void PaintBackground(Graphics graphics, Rectangle clipBounds, Rectangle gridBounds)
    {
        using var brush = new SolidBrush(BackgroundColor);
        graphics.FillRectangle(brush, gridBounds);
    }

    private void PaintTopLeftCorner(Graphics graphics)
    {
        var bounds = new Rectangle(0, 0, OriginX, OriginY);
        TopLeftHeaderCell.OwningColumn = null;
        PaintHeaderCell(graphics, TopLeftHeaderCell, bounds, rowIndex: -1);
    }

    private void PaintColumnHeaders(Graphics graphics)
    {
        foreach (var column in ColumnsInDisplayOrder())
        {
            if (!column.Visible) continue;

            var bounds = GetCellDisplayRectangle(column.Index, -1, cutOverflow: true);
            if (bounds.Width <= 0) continue;

            PaintHeaderCell(graphics, column.HeaderCell, bounds, rowIndex: -1);
        }
    }

    private void PaintHeaderCell(Graphics graphics, DataGridViewHeaderCell headerCell, Rectangle bounds, int rowIndex)
    {
        var cellStyle = headerCell.GetInheritedStyle(null, rowIndex, includeColors: true);
        var formatted = HeaderCellText(headerCell);

        InvokeCellPaint(headerCell, graphics, bounds, rowIndex, DataGridViewElementStates.Visible, null, formatted, cellStyle);
    }

    private static object? HeaderCellText(DataGridViewHeaderCell headerCell) => headerCell switch
    {
        DataGridViewColumnHeaderCell { OwningColumn: { } column } => column.HeaderText,
        _ => null,
    };

    private void PaintRows(Graphics graphics)
    {
        var clip = ClientRectangle;

        for (var rowIndex = 0; rowIndex < Rows.Count; rowIndex++)
        {
            var row = Rows[rowIndex];
            if (!row.Visible) continue;

            var rowBounds = GetCellDisplayRectangle(-1, rowIndex, cutOverflow: false) with { X = 0, Width = ClientSize.Width };
            if (rowBounds.Y > clip.Bottom) break;
            if (rowBounds.Bottom < clip.Top) continue;

            if (RowHeadersVisible)
            {
                var headerBounds = GetCellDisplayRectangle(-1, rowIndex, cutOverflow: true);
                PaintHeaderCell(graphics, row.HeaderCell, headerBounds, rowIndex);
            }

            foreach (var column in ColumnsInDisplayOrder())
            {
                if (!column.Visible) continue;

                var cellBounds = GetCellDisplayRectangle(column.Index, rowIndex, cutOverflow: true);
                if (cellBounds.Width <= 0 || cellBounds.Height <= 0) continue;

                PaintDataCell(graphics, row.Cells[column.Index], cellBounds, rowIndex);
            }
        }
    }

    private void PaintDataCell(Graphics graphics, DataGridViewCell cell, Rectangle bounds, int rowIndex)
    {
        var cellStyle = cell.GetInheritedStyle(null, rowIndex, includeColors: true);
        var selected = IsCellSelected(cell);
        var state = selected ? DataGridViewElementStates.Selected | DataGridViewElementStates.Visible : DataGridViewElementStates.Visible;

        var formatted = cell.GetEditedFormattedValue(rowIndex, DataGridViewDataErrorContexts.Display);

        InvokeCellPaint(cell, graphics, bounds, rowIndex, state, cell.Value, formatted, cellStyle);

        if (ReferenceEquals(cell, _currentCell) && Focused)
        {
            ControlPaint.DrawFocusRectangle(graphics, bounds);
        }
    }

    /// <summary>Resolves the alternating-row background before painting, then calls the cell's own virtual Paint.</summary>
    private void InvokeCellPaint(DataGridViewCell cell, Graphics graphics, Rectangle bounds, int rowIndex, DataGridViewElementStates state, object? value, object? formattedValue, DataGridViewCellStyle cellStyle)
    {
        if (rowIndex >= 0 && rowIndex % 2 == 1 && HasAlternatingRowsDefaultCellStyle && !AlternatingRowsDefaultCellStyle.BackColor.IsEmpty && cellStyle.BackColor == DefaultCellStyle.BackColor)
        {
            cellStyle = cellStyle.Clone();
            cellStyle.BackColor = AlternatingRowsDefaultCellStyle.BackColor;
        }

        cell.PaintCore(graphics, bounds, bounds, rowIndex, state, value, formattedValue, cellStyle, AdvancedCellBorderStyle, DataGridViewPaintParts.All);
    }

    /// <summary>Whether cell is selected by any of the three ways a grid tracks selection: by itself, by its row, or by its column.</summary>
    internal bool IsCellSelected(DataGridViewCell cell) =>
        _selectedCells.Contains(cell) || _selectedRowIndexes.Contains(cell.RowIndex) || _selectedColumnIndexes.Contains(cell.ColumnIndex);
}
