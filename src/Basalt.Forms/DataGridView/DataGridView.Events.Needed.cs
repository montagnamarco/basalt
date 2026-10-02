namespace System.Windows.Forms;

/// <summary>
/// The *Changed/*Needed events this round adds on top of DataGridView.Events.cs: the
/// ContextMenuStrip/ErrorText/ToolTipText/style/state notifications a cell, row, column or band
/// raises through the internal *Core hooks below, and the virtual-mode CellValueNeeded/
/// CellValuePushed pair DataGridViewCell.cs calls when VirtualMode is on. DefaultValuesNeeded and
/// NewRowNeeded are declared for API completeness but never raised: both exist only for the
/// AllowUserToAddRows placeholder row, which this grid does not create (see DataGridView.cs).
/// Likewise UserAddedRow/UserDeletedRow/UserDeletingRow stay unraised: nothing in this round adds
/// or removes a row from end-user input (Delete key, the placeholder row) for them to describe.
/// </summary>
public partial class DataGridView
{
    public event DataGridViewAutoSizeColumnModeEventHandler? AutoSizeColumnModeChanged
    {
        add => Events.AddHandler(s_autoSizeColumnModeChangedEvent, value);
        remove => Events.RemoveHandler(s_autoSizeColumnModeChangedEvent, value);
    }

    public event DataGridViewCellEventHandler? CellContextMenuStripChanged
    {
        add => Events.AddHandler(s_cellContextMenuStripChangedEvent, value);
        remove => Events.RemoveHandler(s_cellContextMenuStripChangedEvent, value);
    }

    public event DataGridViewCellContextMenuStripNeededEventHandler? CellContextMenuStripNeeded
    {
        add => Events.AddHandler(s_cellContextMenuStripNeededEvent, value);
        remove => Events.RemoveHandler(s_cellContextMenuStripNeededEvent, value);
    }

    public event DataGridViewCellEventHandler? CellErrorTextChanged
    {
        add => Events.AddHandler(s_cellErrorTextChangedEvent, value);
        remove => Events.RemoveHandler(s_cellErrorTextChangedEvent, value);
    }

    public event DataGridViewCellStateChangedEventHandler? CellStateChanged
    {
        add => Events.AddHandler(s_cellStateChangedEvent, value);
        remove => Events.RemoveHandler(s_cellStateChangedEvent, value);
    }

    public event DataGridViewCellEventHandler? CellStyleChanged
    {
        add => Events.AddHandler(s_cellStyleChangedEvent, value);
        remove => Events.RemoveHandler(s_cellStyleChangedEvent, value);
    }

    public event DataGridViewCellStyleContentChangedEventHandler? CellStyleContentChanged
    {
        add => Events.AddHandler(s_cellStyleContentChangedEvent, value);
        remove => Events.RemoveHandler(s_cellStyleContentChangedEvent, value);
    }

    public event DataGridViewCellEventHandler? CellToolTipTextChanged
    {
        add => Events.AddHandler(s_cellToolTipTextChangedEvent, value);
        remove => Events.RemoveHandler(s_cellToolTipTextChangedEvent, value);
    }

    public event DataGridViewCellToolTipTextNeededEventHandler? CellToolTipTextNeeded
    {
        add => Events.AddHandler(s_cellToolTipTextNeededEvent, value);
        remove => Events.RemoveHandler(s_cellToolTipTextNeededEvent, value);
    }

    public event DataGridViewCellValueEventHandler? CellValueNeeded
    {
        add => Events.AddHandler(s_cellValueNeededEvent, value);
        remove => Events.RemoveHandler(s_cellValueNeededEvent, value);
    }

    public event DataGridViewCellValueEventHandler? CellValuePushed
    {
        add => Events.AddHandler(s_cellValuePushedEvent, value);
        remove => Events.RemoveHandler(s_cellValuePushedEvent, value);
    }

    public event DataGridViewColumnEventHandler? ColumnContextMenuStripChanged
    {
        add => Events.AddHandler(s_columnContextMenuStripChangedEvent, value);
        remove => Events.RemoveHandler(s_columnContextMenuStripChangedEvent, value);
    }

