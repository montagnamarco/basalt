using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>
/// Where each header and cell sits in client coordinates, and HitTest built on the same math.
/// Scrolling is left out this round: a grid taller or wider than its client area simply clips
/// the rows/columns that do not fit, rather than showing scroll bars.
/// </summary>
public partial class DataGridView
{
    private int OriginX => RowHeadersVisible ? RowHeadersWidth : 0;

    private int OriginY => ColumnHeadersVisible ? ColumnHeadersHeight : 0;

    /// <summary>The columns in the left-to-right order they are drawn, honouring DisplayIndex.</summary>
    private IEnumerable<DataGridViewColumn> ColumnsInDisplayOrder() => Columns.Cast<DataGridViewColumn>().OrderBy(column => column.DisplayIndex);

    private int ColumnLeft(DataGridViewColumn target)
    {
        var x = OriginX;
        foreach (var column in ColumnsInDisplayOrder())
        {
            if (column == target) return x;
            if (column.Visible) x += column.Width;
        }

        return x;
    }

    private int RowTop(int rowIndex)
    {
        var y = OriginY;
        for (var index = 0; index < rowIndex && index < Rows.Count; index++)
        {
            if (Rows[index].Visible) y += Rows[index].Height;
        }

        return y;
    }

    public Rectangle GetCellDisplayRectangle(int columnIndex, int rowIndex, bool cutOverflow)
    {
        Rectangle bounds;

        if (columnIndex < 0 && rowIndex < 0)
        {
            bounds = new Rectangle(0, 0, OriginX, OriginY);
        }
        else if (columnIndex < 0)
        {
            bounds = new Rectangle(0, RowTop(rowIndex), OriginX, Rows[rowIndex].Height);
        }
        else if (rowIndex < 0)
        {
            bounds = new Rectangle(ColumnLeft(Columns[columnIndex]), 0, Columns[columnIndex].Width, OriginY);
        }
        else
        {
            bounds = new Rectangle(ColumnLeft(Columns[columnIndex]), RowTop(rowIndex), Columns[columnIndex].Width, Rows[rowIndex].Height);
        }

        return cutOverflow ? Rectangle.Intersect(bounds, ClientRectangle) : bounds;
    }

    public Rectangle GetColumnDisplayRectangle(int columnIndex, bool cutOverflow) => GetCellDisplayRectangle(columnIndex, -1, cutOverflow);

    public Rectangle GetRowDisplayRectangle(int rowIndex, bool cutOverflow) => GetCellDisplayRectangle(-1, rowIndex, cutOverflow);

    /// <summary>
    /// How many rows fit top to bottom in the client area below the column headers, starting at
    /// row 0 (there is no scrolling this round, so row 0 is always the first displayed row):
    /// includePartialRow counts a row that only partly fits, otherwise only whole rows count.
    /// </summary>
    public int DisplayedRowCount(bool includePartialRow)
    {
        var available = ClientSize.Height - OriginY;
        var count = 0;
        var used = 0;

        for (var index = 0; index < Rows.Count; index++)
        {
            if (!Rows[index].Visible) continue;

            var height = Rows[index].Height;
            if (used + height > available)
            {
                if (includePartialRow && used < available) count++;
                break;
            }

            used += height;
            count++;
        }

        return count;
    }

    /// <summary>How many columns fit left to right in the client area to the right of the row headers, starting at column 0 (there is no scrolling this round).</summary>
    public int DisplayedColumnCount(bool includePartialColumn)
    {
        var available = ClientSize.Width - OriginX;
        var count = 0;
        var used = 0;

        foreach (var column in ColumnsInDisplayOrder())
        {
            if (!column.Visible) continue;

            if (used + column.Width > available)
            {
                if (includePartialColumn && used < available) count++;
                break;
            }

            used += column.Width;
            count++;
        }

        return count;
    }

