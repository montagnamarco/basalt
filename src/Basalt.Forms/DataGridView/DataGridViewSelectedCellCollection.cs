using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>A snapshot of the currently selected cells, rebuilt by the grid each time SelectedCells is read.</summary>
public class DataGridViewSelectedCellCollection : BaseCollection, IList
{
    private readonly ArrayList _cells;

    internal DataGridViewSelectedCellCollection(IEnumerable<DataGridViewCell> cells)
    {
        _cells = new ArrayList(cells.ToList());
    }

    public DataGridViewCell this[int index] => (DataGridViewCell)_cells[index]!;

    protected override ArrayList List => _cells;

    bool IList.IsFixedSize => true;

    bool IList.IsReadOnly => true;

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException("SelectedCells is read-only.");
    }

    public void Clear() => throw new NotSupportedException("SelectedCells is read-only.");

    public bool Contains(DataGridViewCell dataGridViewCell) => _cells.Contains(dataGridViewCell);

    public void CopyTo(DataGridViewCell[] array, int index) => _cells.CopyTo(array, index);

    public void Insert(int index, DataGridViewCell dataGridViewCell) => throw new NotSupportedException("SelectedCells is read-only.");

    int IList.Add(object? value) => throw new NotSupportedException("SelectedCells is read-only.");

    bool IList.Contains(object? value) => value is DataGridViewCell cell && Contains(cell);

    int IList.IndexOf(object? value) => value is DataGridViewCell cell ? _cells.IndexOf(cell) : -1;

    void IList.Insert(int index, object? value) => Insert(index, (DataGridViewCell)value!);

    void IList.Remove(object? value) => throw new NotSupportedException("SelectedCells is read-only.");

    void IList.RemoveAt(int index) => throw new NotSupportedException("SelectedCells is read-only.");
}
