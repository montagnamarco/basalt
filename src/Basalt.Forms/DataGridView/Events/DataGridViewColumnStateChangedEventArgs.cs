namespace System.Windows.Forms;

/// <summary>Carries a column and which of its DataGridViewElementStates flags just flipped (Selected, Frozen, Visible, ReadOnly...).</summary>
public class DataGridViewColumnStateChangedEventArgs : EventArgs
{
    public DataGridViewColumnStateChangedEventArgs(DataGridViewColumn dataGridViewColumn, DataGridViewElementStates stateChanged)
    {
        Column = dataGridViewColumn;
        StateChanged = stateChanged;
    }

    public DataGridViewColumn Column { get; }

    public DataGridViewElementStates StateChanged { get; }
}
