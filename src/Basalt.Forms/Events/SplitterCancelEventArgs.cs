using System.ComponentModel;

namespace System.Windows.Forms;

public class SplitterCancelEventArgs : CancelEventArgs
{
    public SplitterCancelEventArgs(int mouseCursorX, int mouseCursorY, int splitX, int splitY)
    {
        MouseCursorX = mouseCursorX;
        MouseCursorY = mouseCursorY;
        SplitX = splitX;
        SplitY = splitY;
    }

    public int MouseCursorX { get; }

    public int MouseCursorY { get; }

    public int SplitX { get; set; }

    public int SplitY { get; set; }
}
