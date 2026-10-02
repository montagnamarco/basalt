using System.Collections;
using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// Keeps Rows mirroring the CurrencyManager's list (ItemAdded/ItemDeleted/ItemChanged/Reset,
/// without touching Columns) and CurrentCell's row in step with the manager's Position, both
/// ways. Resolution, column generation and the one-shot full rebuild live in DataGridView.Binding.cs.
/// </summary>
public partial class DataGridView
{
    /// <summary>Set while this grid is pushing its own Position change, so the manager's PositionChanged does not bounce straight back.</summary>
    private bool _syncingPosition;

    private void WireCurrencyManager(CurrencyManager manager)
    {
        manager.ListChanged += OnManagerListChanged;
        manager.PositionChanged += OnManagerPositionChanged;
        manager.MetaDataChanged += OnManagerMetaDataChanged;
    }

    private void UnwireCurrencyManager()
    {
        if (_currencyManager is not { } manager) return;

        manager.ListChanged -= OnManagerListChanged;
        manager.PositionChanged -= OnManagerPositionChanged;
        manager.MetaDataChanged -= OnManagerMetaDataChanged;
    }

    /// <summary>The list itself changed: mirrored into Rows without ever rebuilding Columns from here.</summary>
    private void OnManagerListChanged(object? sender, ListChangedEventArgs e)
    {
        switch (e.ListChangedType)
        {
            case ListChangedType.ItemAdded:
                InsertBoundRow(e.NewIndex);
                break;

            case ListChangedType.ItemDeleted:
                RemoveBoundRow(e.NewIndex);
                break;

            case ListChangedType.ItemChanged:
                RefreshBoundRow(e.NewIndex);
                break;

            case ListChangedType.Reset:
                PopulateRowsFromList();
                break;

            case ListChangedType.ItemMoved:
                MoveBoundRow(e.OldIndex, e.NewIndex);
                break;

            case ListChangedType.PropertyDescriptorChanged:
                // The item type's own shape changed: rebuilds Columns and raises DataBindingComplete itself, with this same type.
                RebindFromCurrencyManager(rebuildColumns: true, completionType: e.ListChangedType);
                return;

            default:
                return;
        }

        OnDataBindingComplete(new DataGridViewBindingCompleteEventArgs(e.ListChangedType));
    }

    /// <summary>
    /// Moves the row itself (not a new one) from its old position to its new one, so the same
    /// row - and the cell CurrentCell holds in it, if any - keeps showing the same item. Removing
    /// it would otherwise make OnRowsRemovedCore think the row is gone for good and move
    /// CurrentCell elsewhere, so a cell that was current in it is restored once it is back in.
    /// </summary>
    private void MoveBoundRow(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Rows.Count || newIndex < 0) return;

        var row = Rows[oldIndex];
        var currentInMovedRow = ReferenceEquals(_currentCell?.OwningRow, row) ? _currentCell : null;

        Rows.RemoveBoundAt(oldIndex);

        var insertAt = Math.Min(newIndex, Rows.Count);
        Rows.InsertBound(insertAt, row);

