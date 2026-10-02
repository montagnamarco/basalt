using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>Carries the row about to be removed by the user (Delete key, with AllowUserToDeleteRows), cancellable.</summary>
public class DataGridViewRowCancelEventArgs : CancelEventArgs
{
    public DataGridViewRowCancelEventArgs(DataGridViewRow dataGridViewRow)
    {
        Row = dataGridViewRow;
    }

    public DataGridViewRow Row { get; set; }
}
