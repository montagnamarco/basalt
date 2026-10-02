namespace System.Windows.Forms;

/// <summary>Carries a row and which of its DataGridViewElementStates flags just flipped (Selected, Frozen, Visible, ReadOnly...).</summary>
public class DataGridViewRowStateChangedEventArgs : EventArgs
{
    public DataGridViewRowStateChangedEventArgs(DataGridViewRow dataGridViewRow, DataGridViewElementStates stateChanged)
    {
        Row = dataGridViewRow;
        StateChanged = stateChanged;
    }

    public DataGridViewRow Row { get; }

    public DataGridViewElementStates StateChanged { get; }
}