    public void InvalidateCell(DataGridViewCell dataGridViewCell)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewCell);

        var rowIndex = dataGridViewCell.RowIndex;
        var columnIndex = dataGridViewCell.ColumnIndex;

        // A cell that is no longer part of the grid (e.g. the stale CurrentCell being left behind
        // while its row or column is being removed, still carrying its old, now out-of-range
        // index) cannot have its display rectangle computed; just repaint everything instead of
        // indexing past the end of Rows/Columns.
        if ((rowIndex < 0 && columnIndex < 0 && dataGridViewCell.OwningRow is null) ||
            rowIndex >= Rows.Count || columnIndex >= Columns.Count)
        {
            Invalidate();
            return;
        }

        Invalidate(GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: false));
    }

    public void InvalidateCell(int columnIndex, int rowIndex) => Invalidate(GetCellDisplayRectangle(columnIndex, rowIndex, cutOverflow: false));

    public void InvalidateColumn(int columnIndex) => Invalidate();

    public void InvalidateRow(int rowIndex) => Invalidate();

    /// <summary>Which column, row or header is under a client point, and the exact cell/column/row rectangle's origin there.</summary>
    public HitTestInfo HitTest(int x, int y)
    {
        if (x < 0 || y < 0 || x >= ClientSize.Width || y >= ClientSize.Height) return HitTestInfo.Nowhere;

        var inRowHeaders = RowHeadersVisible && x < OriginX;
        var inColumnHeaders = ColumnHeadersVisible && y < OriginY;

        if (inRowHeaders && inColumnHeaders) return new HitTestInfo(DataGridViewHitTestType.TopLeftHeader, -1, -1, 0, 0);

        var rowIndex = inColumnHeaders ? -1 : RowAt(y);
        var columnIndex = inRowHeaders ? -1 : ColumnAt(x);

        if (inColumnHeaders) return new HitTestInfo(columnIndex < 0 ? DataGridViewHitTestType.None : DataGridViewHitTestType.ColumnHeader, columnIndex, -1, columnIndex < 0 ? 0 : ColumnLeft(Columns[columnIndex]), 0);
        if (inRowHeaders) return new HitTestInfo(rowIndex < 0 ? DataGridViewHitTestType.None : DataGridViewHitTestType.RowHeader, -1, rowIndex, 0, rowIndex < 0 ? 0 : RowTop(rowIndex));

        if (columnIndex < 0 || rowIndex < 0) return HitTestInfo.Nowhere;

        return new HitTestInfo(DataGridViewHitTestType.Cell, columnIndex, rowIndex, ColumnLeft(Columns[columnIndex]), RowTop(rowIndex));
    }

    /// <summary>The visible column whose span contains x, in display order; -1 past the last one.</summary>
    private int ColumnAt(int x)
    {
        var left = OriginX;
        foreach (var column in ColumnsInDisplayOrder())
        {
            if (!column.Visible) continue;
            if (x >= left && x < left + column.Width) return column.Index;

            left += column.Width;
        }

        return -1;
    }

    /// <summary>The visible row whose span contains y; -1 past the last one.</summary>
    private int RowAt(int y)
    {
        var top = OriginY;
        for (var index = 0; index < Rows.Count; index++)
        {
            if (!Rows[index].Visible) continue;

            var height = Rows[index].Height;
            if (y >= top && y < top + height) return index;

            top += height;
        }

        return -1;
    }

    /// <summary>Reassigns every column's DisplayIndex so that moving one to a new position shifts the others, as Microsoft's setter does.</summary>
    internal void SetColumnDisplayIndex(DataGridViewColumn column, int value)
    {
        var ordered = ColumnsInDisplayOrder().Where(candidate => candidate != column).ToList();
        value = Math.Clamp(value, 0, ordered.Count);
        ordered.Insert(value, column);

        for (var index = 0; index < ordered.Count; index++) ordered[index].SetDisplayIndexCore(index);

        Invalidate();
    }

    /// <summary>The DataGridView.HitTest result: what was under the point, which column/row, and that column's left edge or row's top edge.</summary>
    public sealed class HitTestInfo
    {
        public static readonly HitTestInfo Nowhere = new(DataGridViewHitTestType.None, -1, -1, -1, -1);

        internal HitTestInfo(DataGridViewHitTestType type, int columnIndex, int rowIndex, int columnX, int rowY)
        {
            Type = type;
            ColumnIndex = columnIndex;
            RowIndex = rowIndex;
            ColumnX = columnX;
            RowY = rowY;
        }

        public int ColumnIndex { get; }

        public int ColumnX { get; }

        public int RowIndex { get; }

        public int RowY { get; }

        public DataGridViewHitTestType Type { get; }

        public override bool Equals(object? value) =>
            value is HitTestInfo other && other.Type == Type && other.ColumnIndex == ColumnIndex && other.RowIndex == RowIndex;

        public override int GetHashCode() => HashCode.Combine(Type, ColumnIndex, RowIndex);

        public override string ToString() => $"HitTestInfo {{ Type={Type}, ColumnIndex={ColumnIndex}, RowIndex={RowIndex} }}";
    }
}
