namespace System.Windows.Forms;

/// <summary>
/// Compatibility surface for Internet Explorer's DOM History. The native
/// WebView has browser history through <see cref="WebBrowser"/>, while this
/// DOM-level wrapper is not implemented.
/// </summary>
public sealed class HtmlHistory : IDisposable
{
    private const string NoDom = "This Internet Explorer history feature is not available in the native WebView.";

    internal HtmlHistory()
    {
    }

    public object DomHistory => throw new PlatformNotSupportedException(NoDom);

    public int Length => throw new PlatformNotSupportedException(NoDom);

    public void Back(int numberBack) => throw new PlatformNotSupportedException(NoDom);

    /// <summary>
    /// Disposing must never throw - a `using` block or a finalizer calls it
    /// unconditionally - and there is nothing here to release anyway.
    /// </summary>
    public void Dispose()
    {
    }

    public void Forward(int numberForward) => throw new PlatformNotSupportedException(NoDom);

    public void Go(int relativePosition) => throw new PlatformNotSupportedException(NoDom);

    public void Go(string urlString) => throw new PlatformNotSupportedException(NoDom);

    public void Go(Uri url) => throw new PlatformNotSupportedException(NoDom);
}
