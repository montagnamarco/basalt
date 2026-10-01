using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>The columns of a DataGridView, kept in Index order; DisplayIndex (set by the grid) can reorder them visually.</summary>
public class DataGridViewColumnCollection : BaseCollection, IList
{
    private readonly ArrayList _columns = [];

    public DataGridViewColumnCollection(DataGridView dataGridView)
    {
        ArgumentNullException.ThrowIfNull(dataGridView);

        DataGridView = dataGridView;
    }

    protected DataGridView DataGridView { get; }

    public DataGridViewColumn this[int index] => (DataGridViewColumn)_columns[index]!;

    public DataGridViewColumn? this[string columnName] =>
        _columns.Cast<DataGridViewColumn>().FirstOrDefault(column => string.Equals(column.Name, columnName, StringComparison.Ordinal));

    protected override ArrayList List => _columns;

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => false;

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException("A column cannot be replaced; remove and add instead.");
    }

    public event CollectionChangeEventHandler? CollectionChanged;

    public virtual int Add(string? columnName, string? headerText) =>
        Add(new DataGridViewTextBoxColumn { Name = columnName ?? string.Empty, HeaderText = headerText ?? string.Empty });

    public virtual int Add(DataGridViewColumn dataGridViewColumn)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewColumn);

        dataGridViewColumn.SetDataGridView(DataGridView);
        dataGridViewColumn.SetDisplayIndexCore(_columns.Count);
        var index = _columns.Add(dataGridViewColumn);
        SetIndex(dataGridViewColumn, index);

        DataGridView.OnColumnAddedCore(dataGridViewColumn);
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Add, dataGridViewColumn));

        return index;
    }

    public virtual void AddRange(params DataGridViewColumn[] dataGridViewColumns)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewColumns);

        foreach (var column in dataGridViewColumns) Add(column);
    }

    public virtual void Clear()
    {
        foreach (var column in _columns.Cast<DataGridViewColumn>().ToArray()) Remove(column);
    }

    public virtual bool Contains(DataGridViewColumn dataGridViewColumn) => _columns.Contains(dataGridViewColumn);

    public virtual bool Contains(string columnName) => this[columnName] is not null;

    public void CopyTo(DataGridViewColumn[] array, int index) => _columns.CopyTo(array, index);

    public int GetColumnCount(DataGridViewElementStates includeFilter) =>
        _columns.Cast<DataGridViewColumn>().Count(column => Matches(column, includeFilter));

    public int GetColumnsWidth(DataGridViewElementStates includeFilter) =>
        _columns.Cast<DataGridViewColumn>().Where(column => Matches(column, includeFilter)).Sum(column => column.Width);

    public DataGridViewColumn GetFirstColumn(DataGridViewElementStates includeFilter) => GetFirstColumn(includeFilter, DataGridViewElementStates.None);

    public DataGridViewColumn GetFirstColumn(DataGridViewElementStates includeFilter, DataGridViewElementStates excludeFilter) =>
        InDisplayOrder().FirstOrDefault(column => Matches(column, includeFilter) && !Matches(column, excludeFilter))!;

    public DataGridViewColumn GetLastColumn(DataGridViewElementStates includeFilter, DataGridViewElementStates excludeFilter) =>
        InDisplayOrder().LastOrDefault(column => Matches(column, includeFilter) && !Matches(column, excludeFilter))!;

    public DataGridViewColumn GetNextColumn(DataGridViewColumn dataGridViewColumnStart, DataGridViewElementStates includeFilter, DataGridViewElementStates excludeFilter) =>
        InDisplayOrder().SkipWhile(column => column != dataGridViewColumnStart).Skip(1).FirstOrDefault(column => Matches(column, includeFilter) && !Matches(column, excludeFilter))!;

    public DataGridViewColumn GetPreviousColumn(DataGridViewColumn dataGridViewColumnStart, DataGridViewElementStates includeFilter, DataGridViewElementStates excludeFilter) =>
        InDisplayOrder().TakeWhile(column => column != dataGridViewColumnStart).LastOrDefault(column => Matches(column, includeFilter) && !Matches(column, excludeFilter))!;

    public int IndexOf(DataGridViewColumn dataGridViewColumn) => _columns.IndexOf(dataGridViewColumn);

    public virtual void Insert(int columnIndex, DataGridViewColumn dataGridViewColumn)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewColumn);

        dataGridViewColumn.SetDataGridView(DataGridView);
        _columns.Insert(columnIndex, dataGridViewColumn);
        ReindexFrom(columnIndex);

        DataGridView.OnColumnAddedCore(dataGridViewColumn);
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Add, dataGridViewColumn));
    }

    protected virtual void OnCollectionChanged(CollectionChangeEventArgs e) => CollectionChanged?.Invoke(this, e);

    public virtual void Remove(DataGridViewColumn dataGridViewColumn)
    {
        var index = _columns.IndexOf(dataGridViewColumn);
        if (index < 0) throw new ArgumentException("The column is not in this collection.", nameof(dataGridViewColumn));

        RemoveAt(index);
    }

    public virtual void Remove(string columnName)
    {
        if (this[columnName] is not { } column) throw new ArgumentException($"No column is named '{columnName}'.", nameof(columnName));

        Remove(column);
    }

    public virtual void RemoveAt(int index)
    {
        var column = this[index];
        DataGridView.EndEditBeforeRemoving(cell => ReferenceEquals(cell.OwningColumn, column));
        DataGridView.OnColumnRemovedCore(column);

        _columns.RemoveAt(index);
        column.SetDataGridView(null);
        ReindexFrom(index);

        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Remove, column));
    }

    int IList.Add(object? value) => Add((DataGridViewColumn)value!);

    bool IList.Contains(object? value) => value is DataGridViewColumn column && Contains(column);

    int IList.IndexOf(object? value) => value is DataGridViewColumn column ? IndexOf(column) : -1;

    void IList.Insert(int index, object? value) => Insert(index, (DataGridViewColumn)value!);

    void IList.Remove(object? value)
    {
        if (value is DataGridViewColumn column) Remove(column);
    }

    private static bool Matches(DataGridViewColumn column, DataGridViewElementStates filter)
    {
        if (filter == DataGridViewElementStates.None) return true;

        var state = DataGridViewElementStates.None;
        if (column.Visible) state |= DataGridViewElementStates.Visible;
        if (column.Frozen) state |= DataGridViewElementStates.Frozen;
        if (column.Selected) state |= DataGridViewElementStates.Selected;
        if (column.ReadOnly) state |= DataGridViewElementStates.ReadOnly;
        if (column.Displayed) state |= DataGridViewElementStates.Displayed;

        return (state & filter) == filter;
    }

    private IEnumerable<DataGridViewColumn> InDisplayOrder() => _columns.Cast<DataGridViewColumn>().OrderBy(column => column.DisplayIndex);

    private void ReindexFrom(int start)
    {
        for (var index = start; index < _columns.Count; index++) SetIndex((DataGridViewColumn)_columns[index]!, index);
    }

    private static void SetIndex(DataGridViewColumn column, int index) => column.IndexCore = index;
}
