using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>A snapshot of the currently selected columns, rebuilt by the grid each time SelectedColumns is read.</summary>
public class DataGridViewSelectedColumnCollection : BaseCollection, IList
{
    private readonly ArrayList _columns;

    internal DataGridViewSelectedColumnCollection(IEnumerable<DataGridViewColumn> columns)
    {
        _columns = new ArrayList(columns.ToList());
    }

    public DataGridViewColumn this[int index] => (DataGridViewColumn)_columns[index]!;

    protected override ArrayList List => _columns;

    bool IList.IsFixedSize => true;

    bool IList.IsReadOnly => true;

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException("SelectedColumns is read-only.");
    }

    public void Clear() => throw new NotSupportedException("SelectedColumns is read-only.");

    public bool Contains(DataGridViewColumn dataGridViewColumn) => _columns.Contains(dataGridViewColumn);

    public void CopyTo(DataGridViewColumn[] array, int index) => _columns.CopyTo(array, index);

    public void Insert(int index, DataGridViewColumn dataGridViewColumn) => throw new NotSupportedException("SelectedColumns is read-only.");

    int IList.Add(object? value) => throw new NotSupportedException("SelectedColumns is read-only.");

    bool IList.Contains(object? value) => value is DataGridViewColumn column && Contains(column);

    int IList.IndexOf(object? value) => value is DataGridViewColumn column ? _columns.IndexOf(column) : -1;

    void IList.Insert(int index, object? value) => Insert(index, (DataGridViewColumn)value!);

    void IList.Remove(object? value) => throw new NotSupportedException("SelectedColumns is read-only.");

    void IList.RemoveAt(int index) => throw new NotSupportedException("SelectedColumns is read-only.");
}
