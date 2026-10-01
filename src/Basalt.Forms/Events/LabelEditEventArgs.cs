namespace System.Windows.Forms;

public class LabelEditEventArgs : EventArgs
{
    public LabelEditEventArgs(int item)
    {
        Item = item;
    }

    public LabelEditEventArgs(int item, string? label)
    {
        Item = item;
        Label = label;
    }

    public bool CancelEdit { get; set; }

    public int Item { get; }

    public string? Label { get; }
}
