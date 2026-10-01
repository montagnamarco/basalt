using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>A snapshot of the currently selected rows, rebuilt by the grid each time SelectedRows is read.</summary>
public class DataGridViewSelectedRowCollection : BaseCollection, IList
{
    private readonly ArrayList _rows;

    internal DataGridViewSelectedRowCollection(IEnumerable<DataGridViewRow> rows)
    {
        _rows = new ArrayList(rows.ToList());
    }

    public DataGridViewRow this[int index] => (DataGridViewRow)_rows[index]!;

    protected override ArrayList List => _rows;

    bool IList.IsFixedSize => true;

    bool IList.IsReadOnly => true;

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException("SelectedRows is read-only.");
    }

    public void Clear() => throw new NotSupportedException("SelectedRows is read-only.");

    public bool Contains(DataGridViewRow dataGridViewRow) => _rows.Contains(dataGridViewRow);

    public void CopyTo(DataGridViewRow[] array, int index) => _rows.CopyTo(array, index);

    public void Insert(int index, DataGridViewRow dataGridViewRow) => throw new NotSupportedException("SelectedRows is read-only.");

    int IList.Add(object? value) => throw new NotSupportedException("SelectedRows is read-only.");

    bool IList.Contains(object? value) => value is DataGridViewRow row && Contains(row);

    int IList.IndexOf(object? value) => value is DataGridViewRow row ? _rows.IndexOf(row) : -1;

    void IList.Insert(int index, object? value) => Insert(index, (DataGridViewRow)value!);

    void IList.Remove(object? value) => throw new NotSupportedException("SelectedRows is read-only.");

    void IList.RemoveAt(int index) => throw new NotSupportedException("SelectedRows is read-only.");
}