    public event DataGridViewColumnEventHandler? ColumnDataPropertyNameChanged
    {
        add => Events.AddHandler(s_columnDataPropertyNameChangedEvent, value);
        remove => Events.RemoveHandler(s_columnDataPropertyNameChangedEvent, value);
    }

    public event DataGridViewColumnEventHandler? ColumnDefaultCellStyleChanged
    {
        add => Events.AddHandler(s_columnDefaultCellStyleChangedEvent, value);
        remove => Events.RemoveHandler(s_columnDefaultCellStyleChangedEvent, value);
    }

    public event DataGridViewColumnEventHandler? ColumnDisplayIndexChanged
    {
        add => Events.AddHandler(s_columnDisplayIndexChangedEvent, value);
        remove => Events.RemoveHandler(s_columnDisplayIndexChangedEvent, value);
    }

    public event DataGridViewColumnEventHandler? ColumnDividerWidthChanged
    {
        add => Events.AddHandler(s_columnDividerWidthChangedEvent, value);
        remove => Events.RemoveHandler(s_columnDividerWidthChangedEvent, value);
    }

    public event DataGridViewColumnEventHandler? ColumnHeaderCellChanged
    {
        add => Events.AddHandler(s_columnHeaderCellChangedEvent, value);
        remove => Events.RemoveHandler(s_columnHeaderCellChangedEvent, value);
    }

    public event EventHandler? ColumnHeadersBorderStyleChanged
    {
        add => Events.AddHandler(s_columnHeadersBorderStyleChangedEvent, value);
        remove => Events.RemoveHandler(s_columnHeadersBorderStyleChangedEvent, value);
    }

    public event DataGridViewAutoSizeModeEventHandler? ColumnHeadersHeightSizeModeChanged
    {
        add => Events.AddHandler(s_columnHeadersHeightSizeModeChangedEvent, value);
        remove => Events.RemoveHandler(s_columnHeadersHeightSizeModeChangedEvent, value);
    }

    public event DataGridViewColumnEventHandler? ColumnMinimumWidthChanged
    {
        add => Events.AddHandler(s_columnMinimumWidthChangedEvent, value);
        remove => Events.RemoveHandler(s_columnMinimumWidthChangedEvent, value);
    }

    public event DataGridViewColumnEventHandler? ColumnNameChanged
    {
        add => Events.AddHandler(s_columnNameChangedEvent, value);
        remove => Events.RemoveHandler(s_columnNameChangedEvent, value);
    }

    public event DataGridViewColumnEventHandler? ColumnSortModeChanged
    {
        add => Events.AddHandler(s_columnSortModeChangedEvent, value);
        remove => Events.RemoveHandler(s_columnSortModeChangedEvent, value);
    }

    public event DataGridViewColumnStateChangedEventHandler? ColumnStateChanged
    {
        add => Events.AddHandler(s_columnStateChangedEvent, value);
        remove => Events.RemoveHandler(s_columnStateChangedEvent, value);
    }

    public event DataGridViewColumnEventHandler? ColumnToolTipTextChanged
    {
        add => Events.AddHandler(s_columnToolTipTextChangedEvent, value);
        remove => Events.RemoveHandler(s_columnToolTipTextChangedEvent, value);
    }

    public event DataGridViewRowEventHandler? DefaultValuesNeeded
    {
        add => Events.AddHandler(s_defaultValuesNeededEvent, value);
        remove => Events.RemoveHandler(s_defaultValuesNeededEvent, value);
    }

    public event DataGridViewRowEventHandler? NewRowNeeded
    {
        add => Events.AddHandler(s_newRowNeededEvent, value);
        remove => Events.RemoveHandler(s_newRowNeededEvent, value);
    }

    public event DataGridViewRowEventHandler? RowContextMenuStripChanged
    {
        add => Events.AddHandler(s_rowContextMenuStripChangedEvent, value);
        remove => Events.RemoveHandler(s_rowContextMenuStripChangedEvent, value);
    }

    public event DataGridViewRowContextMenuStripNeededEventHandler? RowContextMenuStripNeeded
    {
        add => Events.AddHandler(s_rowContextMenuStripNeededEvent, value);
        remove => Events.RemoveHandler(s_rowContextMenuStripNeededEvent, value);
    }

    public event DataGridViewRowEventHandler? RowDefaultCellStyleChanged
    {
        add => Events.AddHandler(s_rowDefaultCellStyleChangedEvent, value);
        remove => Events.RemoveHandler(s_rowDefaultCellStyleChangedEvent, value);
    }

