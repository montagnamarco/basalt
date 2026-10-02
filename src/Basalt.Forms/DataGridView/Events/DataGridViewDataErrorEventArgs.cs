namespace System.Windows.Forms;

public class DataGridViewDataErrorEventArgs : DataGridViewCellCancelEventArgs
{
    public DataGridViewDataErrorEventArgs(Exception? exception, int columnIndex, int rowIndex, DataGridViewDataErrorContexts context)
        : base(columnIndex, rowIndex)
    {
        Exception = exception;
        Context = context;
    }

    public DataGridViewDataErrorContexts Context { get; }

    public Exception? Exception { get; }

    public bool ThrowException { get; set; }
}
