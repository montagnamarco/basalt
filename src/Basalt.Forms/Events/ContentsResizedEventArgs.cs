using System.Drawing;

namespace System.Windows.Forms;

public class ContentsResizedEventArgs : EventArgs
{
    public ContentsResizedEventArgs(Rectangle newRectangle)
    {
        NewRectangle = newRectangle;
    }

    public Rectangle NewRectangle { get; }
}
