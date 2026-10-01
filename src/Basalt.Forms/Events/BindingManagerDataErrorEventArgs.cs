namespace System.Windows.Forms;

public class BindingManagerDataErrorEventArgs : EventArgs
{
    public BindingManagerDataErrorEventArgs(Exception exception)
    {
        Exception = exception;
    }

    public Exception Exception { get; }
}
