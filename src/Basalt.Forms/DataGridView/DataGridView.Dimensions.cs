using System.ComponentModel;

namespace System.Windows.Forms;

public partial class DataGridView
{
    [Browsable(false)]
    public int ColumnCount
    {
        get => Columns.Count;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (DataSource is not null)
                throw new InvalidOperationException("ColumnCount cannot be changed while the grid is data-bound.");
            while (Columns.Count > value) Columns.RemoveAt(Columns.Count - 1);
            while (Columns.Count < value) Columns.Add(new DataGridViewTextBoxColumn());
        }
    }
}
