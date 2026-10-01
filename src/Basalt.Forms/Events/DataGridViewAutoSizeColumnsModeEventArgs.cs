namespace System.Windows.Forms;

public class DataGridViewAutoSizeColumnsModeEventArgs : EventArgs
{
    public DataGridViewAutoSizeColumnsModeEventArgs(DataGridViewAutoSizeColumnMode[] previousModes)
    {
        PreviousModes = previousModes;
    }

    public DataGridViewAutoSizeColumnMode[] PreviousModes { get; }
}
