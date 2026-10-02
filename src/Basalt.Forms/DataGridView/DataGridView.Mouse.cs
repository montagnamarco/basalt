namespace System.Windows.Forms;

/// <summary>
/// The mouse events that carry which cell, column header or row header they happened over:
/// CellMouseClick/DoubleClick/Enter/Leave/Move and their ColumnHeader/RowHeader equivalents, plus
/// the column/row divider double-click that auto-fits a column's width or a row's height. Raised
/// from the hit-testing already done in DataGridView.Input.cs's OnMouseDown/Up/Move/Leave.
/// </summary>
public partial class DataGridView
{
    public event DataGridViewCellMouseEventHandler? CellMouseClick
    {
        add => Events.AddHandler(s_cellMouseClickEvent, value);
        remove => Events.RemoveHandler(s_cellMouseClickEvent, value);
    }

    public event DataGridViewCellMouseEventHandler? CellMouseDoubleClick
    {
        add => Events.AddHandler(s_cellMouseDoubleClickEvent, value);
        remove => Events.RemoveHandler(s_cellMouseDoubleClickEvent, value);
    }

    public event DataGridViewCellEventHandler? CellMouseEnter
    {
        add => Events.AddHandler(s_cellMouseEnterEvent, value);
        remove => Events.RemoveHandler(s_cellMouseEnterEvent, value);
    }

    public event DataGridViewCellEventHandler? CellMouseLeave
    {
        add => Events.AddHandler(s_cellMouseLeaveEvent, value);
        remove => Events.RemoveHandler(s_cellMouseLeaveEvent, value);
    }

    public event DataGridViewCellMouseEventHandler? CellMouseMove
    {
        add => Events.AddHandler(s_cellMouseMoveEvent, value);
        remove => Events.RemoveHandler(s_cellMouseMoveEvent, value);
    }

    public event DataGridViewCellMouseEventHandler? ColumnHeaderMouseClick
    {
        add => Events.AddHandler(s_columnHeaderMouseClickEvent, value);
        remove => Events.RemoveHandler(s_columnHeaderMouseClickEvent, value);
    }

    public event DataGridViewCellMouseEventHandler? ColumnHeaderMouseDoubleClick
    {
        add => Events.AddHandler(s_columnHeaderMouseDoubleClickEvent, value);
        remove => Events.RemoveHandler(s_columnHeaderMouseDoubleClickEvent, value);
    }

    public event DataGridViewCellMouseEventHandler? RowHeaderMouseClick
    {
        add => Events.AddHandler(s_rowHeaderMouseClickEvent, value);
        remove => Events.RemoveHandler(s_rowHeaderMouseClickEvent, value);
    }

    public event DataGridViewCellMouseEventHandler? RowHeaderMouseDoubleClick
    {
        add => Events.AddHandler(s_rowHeaderMouseDoubleClickEvent, value);
        remove => Events.RemoveHandler(s_rowHeaderMouseDoubleClickEvent, value);
    }

    public event DataGridViewColumnDividerDoubleClickEventHandler? ColumnDividerDoubleClick
    {
        add => Events.AddHandler(s_columnDividerDoubleClickEvent, value);
        remove => Events.RemoveHandler(s_columnDividerDoubleClickEvent, value);
    }

    public event DataGridViewRowDividerDoubleClickEventHandler? RowDividerDoubleClick
    {
        add => Events.AddHandler(s_rowDividerDoubleClickEvent, value);
        remove => Events.RemoveHandler(s_rowDividerDoubleClickEvent, value);
    }

    private static readonly object s_cellMouseClickEvent = new();
    private static readonly object s_cellMouseDoubleClickEvent = new();
    private static readonly object s_cellMouseEnterEvent = new();
    private static readonly object s_cellMouseLeaveEvent = new();
    private static readonly object s_cellMouseMoveEvent = new();
    private static readonly object s_columnHeaderMouseClickEvent = new();
    private static readonly object s_columnHeaderMouseDoubleClickEvent = new();
    private static readonly object s_rowHeaderMouseClickEvent = new();
    private static readonly object s_rowHeaderMouseDoubleClickEvent = new();
    private static readonly object s_columnDividerDoubleClickEvent = new();
    private static readonly object s_rowDividerDoubleClickEvent = new();

    protected virtual void OnCellMouseClick(DataGridViewCellMouseEventArgs e) => ((DataGridViewCellMouseEventHandler?)Events[s_cellMouseClickEvent])?.Invoke(this, e);

    protected virtual void OnCellMouseDoubleClick(DataGridViewCellMouseEventArgs e) => ((DataGridViewCellMouseEventHandler?)Events[s_cellMouseDoubleClickEvent])?.Invoke(this, e);

