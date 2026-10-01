namespace System.Windows.Forms;

public class SplitterEventArgs : EventArgs
{
    public SplitterEventArgs(int x, int y, int splitX, int splitY)
    {
        X = x;
        Y = y;
        SplitX = splitX;
        SplitY = splitY;
    }

    public int SplitX { get; set; }

    public int SplitY { get; set; }

    public int X { get; }

    public int Y { get; }
}