    public event DataGridViewRowEventHandler? RowDividerHeightChanged
    {
        add => Events.AddHandler(s_rowDividerHeightChangedEvent, value);
        remove => Events.RemoveHandler(s_rowDividerHeightChangedEvent, value);
    }

    public event DataGridViewCellEventHandler? RowEnter
    {
        add => Events.AddHandler(s_rowEnterEvent, value);
        remove => Events.RemoveHandler(s_rowEnterEvent, value);
    }

    public event DataGridViewRowEventHandler? RowErrorTextChanged
    {
        add => Events.AddHandler(s_rowErrorTextChangedEvent, value);
        remove => Events.RemoveHandler(s_rowErrorTextChangedEvent, value);
    }

    public event DataGridViewRowErrorTextNeededEventHandler? RowErrorTextNeeded
    {
        add => Events.AddHandler(s_rowErrorTextNeededEvent, value);
        remove => Events.RemoveHandler(s_rowErrorTextNeededEvent, value);
    }

    public event DataGridViewRowEventHandler? RowHeaderCellChanged
    {
        add => Events.AddHandler(s_rowHeaderCellChangedEvent, value);
        remove => Events.RemoveHandler(s_rowHeaderCellChangedEvent, value);
    }

    public event EventHandler? RowHeadersBorderStyleChanged
    {
        add => Events.AddHandler(s_rowHeadersBorderStyleChangedEvent, value);
        remove => Events.RemoveHandler(s_rowHeadersBorderStyleChangedEvent, value);
    }

    public event DataGridViewAutoSizeModeEventHandler? RowHeadersWidthSizeModeChanged
    {
        add => Events.AddHandler(s_rowHeadersWidthSizeModeChangedEvent, value);
        remove => Events.RemoveHandler(s_rowHeadersWidthSizeModeChangedEvent, value);
    }

    public event DataGridViewRowHeightInfoNeededEventHandler? RowHeightInfoNeeded
    {
        add => Events.AddHandler(s_rowHeightInfoNeededEvent, value);
        remove => Events.RemoveHandler(s_rowHeightInfoNeededEvent, value);
    }

    public event DataGridViewRowHeightInfoPushedEventHandler? RowHeightInfoPushed
    {
        add => Events.AddHandler(s_rowHeightInfoPushedEvent, value);
        remove => Events.RemoveHandler(s_rowHeightInfoPushedEvent, value);
    }

    public event DataGridViewCellEventHandler? RowLeave
    {
        add => Events.AddHandler(s_rowLeaveEvent, value);
        remove => Events.RemoveHandler(s_rowLeaveEvent, value);
    }

    public event DataGridViewRowEventHandler? RowMinimumHeightChanged
    {
        add => Events.AddHandler(s_rowMinimumHeightChangedEvent, value);
        remove => Events.RemoveHandler(s_rowMinimumHeightChangedEvent, value);
    }

    public event DataGridViewRowStateChangedEventHandler? RowStateChanged
    {
        add => Events.AddHandler(s_rowStateChangedEvent, value);
        remove => Events.RemoveHandler(s_rowStateChangedEvent, value);
    }

    public event DataGridViewRowEventHandler? RowUnshared
    {
        add => Events.AddHandler(s_rowUnsharedEvent, value);
        remove => Events.RemoveHandler(s_rowUnsharedEvent, value);
    }

    public event DataGridViewCellEventHandler? RowValidated
    {
        add => Events.AddHandler(s_rowValidatedEvent, value);
        remove => Events.RemoveHandler(s_rowValidatedEvent, value);
    }

    public event DataGridViewRowEventHandler? UserAddedRow
    {
        add => Events.AddHandler(s_userAddedRowEvent, value);
        remove => Events.RemoveHandler(s_userAddedRowEvent, value);
    }

    public event DataGridViewRowEventHandler? UserDeletedRow
    {
        add => Events.AddHandler(s_userDeletedRowEvent, value);
        remove => Events.RemoveHandler(s_userDeletedRowEvent, value);
    }

    public event DataGridViewRowCancelEventHandler? UserDeletingRow
    {
        add => Events.AddHandler(s_userDeletingRowEvent, value);
        remove => Events.RemoveHandler(s_userDeletingRowEvent, value);
    }

