using System.ComponentModel;

namespace System.Windows.Forms;

public class WebBrowserNavigatingEventArgs : CancelEventArgs
{
    public WebBrowserNavigatingEventArgs(Uri? url, string? targetFrameName)
    {
        Url = url;
        TargetFrameName = targetFrameName;
    }

    public string? TargetFrameName { get; }

    public Uri? Url { get; }
}
