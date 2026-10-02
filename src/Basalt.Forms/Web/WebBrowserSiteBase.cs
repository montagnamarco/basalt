namespace System.Windows.Forms;

/// <summary>
/// The site a <see cref="WebBrowserBase"/> would give the ActiveX browser
/// control it hosts, so the control can ask its container for ambient
/// properties and in-place UI activation. There is no ActiveX control here
/// - Basalt draws the browser's blank page itself - so this is only the
/// disposable handle WebBrowserBase keeps for the lifetime of its site and
/// releases with its own Dispose.
/// </summary>
public class WebBrowserSiteBase : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Never public in real WinForms either: a site only ever comes from
    /// <see cref="WebBrowserBase.CreateWebBrowserSiteBase"/>, which is how a
    /// derived host plugs in its own site type.
    /// </summary>
    internal WebBrowserSiteBase(WebBrowserBase host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Host = host;
    }

    internal WebBrowserBase Host { get; }

    internal bool IsDisposed => _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        _disposed = true;
    }
}
