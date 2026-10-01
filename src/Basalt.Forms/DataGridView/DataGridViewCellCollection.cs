using System.Collections;
using System.ComponentModel;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>The cells of one DataGridViewRow, one per column, kept in column index order.</summary>
public class DataGridViewCellCollection : BaseCollection, IList
{
    private readonly ArrayList _cells = [];
    private readonly DataGridViewRow _owner;

    public DataGridViewCellCollection(DataGridViewRow dataGridViewRow)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewRow);

        _owner = dataGridViewRow;
    }

    public DataGridViewCell this[int index]
    {
        get => (DataGridViewCell)_cells[index]!;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            var old = (DataGridViewCell)_cells[index]!;
            old.OwningRow = null;
            old.OwningColumn = null;

            value.OwningRow = _owner;
            value.OwningColumn = _owner.DataGridView?.Columns[index];
            _cells[index] = value;
        }
    }

    public DataGridViewCell this[string columnName]
    {
        get => this[ColumnIndexOf(columnName)];
        set => this[ColumnIndexOf(columnName)] = value;
    }

    protected override ArrayList List => _cells;

    bool IList.IsFixedSize => false;

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = (DataGridViewCell)value!;
    }

    public event CollectionChangeEventHandler? CollectionChanged;

    public virtual int Add(DataGridViewCell dataGridViewCell)
    {
        var index = AddCore(dataGridViewCell);
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Add, dataGridViewCell));

        return index;
    }

    public virtual void AddRange(params DataGridViewCell[] dataGridViewCells)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewCells);

        foreach (var cell in dataGridViewCells) Add(cell);
    }

    public virtual void Clear()
    {
        foreach (var cell in _cells.Cast<DataGridViewCell>())
        {
            cell.OwningRow = null;
            cell.OwningColumn = null;
        }

        _cells.Clear();
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Refresh, null));
    }

    public virtual bool Contains(DataGridViewCell dataGridViewCell) => _cells.Contains(dataGridViewCell);

    public void CopyTo(DataGridViewCell[] array, int index) => _cells.CopyTo(array, index);

    public int IndexOf(DataGridViewCell dataGridViewCell) => _cells.IndexOf(dataGridViewCell);

    public virtual void Insert(int index, DataGridViewCell dataGridViewCell)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewCell);

        dataGridViewCell.OwningRow = _owner;
        // The column at this position is already the final one by the time Insert runs (both
        // column insertion and this cell insertion happen at the same, already-reindexed index),
        // so the cell's style/ColumnIndex resolve correctly from the moment it is in the collection.
        dataGridViewCell.OwningColumn = _owner.DataGridView?.Columns[index];
        _cells.Insert(index, dataGridViewCell);
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Add, dataGridViewCell));
    }

    protected void OnCollectionChanged(CollectionChangeEventArgs e) => CollectionChanged?.Invoke(this, e);

    public virtual void Remove(DataGridViewCell cell)
    {
        if (!_cells.Contains(cell)) throw new ArgumentException("The cell is not in this collection.", nameof(cell));

        cell.OwningRow = null;
        cell.OwningColumn = null;
        _cells.Remove(cell);
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Remove, cell));
    }

    public virtual void RemoveAt(int index)
    {
        var cell = this[index];
        Remove(cell);
    }

    int IList.Add(object? value) => Add((DataGridViewCell)value!);

    bool IList.Contains(object? value) => value is DataGridViewCell cell && Contains(cell);

    int IList.IndexOf(object? value) => value is DataGridViewCell cell ? IndexOf(cell) : -1;

    void IList.Insert(int index, object? value) => Insert(index, (DataGridViewCell)value!);

    void IList.Remove(object? value)
    {
        if (value is DataGridViewCell cell) Remove(cell);
    }

    /// <summary>Appends a cell without raising CollectionChanged, for CreateCells building a fresh row's cells one per column.</summary>
    internal int AddCore(DataGridViewCell dataGridViewCell)
    {
        ArgumentNullException.ThrowIfNull(dataGridViewCell);

        dataGridViewCell.OwningRow = _owner;

        return _cells.Add(dataGridViewCell);
    }

    private int ColumnIndexOf(string columnName)
    {
        var column = _owner.DataGridView?.Columns[columnName] ?? throw new ArgumentException($"No column is named '{columnName}'.", nameof(columnName));

        return column.Index;
    }
}
