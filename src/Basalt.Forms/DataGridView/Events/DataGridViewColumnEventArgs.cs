namespace System.Windows.Forms;

/// <summary>Carries the column an event is about.</summary>
public class DataGridViewColumnEventArgs : EventArgs
{
    public DataGridViewColumnEventArgs(DataGridViewColumn dataGridViewColumn)
    {
        Column = dataGridViewColumn;
    }

    public DataGridViewColumn Column { get; }
}
