namespace System.Windows.Forms;

/// <summary>
/// WinForms wraps Internet Explorer's DOM History in an HtmlHistory.
/// Basalt has no HTML engine and so no navigation history to wrap at the
/// DOM level - WebBrowser keeps its own, in <see cref="WebBrowser"/>
/// itself. Nothing in this assembly calls the constructor below, matching
/// WinForms, where this type has no public one either. Every member still
/// exists, for source compatibility, and refuses the moment code reaches
/// past the null checks that would normally stop it first.
/// </summary>
public sealed class HtmlHistory : IDisposable
{
    private const string NoDom = "There is no HTML DOM behind this control: HtmlHistory has nothing to wrap.";

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
