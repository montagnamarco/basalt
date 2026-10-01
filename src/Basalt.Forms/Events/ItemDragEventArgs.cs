namespace System.Windows.Forms;

public class ItemDragEventArgs : EventArgs
{
    public ItemDragEventArgs(MouseButtons button)
    {
        Button = button;
    }

    public ItemDragEventArgs(MouseButtons button, object? item)
    {
        Button = button;
        Item = item;
    }

    public MouseButtons Button { get; }

    public object? Item { get; }
}
