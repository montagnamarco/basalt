namespace System.Windows.Forms;

public class WebBrowserNavigatedEventArgs : EventArgs
{
    public WebBrowserNavigatedEventArgs(Uri? url)
    {
        Url = url;
    }

    public Uri? Url { get; }
}
