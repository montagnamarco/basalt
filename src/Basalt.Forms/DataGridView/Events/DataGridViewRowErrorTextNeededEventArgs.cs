namespace System.Windows.Forms;

/// <summary>Carries a row's error text for a virtual-mode handler to supply on demand, instead of the grid reading it from the row's own ErrorText.</summary>
public class DataGridViewRowErrorTextNeededEventArgs : EventArgs
{
    internal DataGridViewRowErrorTextNeededEventArgs(int rowIndex, string errorText)
    {
        RowIndex = rowIndex;
        ErrorText = errorText;
    }

    public string? ErrorText { get; set; }

    public int RowIndex { get; }
}
