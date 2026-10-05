using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>
/// Where each header and cell sits in client coordinates, and HitTest built on the same math.
/// Scrolling and hit testing share the same viewport coordinates.
/// </summary>
public partial class DataGridView
{
    private int OriginX => RowHeadersVisible ? RowHeadersWidth : 0;

    private int OriginY => ColumnHeadersVisible ? ColumnHeadersHeight : 0;

    /// <summary>The columns in the left-to-right order they are drawn, honouring DisplayIndex.</summary>
    private IEnumerable<DataGridViewColumn> ColumnsInDisplayOrder() => Columns.Cast<DataGridViewColumn>().OrderBy(column => column.DisplayIndex);

    private int ColumnLeft(DataGridViewColumn target)
    {
        var x = OriginX - (target.Frozen ? 0 : _horizontalScrollingOffset);
        foreach (var column in ColumnsInDisplayOrder())
        {
            if (column == target) return x;
            if (column.Visible) x += column.Width;
        }

        return x;
    }

    private int RowTop(int rowIndex)
    {
        var y = OriginY - (Rows[rowIndex].Frozen ? 0 : _verticalScrollingOffset);
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

        var viewport = columnIndex < 0 && rowIndex < 0 ? new Rectangle(0, 0, OriginX, OriginY)
            : columnIndex < 0 ? new Rectangle(0, OriginY, OriginX, DisplayRectangle.Height)
            : rowIndex < 0 ? new Rectangle(OriginX, 0, DisplayRectangle.Width, OriginY)
            : DisplayRectangle;
        if (columnIndex >= 0 && !Columns[columnIndex].Frozen)
        {
            viewport.X += FrozenColumnsWidth;
            viewport.Width = Math.Max(0, viewport.Width - FrozenColumnsWidth);
        }
        if (rowIndex >= 0 && !Rows[rowIndex].Frozen)
        {
            viewport.Y += FrozenRowsHeight;
            viewport.Height = Math.Max(0, viewport.Height - FrozenRowsHeight);
        }
        return cutOverflow ? Rectangle.Intersect(bounds, viewport) : bounds;
    }

    public Rectangle GetColumnDisplayRectangle(int columnIndex, bool cutOverflow) => GetCellDisplayRectangle(columnIndex, -1, cutOverflow);

    public Rectangle GetRowDisplayRectangle(int rowIndex, bool cutOverflow) => GetCellDisplayRectangle(-1, rowIndex, cutOverflow);

    private int DisplayedRowHeight(int rowIndex)
    {
        var row = Rows[rowIndex];
        var top = RowTop(rowIndex);
        var viewportTop = OriginY + (row.Frozen ? 0 : FrozenRowsHeight);
        return Math.Max(0, Math.Min(top + row.Height, DisplayRectangle.Bottom) - Math.Max(top, viewportTop));
    }

    private int DisplayedColumnWidth(DataGridViewColumn column)
    {
        var left = ColumnLeft(column);
        var viewportLeft = OriginX + (column.Frozen ? 0 : FrozenColumnsWidth);
        return Math.Max(0, Math.Min(left + column.Width, DisplayRectangle.Right) - Math.Max(left, viewportLeft));
    }

    public int DisplayedRowCount(bool includePartialRow) => Rows.Cast<DataGridViewRow>().Count(row =>
        row.Visible && (includePartialRow ? DisplayedRowHeight(row.Index) > 0 : DisplayedRowHeight(row.Index) == row.Height));

    public int DisplayedColumnCount(bool includePartialColumn) => ColumnsInDisplayOrder().Count(column =>
        column.Visible && (includePartialColumn ? DisplayedColumnWidth(column) > 0 : DisplayedColumnWidth(column) == column.Width));

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
        foreach (var column in ColumnsInDisplayOrder())
        {
            if (!column.Visible) continue;
            var left = ColumnLeft(column);
            if (x >= left && x < left + column.Width) return column.Index;
        }

        return -1;
    }

    /// <summary>The visible row whose span contains y; -1 past the last one.</summary>
    private int RowAt(int y)
    {
        for (var index = 0; index < Rows.Count; index++)
        {
            if (!Rows[index].Visible) continue;

            var top = RowTop(index);
            var height = Rows[index].Height;
            if (y >= top && y < top + height) return index;

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
