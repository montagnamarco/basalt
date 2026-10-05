using System.Drawing;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>
/// OnPaint for the classic look: the background, the column/row headers as raised buttons with
/// a sort glyph, grid lines between cells, alternating row colour, the selection highlight and
/// the current cell's focus rectangle. Every cell (header or data) is painted through
/// InvokeCellPaint, which is also where CellPainting is raised; every row is wrapped in
/// RowPrePaint/RowPostPaint. Frozen columns and scroll bars are left out this round: every column
/// and row is drawn, clipped to the client area.
/// </summary>
public partial class DataGridView
{
    public event DataGridViewCellPaintingEventHandler? CellPainting
    {
        add => Events.AddHandler(s_cellPaintingEvent, value);
        remove => Events.RemoveHandler(s_cellPaintingEvent, value);
    }

    public event DataGridViewRowPrePaintEventHandler? RowPrePaint
    {
        add => Events.AddHandler(s_rowPrePaintEvent, value);
        remove => Events.RemoveHandler(s_rowPrePaintEvent, value);
    }

    public event DataGridViewRowPostPaintEventHandler? RowPostPaint
    {
        add => Events.AddHandler(s_rowPostPaintEvent, value);
        remove => Events.RemoveHandler(s_rowPostPaintEvent, value);
    }

    private static readonly object s_cellPaintingEvent = new();
    private static readonly object s_rowPrePaintEvent = new();
    private static readonly object s_rowPostPaintEvent = new();

    protected virtual void OnCellPainting(DataGridViewCellPaintingEventArgs e) => ((DataGridViewCellPaintingEventHandler?)Events[s_cellPaintingEvent])?.Invoke(this, e);

    protected virtual void OnRowPrePaint(DataGridViewRowPrePaintEventArgs e) => ((DataGridViewRowPrePaintEventHandler?)Events[s_rowPrePaintEvent])?.Invoke(this, e);

    protected virtual void OnRowPostPaint(DataGridViewRowPostPaintEventArgs e) => ((DataGridViewRowPostPaintEventHandler?)Events[s_rowPostPaintEvent])?.Invoke(this, e);

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;

        PaintBackground(graphics, e.ClipRectangle, ClientRectangle);

        if (RowHeadersVisible && ColumnHeadersVisible) PaintTopLeftCorner(graphics);
        if (ColumnHeadersVisible) PaintColumnHeaders(graphics);

