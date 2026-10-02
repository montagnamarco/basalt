namespace System.Windows.Forms;

/// <summary>Carries the column and its previous AutoSizeMode when DataGridView.AutoSizeColumnsMode changes it.</summary>
public class DataGridViewAutoSizeColumnModeEventArgs : EventArgs
{
    public DataGridViewAutoSizeColumnModeEventArgs(DataGridViewColumn dataGridViewColumn, DataGridViewAutoSizeColumnMode previousModeCollection)
    {
        Column = dataGridViewColumn;
        PreviousMode = previousModeCollection;
    }

    public DataGridViewColumn Column { get; }

    public DataGridViewAutoSizeColumnMode PreviousMode { get; }
}