        if (currentInMovedRow is not null) SetCurrentCell(currentInMovedRow, throughMouseClick: false);
    }

    /// <summary>The schema itself changed (SetDataSource onto a differently-typed list): the one case that does rebuild Columns.</summary>
    private void OnManagerMetaDataChanged(object? sender, EventArgs e) => RebindFromCurrencyManager(rebuildColumns: true, completionType: ListChangedType.Reset);

    private void OnManagerPositionChanged(object? sender, EventArgs e)
    {
        if (_syncingPosition) return;

        _syncingPosition = true;
        try
        {
            SyncCurrentCellFromPosition();
        }
        finally
        {
            _syncingPosition = false;
        }
    }

    /// <summary>Called by SetCurrentCell (DataGridView.Selection.cs) once CurrentCell's row actually moved: pushes it into the manager, unless the manager itself caused the move.</summary>
    private void SyncCurrencyManagerPosition()
    {
        if (_currencyManager is null || _syncingPosition) return;

        var rowIndex = _currentCell?.RowIndex ?? -1;
        if (rowIndex < 0 || rowIndex == _currencyManager.Position) return;

        _syncingPosition = true;
        try
        {
            _currencyManager.Position = rowIndex;
        }
        finally
        {
            _syncingPosition = false;
        }
    }

    /// <summary>Moves CurrentCell to the manager's row, keeping the current column when there is one.</summary>
    private void SyncCurrentCellFromPosition()
    {
        if (_currencyManager is null || Columns.Count == 0) return;

        var rowIndex = _currencyManager.Position;
        if (rowIndex < 0 || rowIndex >= Rows.Count || rowIndex == (_currentCell?.RowIndex ?? -1)) return;

        var columnIndex = _currentCell?.ColumnIndex ?? 0;
        if (columnIndex < 0 || columnIndex >= Columns.Count) columnIndex = 0;

        SetCurrentCell(Rows[rowIndex].Cells[columnIndex], throughMouseClick: false);
    }

    /// <summary>Rebuilds every row from the manager's list, keeping whatever columns already exist.</summary>
    private void PopulateRowsFromList()
    {
        ClearBoundRows();
        if (_currencyManager is null) return;

        for (var index = 0; index < _currencyManager.Count; index++) AppendBoundRow(_currencyManager.List[index]);
    }

    private void AppendBoundRow(object? item)
    {
        var row = (DataGridViewRow)RowTemplate.Clone();

        Rows.AddBound(row);
        row.DataBoundItem = item;
        PopulateRowValues(row, item);
    }

    private void InsertBoundRow(int index)
    {
        if (_currencyManager is null || index < 0) return;

        var row = (DataGridViewRow)RowTemplate.Clone();
        var item = index < _currencyManager.Count ? _currencyManager.List[index] : null;

        if (index >= Rows.Count) Rows.AddBound(row);
        else Rows.InsertBound(index, row);

        row.DataBoundItem = item;
        PopulateRowValues(row, item);
    }

    private void RemoveBoundRow(int index)
    {
        if (index < 0 || index >= Rows.Count) return;

        Rows.RemoveBoundAt(index);
    }

    private void RefreshBoundRow(int index)
    {
        if (_currencyManager is null || index < 0 || index >= Rows.Count) return;

        var item = index < _currencyManager.Count ? _currencyManager.List[index] : null;
        var row = Rows[index];

        row.DataBoundItem = item;
        PopulateRowValues(row, item);
    }

    private void ClearBoundRows()
    {
        if (Rows.Count > 0) Rows.ClearBound();
    }

    private void PopulateRowValues(DataGridViewRow row, object? item)
    {
        if (item is null) return;

        foreach (DataGridViewColumn column in Columns)
        {
            if (!column.IsDataBound) continue;
            if (_itemProperties?.Find(column.DataPropertyName, true) is not { } descriptor) continue;

            row.Cells[column.Index].Value = ReadBoundValue(descriptor, item);
        }
    }

    /// <summary>Called from OnColumnAddedCore (DataGridView.Events.cs) once a newly added column has a cell in every row: fills just that column from the bound items.</summary>
    private void PopulateNewColumnValues(DataGridViewColumn column)
    {
        if (_currencyManager is null || !column.IsDataBound) return;
        if (_itemProperties?.Find(column.DataPropertyName, true) is not { } descriptor) return;

        foreach (DataGridViewRow row in Rows)
        {
            if (row.DataBoundItem is { } item) row.Cells[column.Index].Value = ReadBoundValue(descriptor, item);
        }
    }

    /// <summary>A DataRowView's own columns answer DBNull.Value, not a plain null, for an empty cell: normalised here so cell.Value is never DBNull.</summary>
    private static object? ReadBoundValue(PropertyDescriptor descriptor, object item)
    {
        var value = descriptor.GetValue(item);

        return value is DBNull ? null : value;
    }
}