        var saved = graphics.Save();
        try
        {
            graphics.IntersectClip(new Rectangle(0, OriginY, DisplayRectangle.Right, DisplayRectangle.Height));
            PaintRows(graphics, Rectangle.Intersect(e.ClipRectangle, new Rectangle(0, OriginY, DisplayRectangle.Right, DisplayRectangle.Height)));
        }
        finally { graphics.Restore(saved); }

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
        PaintHeaderCell(graphics, TopLeftHeaderCell, bounds, rowIndex: -1, DataGridViewPaintParts.All);
    }

    private void PaintColumnHeaders(Graphics graphics)
    {
        var displayed = ColumnsInDisplayOrder().Where(column => column.Visible && GetCellDisplayRectangle(column.Index, -1, true).Width > 0).ToArray();
        foreach (var column in displayed)
        {
            var bounds = GetCellDisplayRectangle(column.Index, -1, cutOverflow: false);
            if (bounds.Width <= 0) continue;

            var border = AdjustColumnHeaderBorderStyle(AdvancedColumnHeadersBorderStyle, new DataGridViewAdvancedBorderStyle(), column == displayed[0], column == displayed[^1]);
            PaintHeaderCell(graphics, column.HeaderCell, bounds, rowIndex: -1, DataGridViewPaintParts.All, border);
        }
    }

    private void PaintHeaderCell(Graphics graphics, DataGridViewHeaderCell headerCell, Rectangle bounds, int rowIndex, DataGridViewPaintParts paintParts, DataGridViewAdvancedBorderStyle? borderStyle = null)
    {
        var cellStyle = headerCell.GetInheritedStyle(null, rowIndex, includeColors: true);
        var formatted = HeaderCellText(headerCell);

        InvokeCellPaint(headerCell, graphics, bounds, rowIndex, DataGridViewElementStates.Visible, null, formatted, cellStyle, paintParts, borderStyle);
    }

    private static object? HeaderCellText(DataGridViewHeaderCell headerCell) => headerCell switch
    {
        DataGridViewColumnHeaderCell { OwningColumn: { } column } => column.HeaderText,
        _ => null,
    };

    private void PaintRows(Graphics graphics, Rectangle clip)
    {
        var firstVisible = FirstVisibleRowIndex();
        var lastVisible = LastVisibleRowIndex();

        for (var rowIndex = 0; rowIndex < Rows.Count; rowIndex++)
        {
            var row = Rows[rowIndex];
            if (!row.Visible) continue;

            var rowBounds = GetCellDisplayRectangle(-1, rowIndex, cutOverflow: false) with { X = 0, Width = ClientSize.Width };
            if (rowBounds.Y > clip.Bottom) break;
            if (rowBounds.Bottom < clip.Top) continue;

            PaintOneRow(graphics, rowIndex, rowBounds, clip, rowIndex == firstVisible, rowIndex == lastVisible);
        }
    }

    /// <summary>Wraps one row's header and cells in RowPrePaint/RowPostPaint: a handler that sets Handled on the pre-paint skips the grid's own painting of this row.</summary>
    private void PaintOneRow(Graphics graphics, int rowIndex, Rectangle rowBounds, Rectangle clip, bool isFirstDisplayedRow, bool isLastVisibleRow)
    {
        var row = Rows[rowIndex];
        var state = row.GetState(rowIndex);
        var errorText = row.ErrorText.Length == 0 ? null : row.ErrorText;
        var inheritedStyle = row.InheritedStyle;

        var viewportTop = OriginY + (row.Frozen ? 0 : FrozenRowsHeight);
        var viewport = new Rectangle(0, viewportTop, DisplayRectangle.Right, Math.Max(0, DisplayRectangle.Bottom - viewportTop));
        clip = Rectangle.Intersect(clip, Rectangle.Intersect(rowBounds, viewport));
        if (clip.Height <= 0 || clip.Width <= 0) return;
        var saved = graphics.Save();
        try
        {
            graphics.IntersectClip(clip);
            var preArgs = new DataGridViewRowPrePaintEventArgs(this, graphics, clip, rowBounds, rowIndex, state, errorText, inheritedStyle, isFirstDisplayedRow, isLastVisibleRow);
            OnRowPrePaint(preArgs);

            if (!preArgs.Handled)
            {
                row.PaintCore(graphics, clip, rowBounds, rowIndex, state, isFirstDisplayedRow, isLastVisibleRow);
            }

            var postArgs = new DataGridViewRowPostPaintEventArgs(this, graphics, clip, rowBounds, rowIndex, state, errorText, inheritedStyle, isFirstDisplayedRow, isLastVisibleRow);
            OnRowPostPaint(postArgs);
        }
        finally { graphics.Restore(saved); }
    }

    private int FirstVisibleRowIndex()
    {
        for (var index = 0; index < Rows.Count; index++)
        {
            if (Rows[index].Visible && DisplayedRowHeight(index) > 0) return index;
        }

        return -1;
    }

    private int LastVisibleRowIndex()
    {
        for (var index = Rows.Count - 1; index >= 0; index--)
        {
            if (Rows[index].Visible && DisplayedRowHeight(index) > 0) return index;
        }

        return -1;
    }

    /// <summary>Paints row's header cell, called both from the normal row flow and from RowPrePaint/RowPostPaint's own PaintHeader helper.</summary>
    internal void PaintRowHeaderCore(Graphics graphics, int rowIndex, Rectangle bounds, DataGridViewPaintParts paintParts)
    {
        var row = Rows[rowIndex];
        var borderStyle = GetRowHeaderBorderStyleCore(rowIndex);
        PaintHeaderCell(graphics, row.HeaderCell, bounds, rowIndex, paintParts, borderStyle);
    }

    internal DataGridViewAdvancedBorderStyle GetRowHeaderBorderStyleCore(int rowIndex) =>
        Rows[rowIndex].AdjustRowHeaderBorderStyle(
            AdvancedRowHeadersBorderStyle,
            new DataGridViewAdvancedBorderStyle(),
            singleVerticalBorderAdded: AdvancedCellBorderStyle.All == DataGridViewAdvancedCellBorderStyle.Single,
            singleHorizontalBorderAdded: AdvancedCellBorderStyle.All == DataGridViewAdvancedCellBorderStyle.Single,
            isFirstDisplayedRow: rowIndex == FirstVisibleRowIndex(),
            isLastVisibleRow: rowIndex == LastVisibleRowIndex());

    /// <summary>Paints every visible, clip-intersecting cell of one row, called both from the normal row flow and from RowPrePaint/RowPostPaint's own PaintCells helper.</summary>
    internal void PaintRowCellsCore(Graphics graphics, int rowIndex, Rectangle clipBounds, DataGridViewPaintParts paintParts)
    {
        var row = Rows[rowIndex];

        foreach (var column in ColumnsInDisplayOrder())
        {
            if (!column.Visible) continue;

            var cellBounds = GetCellDisplayRectangle(column.Index, rowIndex, cutOverflow: false);
            if (cellBounds.Width <= 0 || cellBounds.Height <= 0) continue;
            if (!cellBounds.IntersectsWith(clipBounds)) continue;

            PaintDataCell(graphics, row.Cells[column.Index], cellBounds, rowIndex, paintParts);
        }
    }

    internal void DrawRowFocusCore(Graphics graphics, Rectangle bounds) => ControlPaint.DrawFocusRectangle(graphics, bounds);

    private void PaintDataCell(Graphics graphics, DataGridViewCell cell, Rectangle bounds, int rowIndex, DataGridViewPaintParts paintParts)
    {
        var cellStyle = cell.GetInheritedStyle(null, rowIndex, includeColors: true);
        var selected = IsCellSelected(cell);
        var state = selected ? DataGridViewElementStates.Selected | DataGridViewElementStates.Visible : DataGridViewElementStates.Visible;

        var formatted = cell.GetEditedFormattedValue(rowIndex, DataGridViewDataErrorContexts.Display);

        InvokeCellPaint(cell, graphics, bounds, rowIndex, state, cell.Value, formatted, cellStyle, paintParts);

        if (ReferenceEquals(cell, _currentCell) && Focused)
        {
            ControlPaint.DrawFocusRectangle(graphics, bounds);
        }
    }

    /// <summary>
    /// Resolves the alternating-row background, raises CellPainting (a handler that sets Handled
    /// skips the cell's own default painting below), then calls the cell's own virtual Paint.
    /// </summary>
    private void InvokeCellPaint(DataGridViewCell cell, Graphics graphics, Rectangle bounds, int rowIndex, DataGridViewElementStates state, object? value, object? formattedValue, DataGridViewCellStyle cellStyle, DataGridViewPaintParts paintParts, DataGridViewAdvancedBorderStyle? borderStyle = null)
    {
        if (rowIndex >= 0 && rowIndex % 2 == 1 && HasAlternatingRowsDefaultCellStyle && !AlternatingRowsDefaultCellStyle.BackColor.IsEmpty && cellStyle.BackColor == DefaultCellStyle.BackColor)
        {
            cellStyle = cellStyle.Clone();
            cellStyle.BackColor = AlternatingRowsDefaultCellStyle.BackColor;
        }

        var errorText = GetCellErrorTextCore(cell, rowIndex);
        var effectiveBorderStyle = borderStyle ?? AdvancedCellBorderStyle;
        var clip = Rectangle.Intersect(bounds, GetCellDisplayRectangle(cell.ColumnIndex, rowIndex, true));
        if (clip.Width <= 0 || clip.Height <= 0) return;
        var saved = graphics.Save();
        try
        {
            graphics.IntersectClip(clip);
            var args = new DataGridViewCellPaintingEventArgs(this, cell, graphics, clip, bounds, rowIndex, cell.ColumnIndex, state, value, formattedValue, errorText, cellStyle, effectiveBorderStyle, paintParts);
            OnCellPainting(args);
            if (!args.Handled) cell.PaintCore(graphics, clip, bounds, rowIndex, state, value, formattedValue, cellStyle, effectiveBorderStyle, paintParts);
        }
        finally { graphics.Restore(saved); }
    }

    /// <summary>Whether cell is selected by any of the three ways a grid tracks selection: by itself, by its row, or by its column.</summary>
    internal bool IsCellSelected(DataGridViewCell cell) =>
        _selectedCells.Contains(cell) || _selectedRowIndexes.Contains(cell.RowIndex) || _selectedColumnIndexes.Contains(cell.ColumnIndex);
}