    private static readonly object s_autoSizeColumnModeChangedEvent = new();
    private static readonly object s_cellContextMenuStripChangedEvent = new();
    private static readonly object s_cellContextMenuStripNeededEvent = new();
    private static readonly object s_cellErrorTextChangedEvent = new();
    private static readonly object s_cellStateChangedEvent = new();
    private static readonly object s_cellStyleChangedEvent = new();
    private static readonly object s_cellStyleContentChangedEvent = new();
    private static readonly object s_cellToolTipTextChangedEvent = new();
    private static readonly object s_cellToolTipTextNeededEvent = new();
    private static readonly object s_cellValueNeededEvent = new();
    private static readonly object s_cellValuePushedEvent = new();
    private static readonly object s_columnContextMenuStripChangedEvent = new();
    private static readonly object s_columnDataPropertyNameChangedEvent = new();
    private static readonly object s_columnDefaultCellStyleChangedEvent = new();
    private static readonly object s_columnDisplayIndexChangedEvent = new();
    private static readonly object s_columnDividerWidthChangedEvent = new();
    private static readonly object s_columnHeaderCellChangedEvent = new();
    private static readonly object s_columnHeadersBorderStyleChangedEvent = new();
    private static readonly object s_columnHeadersHeightSizeModeChangedEvent = new();
    private static readonly object s_columnMinimumWidthChangedEvent = new();
    private static readonly object s_columnNameChangedEvent = new();
    private static readonly object s_columnSortModeChangedEvent = new();
    private static readonly object s_columnStateChangedEvent = new();
    private static readonly object s_columnToolTipTextChangedEvent = new();
    private static readonly object s_defaultValuesNeededEvent = new();
    private static readonly object s_newRowNeededEvent = new();
    private static readonly object s_rowContextMenuStripChangedEvent = new();
    private static readonly object s_rowContextMenuStripNeededEvent = new();
    private static readonly object s_rowDefaultCellStyleChangedEvent = new();
    private static readonly object s_rowDividerHeightChangedEvent = new();
    private static readonly object s_rowEnterEvent = new();
    private static readonly object s_rowErrorTextChangedEvent = new();
    private static readonly object s_rowErrorTextNeededEvent = new();
    private static readonly object s_rowHeaderCellChangedEvent = new();
    private static readonly object s_rowHeadersBorderStyleChangedEvent = new();
    private static readonly object s_rowHeadersWidthSizeModeChangedEvent = new();
    private static readonly object s_rowHeightInfoNeededEvent = new();
    private static readonly object s_rowHeightInfoPushedEvent = new();
    private static readonly object s_rowLeaveEvent = new();
    private static readonly object s_rowMinimumHeightChangedEvent = new();
    private static readonly object s_rowStateChangedEvent = new();
    private static readonly object s_rowUnsharedEvent = new();
    private static readonly object s_rowValidatedEvent = new();
    private static readonly object s_userAddedRowEvent = new();
    private static readonly object s_userDeletedRowEvent = new();
    private static readonly object s_userDeletingRowEvent = new();

    protected virtual void OnAutoSizeColumnModeChanged(DataGridViewAutoSizeColumnModeEventArgs e) => ((DataGridViewAutoSizeColumnModeEventHandler?)Events[s_autoSizeColumnModeChangedEvent])?.Invoke(this, e);

