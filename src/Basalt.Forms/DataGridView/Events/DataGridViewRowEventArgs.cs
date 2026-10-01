namespace System.Windows.Forms;

/// <summary>Carries the row an event is about.</summary>
public class DataGridViewRowEventArgs : EventArgs
{
    public DataGridViewRowEventArgs(DataGridViewRow dataGridViewRow)
    {
        Row = dataGridViewRow;
    }

    public DataGridViewRow Row { get; }
}
