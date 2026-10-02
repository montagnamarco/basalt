namespace System.Windows.Forms;

/// <summary>Carries a cell and which of its DataGridViewElementStates flags just flipped (Selected, typically).</summary>
public class DataGridViewCellStateChangedEventArgs : EventArgs
{
    public DataGridViewCellStateChangedEventArgs(DataGridViewCell dataGridViewCell, DataGridViewElementStates stateChanged)
    {
        Cell = dataGridViewCell;
        StateChanged = stateChanged;
    }

    public DataGridViewCell Cell { get; }

    public DataGridViewElementStates StateChanged { get; }
}
