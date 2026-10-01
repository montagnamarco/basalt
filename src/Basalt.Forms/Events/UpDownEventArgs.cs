namespace System.Windows.Forms;

/// <summary>Which spin button (up or down) of an <see cref="UpDownBase"/> was pushed.</summary>
public class UpDownEventArgs : EventArgs
{
    public UpDownEventArgs(int buttonPushed)
    {
        ButtonID = buttonPushed;
    }

    public int ButtonID { get; }
}
