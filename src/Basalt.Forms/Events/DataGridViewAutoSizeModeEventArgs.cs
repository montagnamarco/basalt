namespace System.Windows.Forms;

public class DataGridViewAutoSizeModeEventArgs : EventArgs
{
    public DataGridViewAutoSizeModeEventArgs(bool previousModeAutoSized)
    {
        PreviousModeAutoSized = previousModeAutoSized;
    }

    public bool PreviousModeAutoSized { get; }
}
