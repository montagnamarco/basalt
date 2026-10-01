using System.Drawing;

namespace System.Windows.Forms;

public class HelpEventArgs : EventArgs
{
    public HelpEventArgs(Point mousePos)
    {
        MousePos = mousePos;
    }

    public bool Handled { get; set; }

    public Point MousePos { get; }
}
