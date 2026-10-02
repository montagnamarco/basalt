using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// AutoResizeColumn(s)/Row(s)/ColumnHeadersHeight/RowHeadersWidth: widens or heightens to fit the
/// widest/tallest content currently in the grid, measured with TextRenderer against each cell's
/// own inherited style and font - the same measuring DataGridView.Mouse.cs's column/row divider
/// double-click already calls through AutoFitColumnWidthCore/AutoFitRowHeightCore. The AutoSize*
/// mode parameters this round only choose whether headers are measured alongside cells; honouring
/// Fill, a fixed opposite dimension, or per-column/per-row modes is left for a future round.
/// </summary>
public partial class DataGridView
{
    private DataGridViewAutoSizeRowsMode _autoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
    private bool _allowUserToOrderColumns;

    [DefaultValue(false)]
    public bool AllowUserToOrderColumns
    {
        get => _allowUserToOrderColumns;
        set
        {
            if (value == _allowUserToOrderColumns) return;

            _allowUserToOrderColumns = value;
            OnAllowUserToOrderColumnsChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(DataGridViewAutoSizeRowsMode.None)]
    public DataGridViewAutoSizeRowsMode AutoSizeRowsMode
    {
        get => _autoSizeRowsMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(DataGridViewAutoSizeRowsMode));
            if (value == _autoSizeRowsMode) return;

            _autoSizeRowsMode = value;
            OnAutoSizeRowsModeChanged(new DataGridViewAutoSizeModeEventArgs(previousModeAutoSized: false));
        }
    }

    public event EventHandler? AllowUserToOrderColumnsChanged
    {
        add => Events.AddHandler(s_allowUserToOrderColumnsChangedEvent, value);
        remove => Events.RemoveHandler(s_allowUserToOrderColumnsChangedEvent, value);
    }

    public event DataGridViewAutoSizeColumnsModeEventHandler? AutoSizeColumnsModeChanged
    {
        add => Events.AddHandler(s_autoSizeColumnsModeChangedEvent, value);
        remove => Events.RemoveHandler(s_autoSizeColumnsModeChangedEvent, value);
    }

    public event DataGridViewAutoSizeModeEventHandler? AutoSizeRowsModeChanged
    {
        add => Events.AddHandler(s_autoSizeRowsModeChangedEvent, value);
        remove => Events.RemoveHandler(s_autoSizeRowsModeChangedEvent, value);
    }

    private static readonly object s_allowUserToOrderColumnsChangedEvent = new();
    private static readonly object s_autoSizeColumnsModeChangedEvent = new();
    private static readonly object s_autoSizeRowsModeChangedEvent = new();

    protected virtual void OnAllowUserToOrderColumnsChanged(EventArgs e) => ((EventHandler?)Events[s_allowUserToOrderColumnsChangedEvent])?.Invoke(this, e);

    protected virtual void OnAutoSizeColumnsModeChanged(DataGridViewAutoSizeColumnsModeEventArgs e) => ((DataGridViewAutoSizeColumnsModeEventHandler?)Events[s_autoSizeColumnsModeChangedEvent])?.Invoke(this, e);

    protected virtual void OnAutoSizeRowsModeChanged(DataGridViewAutoSizeModeEventArgs e) => ((DataGridViewAutoSizeModeEventHandler?)Events[s_autoSizeRowsModeChangedEvent])?.Invoke(this, e);

    public void AutoResizeColumn(int columnIndex) => AutoResizeColumn(columnIndex, DataGridViewAutoSizeColumnMode.AllCells, fixedHeight: false);

    public void AutoResizeColumn(int columnIndex, DataGridViewAutoSizeColumnMode autoSizeColumnMode) => AutoResizeColumn(columnIndex, autoSizeColumnMode, fixedHeight: false);

    protected void AutoResizeColumn(int columnIndex, DataGridViewAutoSizeColumnMode autoSizeColumnMode, bool fixedHeight) =>
        AutoFitColumnWidthCore(Columns[columnIndex], autoSizeColumnMode);

    public void AutoResizeColumns() => AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells, fixedHeight: false);

    public void AutoResizeColumns(DataGridViewAutoSizeColumnsMode autoSizeColumnsMode) => AutoResizeColumns(autoSizeColumnsMode, fixedHeight: false);

    protected void AutoResizeColumns(DataGridViewAutoSizeColumnsMode autoSizeColumnsMode, bool fixedHeight)
    {
        var columnMode = ColumnModeFrom(autoSizeColumnsMode);
        foreach (DataGridViewColumn column in Columns) AutoFitColumnWidthCore(column, columnMode);
    }

    public void AutoResizeColumnHeadersHeight() => AutoResizeColumnHeadersHeight(fixedRowHeadersWidth: false, fixedColumnsWidth: false);

    public void AutoResizeColumnHeadersHeight(int columnIndex) => AutoResizeColumnHeadersHeight(columnIndex, fixedRowHeadersWidth: false, fixedColumnWidth: false);

    protected void AutoResizeColumnHeadersHeight(bool fixedRowHeadersWidth, bool fixedColumnsWidth)
    {
        var tallest = ColumnHeadersHeight;
        foreach (DataGridViewColumn column in Columns) tallest = Math.Max(tallest, MeasureColumnHeaderHeight(column));

        ColumnHeadersHeight = tallest;
    }

    protected void AutoResizeColumnHeadersHeight(int columnIndex, bool fixedRowHeadersWidth, bool fixedColumnWidth) =>
        ColumnHeadersHeight = Math.Max(ColumnHeadersHeight, MeasureColumnHeaderHeight(Columns[columnIndex]));

    public void AutoResizeRow(int rowIndex) => AutoResizeRow(rowIndex, DataGridViewAutoSizeRowMode.AllCells, fixedWidth: false);

    public void AutoResizeRow(int rowIndex, DataGridViewAutoSizeRowMode autoSizeRowMode) => AutoResizeRow(rowIndex, autoSizeRowMode, fixedWidth: false);

    protected void AutoResizeRow(int rowIndex, DataGridViewAutoSizeRowMode autoSizeRowMode, bool fixedWidth) => AutoFitRowHeightCore(Rows[rowIndex]);

    public void AutoResizeRows() => AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCells, fixedWidth: false);

    public void AutoResizeRows(DataGridViewAutoSizeRowsMode autoSizeRowsMode) => AutoResizeRows(autoSizeRowsMode, fixedWidth: false);

    protected void AutoResizeRows(DataGridViewAutoSizeRowsMode autoSizeRowsMode, bool fixedWidth)
    {
        foreach (DataGridViewRow row in Rows) AutoFitRowHeightCore(row);
    }

    protected void AutoResizeRows(int rowIndexStart, int rowsCount, DataGridViewAutoSizeRowMode autoSizeRowMode, bool fixedWidth)
    {
        var end = Math.Min(rowIndexStart + rowsCount, Rows.Count);
        for (var index = Math.Max(rowIndexStart, 0); index < end; index++) AutoFitRowHeightCore(Rows[index]);
    }

    public void AutoResizeRowHeadersWidth(DataGridViewRowHeadersWidthSizeMode rowHeadersWidthSizeMode) =>
        AutoResizeRowHeadersWidth(rowHeadersWidthSizeMode, fixedColumnHeadersHeight: false, fixedRowsHeight: false);

    public void AutoResizeRowHeadersWidth(int rowIndex, DataGridViewRowHeadersWidthSizeMode rowHeadersWidthSizeMode) =>
        AutoResizeRowHeadersWidth(rowIndex, rowHeadersWidthSizeMode, fixedColumnHeadersHeight: false, fixedRowHeight: false);

    protected void AutoResizeRowHeadersWidth(DataGridViewRowHeadersWidthSizeMode rowHeadersWidthSizeMode, bool fixedColumnHeadersHeight, bool fixedRowsHeight)
    {
        var widest = RowHeadersWidth;
        foreach (DataGridViewRow row in Rows) widest = Math.Max(widest, MeasureRowHeaderWidth(row));

        RowHeadersWidth = widest;
    }

    protected void AutoResizeRowHeadersWidth(int rowIndex, DataGridViewRowHeadersWidthSizeMode rowHeadersWidthSizeMode, bool fixedColumnHeadersHeight, bool fixedRowHeight) =>
        RowHeadersWidth = Math.Max(RowHeadersWidth, MeasureRowHeaderWidth(Rows[rowIndex]));

    private static DataGridViewAutoSizeColumnMode ColumnModeFrom(DataGridViewAutoSizeColumnsMode mode) => mode switch
    {
        DataGridViewAutoSizeColumnsMode.ColumnHeader => DataGridViewAutoSizeColumnMode.ColumnHeader,
        DataGridViewAutoSizeColumnsMode.AllCellsExceptHeader => DataGridViewAutoSizeColumnMode.AllCellsExceptHeader,
        DataGridViewAutoSizeColumnsMode.DisplayedCellsExceptHeader => DataGridViewAutoSizeColumnMode.DisplayedCellsExceptHeader,
        DataGridViewAutoSizeColumnsMode.DisplayedCells => DataGridViewAutoSizeColumnMode.DisplayedCells,
        DataGridViewAutoSizeColumnsMode.Fill => DataGridViewAutoSizeColumnMode.Fill,
        _ => DataGridViewAutoSizeColumnMode.AllCells,
    };

    /// <summary>Widens column to its widest content: the header text (unless mode excludes it) and every cell's formatted value (unless mode excludes those), each measured with its own style's font.</summary>
    internal void AutoFitColumnWidthCore(DataGridViewColumn column, DataGridViewAutoSizeColumnMode mode)
    {
        var includeHeader = mode is not (DataGridViewAutoSizeColumnMode.AllCellsExceptHeader or DataGridViewAutoSizeColumnMode.DisplayedCellsExceptHeader);
        var includeCells = mode != DataGridViewAutoSizeColumnMode.ColumnHeader;

        var widest = column.MinimumWidth;

        if (includeHeader)
        {
            var headerStyle = column.HeaderCell.GetInheritedStyle(null, -1, includeColors: false);
            widest = Math.Max(widest, TextRenderer.MeasureText(column.HeaderText, headerStyle.Font ?? Font).Width + 12);
        }

        if (includeCells)
        {
            foreach (DataGridViewRow row in Rows)
            {
                var cell = row.Cells[column.Index];
                var text = cell.GetEditedFormattedValue(row.Index, DataGridViewDataErrorContexts.Display)?.ToString();
                if (string.IsNullOrEmpty(text)) continue;

                var cellStyle = cell.GetInheritedStyle(null, row.Index, includeColors: false);
                widest = Math.Max(widest, TextRenderer.MeasureText(text, cellStyle.Font ?? Font).Width + 12);
            }
        }

        column.Width = widest;
    }

    /// <summary>Heightens row to the tallest of every cell's formatted value in it, each measured against the column's own width.</summary>
    internal void AutoFitRowHeightCore(DataGridViewRow row)
    {
        var tallest = row.MinimumHeight;

        foreach (DataGridViewColumn column in Columns)
        {
            var cell = row.Cells[column.Index];
            var text = cell.GetEditedFormattedValue(row.Index, DataGridViewDataErrorContexts.Display)?.ToString();
            if (string.IsNullOrEmpty(text)) continue;

            var cellStyle = cell.GetInheritedStyle(null, row.Index, includeColors: false);
            var available = new Size(Math.Max(column.Width - 8, 1), int.MaxValue);
            tallest = Math.Max(tallest, TextRenderer.MeasureText(text, cellStyle.Font ?? Font, available, TextFormatFlags.WordBreak).Height + 8);
        }

        row.Height = tallest;
    }

    private int MeasureColumnHeaderHeight(DataGridViewColumn column)
    {
        var headerStyle = column.HeaderCell.GetInheritedStyle(null, -1, includeColors: false);
        var available = new Size(Math.Max(column.Width - 8, 1), int.MaxValue);

        return TextRenderer.MeasureText(column.HeaderText, headerStyle.Font ?? Font, available, TextFormatFlags.WordBreak).Height + 8;
    }

    /// <summary>Row headers carry no text of their own this round (just the selection glyph), so this only re-asserts RowHeadersWidth: a future round that adds row header text widens it here too.</summary>
    private int MeasureRowHeaderWidth(DataGridViewRow row) => RowHeadersWidth;
}