    protected virtual void OnCellContextMenuStripChanged(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_cellContextMenuStripChangedEvent])?.Invoke(this, e);

    protected virtual void OnCellContextMenuStripNeeded(DataGridViewCellContextMenuStripNeededEventArgs e) => ((DataGridViewCellContextMenuStripNeededEventHandler?)Events[s_cellContextMenuStripNeededEvent])?.Invoke(this, e);

    protected virtual void OnCellErrorTextChanged(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_cellErrorTextChangedEvent])?.Invoke(this, e);

    protected virtual void OnCellStateChanged(DataGridViewCellStateChangedEventArgs e) => ((DataGridViewCellStateChangedEventHandler?)Events[s_cellStateChangedEvent])?.Invoke(this, e);

    protected virtual void OnCellStyleChanged(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_cellStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnCellStyleContentChanged(DataGridViewCellStyleContentChangedEventArgs e) => ((DataGridViewCellStyleContentChangedEventHandler?)Events[s_cellStyleContentChangedEvent])?.Invoke(this, e);

    protected virtual void OnCellToolTipTextChanged(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_cellToolTipTextChangedEvent])?.Invoke(this, e);

    protected virtual void OnCellToolTipTextNeeded(DataGridViewCellToolTipTextNeededEventArgs e) => ((DataGridViewCellToolTipTextNeededEventHandler?)Events[s_cellToolTipTextNeededEvent])?.Invoke(this, e);

    protected virtual void OnCellValueNeeded(DataGridViewCellValueEventArgs e) => ((DataGridViewCellValueEventHandler?)Events[s_cellValueNeededEvent])?.Invoke(this, e);

    protected virtual void OnCellValuePushed(DataGridViewCellValueEventArgs e) => ((DataGridViewCellValueEventHandler?)Events[s_cellValuePushedEvent])?.Invoke(this, e);

    protected virtual void OnColumnContextMenuStripChanged(DataGridViewColumnEventArgs e) => ((DataGridViewColumnEventHandler?)Events[s_columnContextMenuStripChangedEvent])?.Invoke(this, e);

    protected virtual void OnColumnDataPropertyNameChanged(DataGridViewColumnEventArgs e) => ((DataGridViewColumnEventHandler?)Events[s_columnDataPropertyNameChangedEvent])?.Invoke(this, e);

    protected virtual void OnColumnDefaultCellStyleChanged(DataGridViewColumnEventArgs e) => ((DataGridViewColumnEventHandler?)Events[s_columnDefaultCellStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnColumnDisplayIndexChanged(DataGridViewColumnEventArgs e) => ((DataGridViewColumnEventHandler?)Events[s_columnDisplayIndexChangedEvent])?.Invoke(this, e);

    protected virtual void OnColumnDividerWidthChanged(DataGridViewColumnEventArgs e) => ((DataGridViewColumnEventHandler?)Events[s_columnDividerWidthChangedEvent])?.Invoke(this, e);

    protected virtual void OnColumnHeaderCellChanged(DataGridViewColumnEventArgs e) => ((DataGridViewColumnEventHandler?)Events[s_columnHeaderCellChangedEvent])?.Invoke(this, e);

    protected virtual void OnColumnHeadersBorderStyleChanged(EventArgs e) => ((EventHandler?)Events[s_columnHeadersBorderStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnColumnHeadersHeightSizeModeChanged(DataGridViewAutoSizeModeEventArgs e) => ((DataGridViewAutoSizeModeEventHandler?)Events[s_columnHeadersHeightSizeModeChangedEvent])?.Invoke(this, e);

    protected virtual void OnColumnMinimumWidthChanged(DataGridViewColumnEventArgs e) => ((DataGridViewColumnEventHandler?)Events[s_columnMinimumWidthChangedEvent])?.Invoke(this, e);

    protected virtual void OnColumnNameChanged(DataGridViewColumnEventArgs e) => ((DataGridViewColumnEventHandler?)Events[s_columnNameChangedEvent])?.Invoke(this, e);

    protected virtual void OnColumnSortModeChanged(DataGridViewColumnEventArgs e) => ((DataGridViewColumnEventHandler?)Events[s_columnSortModeChangedEvent])?.Invoke(this, e);

    protected virtual void OnColumnStateChanged(DataGridViewColumnStateChangedEventArgs e) => ((DataGridViewColumnStateChangedEventHandler?)Events[s_columnStateChangedEvent])?.Invoke(this, e);

    protected virtual void OnColumnToolTipTextChanged(DataGridViewColumnEventArgs e) => ((DataGridViewColumnEventHandler?)Events[s_columnToolTipTextChangedEvent])?.Invoke(this, e);

    protected virtual void OnDefaultValuesNeeded(DataGridViewRowEventArgs e) => ((DataGridViewRowEventHandler?)Events[s_defaultValuesNeededEvent])?.Invoke(this, e);

    protected virtual void OnNewRowNeeded(DataGridViewRowEventArgs e) => ((DataGridViewRowEventHandler?)Events[s_newRowNeededEvent])?.Invoke(this, e);

    protected virtual void OnRowContextMenuStripChanged(DataGridViewRowEventArgs e) => ((DataGridViewRowEventHandler?)Events[s_rowContextMenuStripChangedEvent])?.Invoke(this, e);

    protected virtual void OnRowContextMenuStripNeeded(DataGridViewRowContextMenuStripNeededEventArgs e) => ((DataGridViewRowContextMenuStripNeededEventHandler?)Events[s_rowContextMenuStripNeededEvent])?.Invoke(this, e);

    protected virtual void OnRowDefaultCellStyleChanged(DataGridViewRowEventArgs e) => ((DataGridViewRowEventHandler?)Events[s_rowDefaultCellStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnRowDividerHeightChanged(DataGridViewRowEventArgs e) => ((DataGridViewRowEventHandler?)Events[s_rowDividerHeightChangedEvent])?.Invoke(this, e);

    protected virtual void OnRowEnter(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_rowEnterEvent])?.Invoke(this, e);

    protected virtual void OnRowErrorTextChanged(DataGridViewRowEventArgs e) => ((DataGridViewRowEventHandler?)Events[s_rowErrorTextChangedEvent])?.Invoke(this, e);

    protected virtual void OnRowErrorTextNeeded(DataGridViewRowErrorTextNeededEventArgs e) => ((DataGridViewRowErrorTextNeededEventHandler?)Events[s_rowErrorTextNeededEvent])?.Invoke(this, e);

    protected virtual void OnRowHeaderCellChanged(DataGridViewRowEventArgs e) => ((DataGridViewRowEventHandler?)Events[s_rowHeaderCellChangedEvent])?.Invoke(this, e);

    protected virtual void OnRowHeadersBorderStyleChanged(EventArgs e) => ((EventHandler?)Events[s_rowHeadersBorderStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnRowHeadersWidthSizeModeChanged(DataGridViewAutoSizeModeEventArgs e) => ((DataGridViewAutoSizeModeEventHandler?)Events[s_rowHeadersWidthSizeModeChangedEvent])?.Invoke(this, e);

    protected virtual void OnRowHeightInfoNeeded(DataGridViewRowHeightInfoNeededEventArgs e) => ((DataGridViewRowHeightInfoNeededEventHandler?)Events[s_rowHeightInfoNeededEvent])?.Invoke(this, e);

    protected virtual void OnRowHeightInfoPushed(DataGridViewRowHeightInfoPushedEventArgs e) => ((DataGridViewRowHeightInfoPushedEventHandler?)Events[s_rowHeightInfoPushedEvent])?.Invoke(this, e);

    protected virtual void OnRowLeave(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_rowLeaveEvent])?.Invoke(this, e);

    protected virtual void OnRowMinimumHeightChanged(DataGridViewRowEventArgs e) => ((DataGridViewRowEventHandler?)Events[s_rowMinimumHeightChangedEvent])?.Invoke(this, e);

    /// <summary>rowIndex matches e.Row.Index in this grid (rows are never shared), kept as its own parameter only because Microsoft declares it that way.</summary>
    protected virtual void OnRowStateChanged(int rowIndex, DataGridViewRowStateChangedEventArgs e) => ((DataGridViewRowStateChangedEventHandler?)Events[s_rowStateChangedEvent])?.Invoke(this, e);

    protected virtual void OnRowUnshared(DataGridViewRowEventArgs e) => ((DataGridViewRowEventHandler?)Events[s_rowUnsharedEvent])?.Invoke(this, e);

    protected virtual void OnRowValidated(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_rowValidatedEvent])?.Invoke(this, e);

    protected virtual void OnUserAddedRow(DataGridViewRowEventArgs e) => ((DataGridViewRowEventHandler?)Events[s_userAddedRowEvent])?.Invoke(this, e);

    protected virtual void OnUserDeletedRow(DataGridViewRowEventArgs e) => ((DataGridViewRowEventHandler?)Events[s_userDeletedRowEvent])?.Invoke(this, e);

    protected virtual void OnUserDeletingRow(DataGridViewRowCancelEventArgs e) => ((DataGridViewRowCancelEventHandler?)Events[s_userDeletingRowEvent])?.Invoke(this, e);

    /// <summary>A cell's ContextMenuStrip was read while it had none of its own: lets a handler supply one dynamically, exactly as CellErrorTextNeeded supplies error text.</summary>
    internal ContextMenuStrip? GetCellContextMenuStripCore(DataGridViewCell cell, int rowIndex)
    {
        var args = new DataGridViewCellContextMenuStripNeededEventArgs(cell.ColumnIndex, rowIndex);
        OnCellContextMenuStripNeeded(args);

        return args.ContextMenuStrip;
    }

    internal void OnCellContextMenuStripChangedCore(DataGridViewCell cell) => OnCellContextMenuStripChanged(new DataGridViewCellEventArgs(cell.ColumnIndex, cell.RowIndex));

    /// <summary>A row's ContextMenuStrip was read while it had none of its own: lets a handler supply one dynamically.</summary>
    internal ContextMenuStrip? GetRowContextMenuStripCore(int rowIndex)
    {
        var args = new DataGridViewRowContextMenuStripNeededEventArgs(rowIndex);
        OnRowContextMenuStripNeeded(args);

        return args.ContextMenuStrip;
    }

    internal void OnRowContextMenuStripChangedCore(DataGridViewRow row) => OnRowContextMenuStripChanged(new DataGridViewRowEventArgs(row));

    internal void OnColumnContextMenuStripChangedCore(DataGridViewColumn column) => OnColumnContextMenuStripChanged(new DataGridViewColumnEventArgs(column));

    /// <summary>Called from DataGridViewCell.ErrorText's setter once the text really changed, and invalidates the cell so a newly shown/hidden error icon repaints.</summary>
    internal void OnCellErrorTextChangedCore(DataGridViewCell cell)
    {
        OnCellErrorTextChanged(new DataGridViewCellEventArgs(cell.ColumnIndex, cell.RowIndex));
        InvalidateCellCore(cell);
    }

    internal void OnCellToolTipTextChangedCore(DataGridViewCell cell) => OnCellToolTipTextChanged(new DataGridViewCellEventArgs(cell.ColumnIndex, cell.RowIndex));

    /// <summary>A cell's ToolTipText was read while it had none of its own: lets a handler supply one dynamically, exactly as GetCellContextMenuStripCore supplies a menu.</summary>
    internal string? GetCellToolTipTextCore(DataGridViewCell cell, int rowIndex)
    {
        var args = new DataGridViewCellToolTipTextNeededEventArgs(cell.ColumnIndex, rowIndex, string.Empty);
        OnCellToolTipTextNeeded(args);

        return args.ToolTipText;
    }

    /// <summary>Called from DataGridViewCell.Value's getter instead of reading the cell's own backing field once the grid is virtual, exactly like GetCellErrorTextCore.</summary>
    internal object? GetCellValueCore(DataGridViewCell cell, int rowIndex)
    {
        var args = new DataGridViewCellValueEventArgs(cell.ColumnIndex, rowIndex);
        OnCellValueNeeded(args);

        return args.Value;
    }

    /// <summary>Called from DataGridViewCell.Value's setter instead of writing the cell's own backing field once the grid is virtual.</summary>
    internal void SetCellValueCore(DataGridViewCell cell, int rowIndex, object? value)
    {
        var args = new DataGridViewCellValueEventArgs(cell.ColumnIndex, rowIndex) { Value = value };
        OnCellValuePushed(args);
    }

    internal void OnCellStateChangedCore(DataGridViewCell cell, DataGridViewElementStates stateChanged) => OnCellStateChanged(new DataGridViewCellStateChangedEventArgs(cell, stateChanged));

    internal void OnCellStyleChangedCore(DataGridViewCell cell) => OnCellStyleChanged(new DataGridViewCellEventArgs(cell.ColumnIndex, cell.RowIndex));

    internal void OnCellStyleContentChangedCore(DataGridViewCellStyle style, DataGridViewCellStyleScopes scope) => OnCellStyleContentChanged(new DataGridViewCellStyleContentChangedEventArgs(style, scope));

    internal void OnColumnStateChangedCore(DataGridViewColumn column, DataGridViewElementStates stateChanged) => OnColumnStateChanged(new DataGridViewColumnStateChangedEventArgs(column, stateChanged));

    internal void OnRowStateChangedCore(DataGridViewRow row, DataGridViewElementStates stateChanged) => OnRowStateChanged(row.Index, new DataGridViewRowStateChangedEventArgs(row, stateChanged));

    internal void OnColumnDefaultCellStyleChangedCore(DataGridViewColumn column) => OnColumnDefaultCellStyleChanged(new DataGridViewColumnEventArgs(column));

    internal void OnRowDefaultCellStyleChangedCore(DataGridViewRow row) => OnRowDefaultCellStyleChanged(new DataGridViewRowEventArgs(row));

    internal void OnColumnDataPropertyNameChangedCore(DataGridViewColumn column) => OnColumnDataPropertyNameChanged(new DataGridViewColumnEventArgs(column));

    internal void OnColumnMinimumWidthChangedCore(DataGridViewColumn column) => OnColumnMinimumWidthChanged(new DataGridViewColumnEventArgs(column));

    internal void OnColumnNameChangedCore(DataGridViewColumn column) => OnColumnNameChanged(new DataGridViewColumnEventArgs(column));

    internal void OnColumnSortModeChangedCore(DataGridViewColumn column) => OnColumnSortModeChanged(new DataGridViewColumnEventArgs(column));

    internal void OnColumnToolTipTextChangedCore(DataGridViewColumn column) => OnColumnToolTipTextChanged(new DataGridViewColumnEventArgs(column));

    internal void OnColumnDividerWidthChangedCore(DataGridViewColumn column) => OnColumnDividerWidthChanged(new DataGridViewColumnEventArgs(column));

    internal void OnColumnDisplayIndexChangedCore(DataGridViewColumn column) => OnColumnDisplayIndexChanged(new DataGridViewColumnEventArgs(column));

    internal void OnColumnHeaderCellChangedCore(DataGridViewColumn column) => OnColumnHeaderCellChanged(new DataGridViewColumnEventArgs(column));

    internal void OnRowHeaderCellChangedCore(DataGridViewRow row) => OnRowHeaderCellChanged(new DataGridViewRowEventArgs(row));

    internal void OnRowDividerHeightChangedCore(DataGridViewRow row) => OnRowDividerHeightChanged(new DataGridViewRowEventArgs(row));

    internal void OnRowMinimumHeightChangedCore(DataGridViewRow row) => OnRowMinimumHeightChanged(new DataGridViewRowEventArgs(row));

    internal void OnAutoSizeColumnModeChangedCore(DataGridViewColumn column, DataGridViewAutoSizeColumnMode previousMode) => OnAutoSizeColumnModeChanged(new DataGridViewAutoSizeColumnModeEventArgs(column, previousMode));

    /// <summary>Called from DataGridViewRow.ErrorText's setter once the text really changed; GetRowErrorTextCore below is the read-side counterpart used in virtual mode.</summary>
    internal void OnRowErrorTextChangedCore(DataGridViewRow row) => OnRowErrorTextChanged(new DataGridViewRowEventArgs(row));

    /// <summary>Called from DataGridViewRow.GetErrorText instead of reading the row's own backing field once the grid is virtual, exactly like GetCellErrorTextCore.</summary>
    internal string? GetRowErrorTextCore(DataGridViewRow row, string ownErrorText)
    {
        if (!VirtualMode) return ownErrorText;

        var args = new DataGridViewRowErrorTextNeededEventArgs(row.Index, ownErrorText);
        OnRowErrorTextNeeded(args);

        return args.ErrorText;
    }

    /// <summary>Called whenever a row's height is about to be used in virtual mode, letting a handler supply it instead of the row's own Height/MinimumHeight.</summary>
    internal (int Height, int MinimumHeight) GetRowHeightInfoCore(DataGridViewRow row)
    {
        var args = new DataGridViewRowHeightInfoNeededEventArgs(row.Index, row.Height, row.MinimumHeight);
        OnRowHeightInfoNeeded(args);

        return (args.Height, args.MinimumHeight);
    }

    /// <summary>Tells a virtual-mode host the height the grid settled on for a row, after a resize; a plain (non-virtual) grid never needs to push this back anywhere.</summary>
    internal void PushRowHeightInfoCore(DataGridViewRow row)
    {
        if (!VirtualMode) return;

        OnRowHeightInfoPushed(new DataGridViewRowHeightInfoPushedEventArgs(row.Index, row.Height, row.MinimumHeight));
    }
}
