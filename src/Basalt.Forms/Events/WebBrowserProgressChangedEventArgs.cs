namespace System.Windows.Forms;

public class WebBrowserProgressChangedEventArgs : EventArgs
{
    public WebBrowserProgressChangedEventArgs(long currentProgress, long maximumProgress)
    {
        CurrentProgress = currentProgress;
        MaximumProgress = maximumProgress;
    }

    public long CurrentProgress { get; }

    public long MaximumProgress { get; }
}
