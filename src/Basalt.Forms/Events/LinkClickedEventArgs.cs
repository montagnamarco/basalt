namespace System.Windows.Forms;

public class LinkClickedEventArgs : EventArgs
{
    public LinkClickedEventArgs(string? linkText)
    {
        LinkText = linkText;
    }

    public LinkClickedEventArgs(string? linkText, int linkStart, int linkLength)
    {
        LinkText = linkText;
        LinkStart = linkStart;
        LinkLength = linkLength;
    }

    public int LinkLength { get; }

    public int LinkStart { get; }

    public string? LinkText { get; }
}
