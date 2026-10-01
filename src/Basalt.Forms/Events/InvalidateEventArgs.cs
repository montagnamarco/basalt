using System.Drawing;

namespace System.Windows.Forms;

public class InvalidateEventArgs : EventArgs
{
    public InvalidateEventArgs(Rectangle invalidRect)
    {
        InvalidRect = invalidRect;
    }

    public Rectangle InvalidRect { get; }
}
