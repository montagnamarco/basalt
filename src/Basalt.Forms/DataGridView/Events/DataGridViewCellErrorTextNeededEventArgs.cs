namespace System.Windows.Forms;

/// <summary>Carries a cell's error text for a virtual-mode handler to supply on demand, instead of the grid reading it from the cell's own ErrorText.</summary>
public class DataGridViewCellErrorTextNeededEventArgs : DataGridViewCellEventArgs
{
    internal DataGridViewCellErrorTextNeededEventArgs(int columnIndex, int rowIndex, string errorText)
        : base(columnIndex, rowIndex)
    {
        ErrorText = errorText;
    }

    public string ErrorText { get; set; }
}
