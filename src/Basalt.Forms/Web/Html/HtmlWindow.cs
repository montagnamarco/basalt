using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Compatibility surface for Internet Explorer's DOM Window. The native
/// WebView has an HTML engine, but window and frame operations still have no
/// synchronous wrapper here.
/// </summary>
public sealed class HtmlWindow
{
    private const string NoDom = "This Internet Explorer window feature is not available in the native WebView.";

    internal HtmlWindow()
    {
    }

    public HtmlDocument? Document => throw new PlatformNotSupportedException(NoDom);

    public object DomWindow => throw new PlatformNotSupportedException(NoDom);

    public HtmlWindowCollection Frames => throw new PlatformNotSupportedException(NoDom);

    public HtmlHistory History => throw new PlatformNotSupportedException(NoDom);

    public bool IsClosed => throw new PlatformNotSupportedException(NoDom);

    public string Name
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlWindow? Opener => throw new PlatformNotSupportedException(NoDom);

    public HtmlWindow? Parent => throw new PlatformNotSupportedException(NoDom);

    public Point Position => throw new PlatformNotSupportedException(NoDom);

    public Size Size
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public string StatusBarText
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public Uri Url => throw new PlatformNotSupportedException(NoDom);

    public HtmlElement? WindowFrameElement => throw new PlatformNotSupportedException(NoDom);

    public void Alert(string message) => throw new PlatformNotSupportedException(NoDom);

    public void AttachEventHandler(string eventName, EventHandler eventHandler) => throw new PlatformNotSupportedException(NoDom);

    public void Close() => throw new PlatformNotSupportedException(NoDom);

    public bool Confirm(string message) => throw new PlatformNotSupportedException(NoDom);

    public void DetachEventHandler(string eventName, EventHandler eventHandler) => throw new PlatformNotSupportedException(NoDom);

    public override bool Equals(object? obj) => throw new PlatformNotSupportedException(NoDom);

    public void Focus() => throw new PlatformNotSupportedException(NoDom);

    public override int GetHashCode() => throw new PlatformNotSupportedException(NoDom);

    public void MoveTo(Point point) => throw new PlatformNotSupportedException(NoDom);

    public void MoveTo(int x, int y) => throw new PlatformNotSupportedException(NoDom);

    public void Navigate(string urlString) => throw new PlatformNotSupportedException(NoDom);

    public void Navigate(Uri url) => throw new PlatformNotSupportedException(NoDom);

    public HtmlWindow? Open(string urlString, string target, string windowOptions, bool replaceEntry) =>
        throw new PlatformNotSupportedException(NoDom);

    public HtmlWindow? Open(Uri url, string target, string windowOptions, bool replaceEntry) =>
        throw new PlatformNotSupportedException(NoDom);

    public HtmlWindow? OpenNew(string urlString, string windowOptions) => throw new PlatformNotSupportedException(NoDom);

    public HtmlWindow? OpenNew(Uri url, string windowOptions) => throw new PlatformNotSupportedException(NoDom);

    public string? Prompt(string message, string defaultInputValue) => throw new PlatformNotSupportedException(NoDom);

    public void RemoveFocus() => throw new PlatformNotSupportedException(NoDom);

    public void ResizeTo(Size size) => throw new PlatformNotSupportedException(NoDom);

    public void ResizeTo(int width, int height) => throw new PlatformNotSupportedException(NoDom);

    public void ScrollTo(Point point) => throw new PlatformNotSupportedException(NoDom);

    public void ScrollTo(int x, int y) => throw new PlatformNotSupportedException(NoDom);

    public static bool operator ==(HtmlWindow? left, HtmlWindow? right) => throw new PlatformNotSupportedException(NoDom);

    public static bool operator !=(HtmlWindow? left, HtmlWindow? right) => throw new PlatformNotSupportedException(NoDom);

    public event HtmlElementErrorEventHandler? Error
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? GotFocus
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? Load
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? LostFocus
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? Resize
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? Scroll
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? Unload
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }
}