    protected virtual void OnCellMouseEnter(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_cellMouseEnterEvent])?.Invoke(this, e);

    protected virtual void OnCellMouseLeave(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_cellMouseLeaveEvent])?.Invoke(this, e);

    protected virtual void OnCellMouseMove(DataGridViewCellMouseEventArgs e) => ((DataGridViewCellMouseEventHandler?)Events[s_cellMouseMoveEvent])?.Invoke(this, e);

    protected virtual void OnColumnHeaderMouseClick(DataGridViewCellMouseEventArgs e) => ((DataGridViewCellMouseEventHandler?)Events[s_columnHeaderMouseClickEvent])?.Invoke(this, e);

    protected virtual void OnColumnHeaderMouseDoubleClick(DataGridViewCellMouseEventArgs e) => ((DataGridViewCellMouseEventHandler?)Events[s_columnHeaderMouseDoubleClickEvent])?.Invoke(this, e);

    protected virtual void OnRowHeaderMouseClick(DataGridViewCellMouseEventArgs e) => ((DataGridViewCellMouseEventHandler?)Events[s_rowHeaderMouseClickEvent])?.Invoke(this, e);

    protected virtual void OnRowHeaderMouseDoubleClick(DataGridViewCellMouseEventArgs e) => ((DataGridViewCellMouseEventHandler?)Events[s_rowHeaderMouseDoubleClickEvent])?.Invoke(this, e);

    protected virtual void OnColumnDividerDoubleClick(DataGridViewColumnDividerDoubleClickEventArgs e) => ((DataGridViewColumnDividerDoubleClickEventHandler?)Events[s_columnDividerDoubleClickEvent])?.Invoke(this, e);

    protected virtual void OnRowDividerDoubleClick(DataGridViewRowDividerDoubleClickEventArgs e) => ((DataGridViewRowDividerDoubleClickEventHandler?)Events[s_rowDividerDoubleClickEvent])?.Invoke(this, e);

    /// <summary>
    /// Raises the Click/DoubleClick pair for whichever kind of cell a mouse-up just resolved to
    /// (data cell, column header or row header), called from DataGridView.Input.cs's OnMouseUp
    /// once it knows the release landed back on the very cell the press went down on.
    /// </summary>
    internal void RaiseMouseClickCore(HitTestInfo hit, DataGridViewCellMouseEventArgs args, bool isDoubleClick)
    {
        switch (hit.Type)
        {
            case DataGridViewHitTestType.Cell:
                OnCellMouseClick(args);
                if (isDoubleClick) OnCellMouseDoubleClick(args);
                break;

            case DataGridViewHitTestType.ColumnHeader:
                OnColumnHeaderMouseClick(args);
                if (isDoubleClick) OnColumnHeaderMouseDoubleClick(args);
                break;

            case DataGridViewHitTestType.RowHeader:
                OnRowHeaderMouseClick(args);
                if (isDoubleClick) OnRowHeaderMouseDoubleClick(args);
                break;
        }
    }

    internal void RaiseCellMouseEnterCore(int columnIndex, int rowIndex) => OnCellMouseEnter(new DataGridViewCellEventArgs(columnIndex, rowIndex));

    internal void RaiseCellMouseLeaveCore(int columnIndex, int rowIndex) => OnCellMouseLeave(new DataGridViewCellEventArgs(columnIndex, rowIndex));

    internal void RaiseCellMouseMoveCore(DataGridViewCellMouseEventArgs args) => OnCellMouseMove(args);

    /// <summary>
    /// The column whose right-edge divider sits within a few pixels of x in the column header
    /// row, or null away from every divider. Mirrors ColumnAt's own left-to-right walk so the two
    /// never disagree about where a column's bounds are.
    /// </summary>
    private DataGridViewColumn? ColumnDividerAt(int x)
    {
        const int tolerance = 4;
        var left = OriginX;

        foreach (var column in ColumnsInDisplayOrder())
        {
            if (!column.Visible) continue;

            var right = left + column.Width;
            if (Math.Abs(x - right) <= tolerance) return column;

            left = right;
        }

        return null;
    }

    /// <summary>The row whose bottom-edge divider sits within a few pixels of y in the row headers column, or null away from every divider.</summary>
    private DataGridViewRow? RowDividerAt(int y)
    {
        const int tolerance = 4;
        var top = OriginY;

        for (var index = 0; index < Rows.Count; index++)
        {
            if (!Rows[index].Visible) continue;

            var bottom = top + Rows[index].Height;
            if (Math.Abs(y - bottom) <= tolerance) return Rows[index];

            top = bottom;
        }

        return null;
    }

    /// <summary>
    /// A double click landing on a column header's right divider (rather than the header itself)
    /// auto-fits that column's width to its widest content, unless a handler sets e.Handled.
    /// </summary>
    private void HandleColumnDividerDoubleClick(int x, int y)
    {
        if (!ColumnHeadersVisible || y >= OriginY) return;
        if (ColumnDividerAt(x) is not { } column) return;

        var args = new DataGridViewColumnDividerDoubleClickEventArgs(column.Index, new HandledMouseEventArgs(MouseButtons.Left, 2, x, y, 0));
        OnColumnDividerDoubleClick(args);
        if (!args.Handled && AllowUserToResizeColumns) AutoFitColumnWidthCore(column, DataGridViewAutoSizeColumnMode.AllCells);
    }

    /// <summary>A double click landing on a row header's bottom divider auto-fits that row's height to its tallest cell content, unless a handler sets e.Handled.</summary>
    private void HandleRowDividerDoubleClick(int x, int y)
    {
        if (!RowHeadersVisible || x >= OriginX) return;
        if (RowDividerAt(y) is not { } row) return;

        var args = new DataGridViewRowDividerDoubleClickEventArgs(row.Index, new HandledMouseEventArgs(MouseButtons.Left, 2, x, y, 0));
        OnRowDividerDoubleClick(args);
        if (!args.Handled && AllowUserToResizeRows) AutoFitRowHeightCore(row);
    }
}
