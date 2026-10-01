namespace System.Windows.Forms;

/// <summary>What a row, column or cell shares: a back-reference to the owning grid, and the few
/// Raise* helpers a derived type uses to bubble an event up into the grid's own On* methods.</summary>
public class DataGridViewElement
{
    private DataGridView? _dataGridView;

    public DataGridView? DataGridView => _dataGridView;

    public virtual DataGridViewElementStates State => DataGridViewElementStates.None;

    /// <summary>Attaches (or detaches, with null) the grid that now owns this element.</summary>
    internal void SetDataGridView(DataGridView? dataGridView)
    {
        if (ReferenceEquals(_dataGridView, dataGridView)) return;

        _dataGridView = dataGridView;
        OnDataGridViewChanged();
    }

    protected virtual void OnDataGridViewChanged()
    {
    }

    protected void RaiseCellClick(DataGridViewCellEventArgs e) => _dataGridView?.RaiseCellClickCore(e);

    protected void RaiseCellContentClick(DataGridViewCellEventArgs e) => _dataGridView?.RaiseCellContentClickCore(e);

    protected void RaiseCellContentDoubleClick(DataGridViewCellEventArgs e) => _dataGridView?.RaiseCellContentDoubleClickCore(e);

    protected void RaiseCellValueChanged(DataGridViewCellEventArgs e) => _dataGridView?.RaiseCellValueChangedCore(e);
}
