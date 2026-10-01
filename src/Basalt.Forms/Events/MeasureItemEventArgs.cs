using System.Drawing;

namespace System.Windows.Forms;

public class MeasureItemEventArgs : EventArgs
{
    public MeasureItemEventArgs(Graphics graphics, int index)
    {
        Graphics = graphics;
        Index = index;
    }

    public MeasureItemEventArgs(Graphics graphics, int index, int itemHeight)
    {
        Graphics = graphics;
        Index = index;
        ItemHeight = itemHeight;
    }

    public Graphics Graphics { get; }

    public int Index { get; }

    public int ItemHeight { get; set; }

    public int ItemWidth { get; set; }
}
