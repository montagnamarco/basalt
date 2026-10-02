using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>
/// The rows of a DataGridView, kept in display order (there is no separate DisplayIndex for rows,
/// unlike columns). Rows are never shared in this implementation: Microsoft's row-sharing
/// optimisation for very large grids is left out, as the contract allows.
/// </summary>
public class DataGridViewRowCollection : ICollection, IEnumerable, IList
{
    private readonly ArrayList _rows = [];
    private readonly DataGridView _dataGridView;

    public DataGridViewRowCollection(DataGridView dataGridView)
    {
        ArgumentNullException.ThrowIfNull(dataGridView);

        _dataGridView = dataGridView;
    }

    public int Count => _rows.Count;

    protected DataGridView DataGridView => _dataGridView;

    public DataGridViewRow this[int index] => (DataGridViewRow)_rows[index]!;

    protected ArrayList List => _rows;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => false;

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException("A row cannot be replaced; remove and add instead.");
    }

    public event CollectionChangeEventHandler? CollectionChanged;

    public virtual int Add() => Add(CreateNewRow());

    public virtual int Add(params object[] values)
    {
        var row = CreateNewRow();
        row.CreateCells(_dataGridView, values);

        return Add(row);
    }

    public virtual int Add(DataGridViewRow dataGridViewRow)
    {
        EnsureNotDataBound();

        return AddCore(dataGridViewRow);
    }

    private int AddCore(DataGridViewRow dataGridViewRow)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewRow);

        dataGridViewRow.SetDataGridView(_dataGridView);
        if (dataGridViewRow.Cells.Count == 0) dataGridViewRow.CreateCells(_dataGridView);

        var index = _rows.Add(dataGridViewRow);
        dataGridViewRow.IndexCore = index;

        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Add, dataGridViewRow));
        _dataGridView.OnRowsAddedCore(index, 1);

        return index;
    }

    public virtual int Add(int count)
    {
        var first = -1;
        for (var i = 0; i < count; i++)
        {
            var added = Add();
            if (first < 0) first = added;
        }

        return first;
    }

    public virtual int AddCopies(int indexSource, int count)
    {
        var first = -1;
        for (var i = 0; i < count; i++)
        {
            var added = AddCopy(indexSource);
            if (first < 0) first = added;
        }

        return first;
    }

    public virtual int AddCopy(int indexSource) => Add((DataGridViewRow)this[indexSource].Clone());

    public virtual void AddRange(params DataGridViewRow[] dataGridViewRows)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewRows);

        foreach (var row in dataGridViewRows) Add(row);
    }

    public virtual void Clear()
    {
        EnsureNotDataBound();
        ClearCore();
    }

    private void ClearCore()
    {
        var count = _rows.Count;
        if (count == 0) return;

        // As RemoveAt: a pending edit is committed first, and a refused commit stops the clear.
        _dataGridView.EndEditBeforeRemoving(_ => true);

        foreach (var row in _rows.Cast<DataGridViewRow>()) row.SetDataGridView(null);

        _rows.Clear();
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Refresh, null));
        _dataGridView.OnRowsRemovedCore(0, count);
    }

    public virtual bool Contains(DataGridViewRow dataGridViewRow) => _rows.Contains(dataGridViewRow);

    public void CopyTo(DataGridViewRow[] array, int index) => _rows.CopyTo(array, index);

    public int GetFirstRow(DataGridViewElementStates includeFilter) => GetFirstRow(includeFilter, DataGridViewElementStates.None);

    public int GetFirstRow(DataGridViewElementStates includeFilter, DataGridViewElementStates excludeFilter)
    {
        for (var index = 0; index < _rows.Count; index++)
        {
            if (Matches(this[index], includeFilter) && !Matches(this[index], excludeFilter)) return index;
        }

        return -1;
    }

    public int GetLastRow(DataGridViewElementStates includeFilter)
    {
        for (var index = _rows.Count - 1; index >= 0; index--)
        {
            if (Matches(this[index], includeFilter)) return index;
        }

        return -1;
    }

    public int GetNextRow(int indexStart, DataGridViewElementStates includeFilter) => GetNextRow(indexStart, includeFilter, DataGridViewElementStates.None);

    public int GetNextRow(int indexStart, DataGridViewElementStates includeFilter, DataGridViewElementStates excludeFilter)
    {
        for (var index = indexStart + 1; index < _rows.Count; index++)
        {
            if (Matches(this[index], includeFilter) && !Matches(this[index], excludeFilter)) return index;
        }

        return -1;
    }

    public int GetPreviousRow(int indexStart, DataGridViewElementStates includeFilter) => GetPreviousRow(indexStart, includeFilter, DataGridViewElementStates.None);

    public int GetPreviousRow(int indexStart, DataGridViewElementStates includeFilter, DataGridViewElementStates excludeFilter)
    {
        for (var index = indexStart - 1; index >= 0; index--)
        {
            if (Matches(this[index], includeFilter) && !Matches(this[index], excludeFilter)) return index;
        }

        return -1;
    }

    public int GetRowCount(DataGridViewElementStates includeFilter) => _rows.Cast<DataGridViewRow>().Count(row => Matches(row, includeFilter));

    public virtual DataGridViewElementStates GetRowState(int rowIndex) => this[rowIndex].State;

    public int GetRowsHeight(DataGridViewElementStates includeFilter) => _rows.Cast<DataGridViewRow>().Where(row => Matches(row, includeFilter)).Sum(row => row.Height);

    public int IndexOf(DataGridViewRow dataGridViewRow) => _rows.IndexOf(dataGridViewRow);

    public virtual void Insert(int rowIndex, params object[] values)
    {
        var row = CreateNewRow();
        row.CreateCells(_dataGridView, values);
        Insert(rowIndex, row);
    }

    public virtual void Insert(int rowIndex, DataGridViewRow dataGridViewRow)
    {
        EnsureNotDataBound();
        InsertCore(rowIndex, dataGridViewRow);
    }

    private void InsertCore(int rowIndex, DataGridViewRow dataGridViewRow)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewRow);

        dataGridViewRow.SetDataGridView(_dataGridView);
        if (dataGridViewRow.Cells.Count == 0) dataGridViewRow.CreateCells(_dataGridView);

        _rows.Insert(rowIndex, dataGridViewRow);
        ReindexFrom(rowIndex);

        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Add, dataGridViewRow));
        _dataGridView.OnRowsAddedCore(rowIndex, 1);
    }

    public virtual void Insert(int rowIndex, int count)
    {
        for (var i = 0; i < count; i++) Insert(rowIndex + i, CreateNewRow());
    }

    public virtual void InsertCopies(int indexSource, int indexDestination, int count)
    {
        for (var i = 0; i < count; i++) InsertCopy(indexSource, indexDestination + i);
    }

    public virtual void InsertCopy(int indexSource, int indexDestination) => Insert(indexDestination, (DataGridViewRow)this[indexSource].Clone());

    public virtual void InsertRange(int rowIndex, params DataGridViewRow[] dataGridViewRows)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewRows);

        for (var i = 0; i < dataGridViewRows.Length; i++) Insert(rowIndex + i, dataGridViewRows[i]);
    }

    protected virtual void OnCollectionChanged(CollectionChangeEventArgs e) => CollectionChanged?.Invoke(this, e);

    public virtual void Remove(DataGridViewRow dataGridViewRow)
    {
        var index = _rows.IndexOf(dataGridViewRow);
        if (index < 0) throw new ArgumentException("The row is not in this collection.", nameof(dataGridViewRow));

        RemoveAt(index);
    }

    public virtual void RemoveAt(int index)
    {
        EnsureNotDataBound();
        RemoveAtCore(index);
    }

    private void RemoveAtCore(int index)
    {
        var row = this[index];
        _dataGridView.EndEditBeforeRemoving(cell => ReferenceEquals(cell.OwningRow, row));

        _rows.RemoveAt(index);
        row.SetDataGridView(null);
        ReindexFrom(index);

        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Remove, row));
        _dataGridView.OnRowsRemovedCore(index, 1);
    }

    /// <summary>Row sharing is not implemented: every row already is its own unshared instance, so this simply indexes.</summary>
    public DataGridViewRow SharedRow(int rowIndex) => this[rowIndex];

    /// <summary>
    /// Refuses a programmatic Add/Insert/Remove/Clear while the grid is data-bound, as WinForms
    /// does: the bound list, through the CurrencyManager, is the only thing allowed to change
    /// which rows exist. AddBound/InsertBound/RemoveBoundAt/ClearBound (called only from
    /// DataGridView's own binding code, mirroring the list) bypass this check.
    /// </summary>
    private void EnsureNotDataBound()
    {
        if (_dataGridView.IsDataBound)
        {
            throw new InvalidOperationException("Rows cannot be programmatically added, inserted, or removed from the DataGridView control's rows collection when the control is data-bound.");
        }
    }

    /// <summary>Mirrors a bound list item being added, bypassing the data-bound guard: called only from DataGridView's own binding code.</summary>
    internal int AddBound(DataGridViewRow dataGridViewRow) => AddCore(dataGridViewRow);

    /// <summary>Mirrors a bound list item being inserted, bypassing the data-bound guard: called only from DataGridView's own binding code.</summary>
    internal void InsertBound(int rowIndex, DataGridViewRow dataGridViewRow) => InsertCore(rowIndex, dataGridViewRow);

    /// <summary>Mirrors a bound list item being removed, bypassing the data-bound guard: called only from DataGridView's own binding code.</summary>
    internal void RemoveBoundAt(int index) => RemoveAtCore(index);

    /// <summary>Mirrors the bound list being reset, bypassing the data-bound guard: called only from DataGridView's own binding code.</summary>
    internal void ClearBound() => ClearCore();

    IEnumerator IEnumerable.GetEnumerator() => _rows.GetEnumerator();

    void ICollection.CopyTo(Array array, int index) => _rows.CopyTo(array, index);

    int IList.Add(object? value) => Add((DataGridViewRow)value!);

    bool IList.Contains(object? value) => value is DataGridViewRow row && Contains(row);

    int IList.IndexOf(object? value) => value is DataGridViewRow row ? IndexOf(row) : -1;

    void IList.Insert(int index, object? value) => Insert(index, (DataGridViewRow)value!);

    void IList.Remove(object? value)
    {
        if (value is DataGridViewRow row) Remove(row);
    }

    /// <summary>
    /// A fresh row cloned from RowTemplate, already attached to this grid: attaching first and
    /// creating its cells after means each cell's OwningRow.DataGridView is already this one.
    /// </summary>
    private DataGridViewRow CreateNewRow()
    {
        var row = (DataGridViewRow)_dataGridView.RowTemplate.Clone();
        row.SetDataGridView(_dataGridView);
        row.CreateCells(_dataGridView);

        return row;
    }

    private static bool Matches(DataGridViewRow row, DataGridViewElementStates filter)
    {
        if (filter == DataGridViewElementStates.None) return true;

        var state = row.State;

        return (state & filter) == filter;
    }

    /// <summary>Puts the rows back in the given order, for Sort: no row is added or removed, so nothing is raised beyond a repaint.</summary>
    internal void ReorderCore(IReadOnlyList<DataGridViewRow> ordered)
    {
        _rows.Clear();
        _rows.AddRange(ordered.Cast<object>().ToArray());
        ReindexFrom(0);
    }

    private void ReindexFrom(int start)
    {
        for (var index = start; index < _rows.Count; index++) this[index].IndexCore = index;
    }
}
