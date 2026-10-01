namespace System.Windows.Forms;

public class WebBrowserDocumentCompletedEventArgs : EventArgs
{
    public WebBrowserDocumentCompletedEventArgs(Uri? url)
    {
        Url = url;
    }

    public Uri? Url { get; }
}
