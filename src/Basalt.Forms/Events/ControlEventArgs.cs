namespace System.Windows.Forms;

public class ControlEventArgs : EventArgs
{
    public ControlEventArgs(Control? control)
    {
        Control = control;
    }

    public Control? Control { get; }
}
