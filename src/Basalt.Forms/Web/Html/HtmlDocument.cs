using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// WinForms wraps Internet Explorer's DOM Document in an HtmlDocument.
/// Basalt has no HTML engine: <see cref="WebBrowser.Document"/> always
/// returns null, and nothing in this assembly calls the constructor below -
/// WinForms itself has no public one either. Every member still exists, for
/// source compatibility, and refuses the moment code reaches past the null
/// checks that would normally stop it first.
/// </summary>
public sealed class HtmlDocument
{
    private const string NoDom = "There is no HTML DOM behind this control: HtmlDocument has nothing to wrap.";

    internal HtmlDocument()
    {
    }

    public HtmlElement? ActiveElement => throw new PlatformNotSupportedException(NoDom);

    public Color ActiveLinkColor
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlElementCollection All => throw new PlatformNotSupportedException(NoDom);

    public Color BackColor
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlElement? Body => throw new PlatformNotSupportedException(NoDom);

    public string Cookie
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public string DefaultEncoding => throw new PlatformNotSupportedException(NoDom);

    public object DomDocument => throw new PlatformNotSupportedException(NoDom);

    public string Domain
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public string Encoding
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public bool Focused => throw new PlatformNotSupportedException(NoDom);

    public Color ForeColor
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlElementCollection Forms => throw new PlatformNotSupportedException(NoDom);

    public HtmlElementCollection Images => throw new PlatformNotSupportedException(NoDom);

    public Color LinkColor
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlElementCollection Links => throw new PlatformNotSupportedException(NoDom);

    public bool RightToLeft
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public string Title
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public Uri Url => throw new PlatformNotSupportedException(NoDom);

    public Color VisitedLinkColor
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlWindow Window => throw new PlatformNotSupportedException(NoDom);

    public void AttachEventHandler(string eventName, EventHandler eventHandler) => throw new PlatformNotSupportedException(NoDom);

    public HtmlElement CreateElement(string elementTag) => throw new PlatformNotSupportedException(NoDom);

    public void DetachEventHandler(string eventName, EventHandler eventHandler) => throw new PlatformNotSupportedException(NoDom);

    public override bool Equals(object? obj) => throw new PlatformNotSupportedException(NoDom);

    public void ExecCommand(string command, bool showUI, object? value) => throw new PlatformNotSupportedException(NoDom);

    public void Focus() => throw new PlatformNotSupportedException(NoDom);

    public HtmlElement? GetElementById(string id) => throw new PlatformNotSupportedException(NoDom);

    public HtmlElement? GetElementFromPoint(Point point) => throw new PlatformNotSupportedException(NoDom);

    public HtmlElementCollection GetElementsByTagName(string tagName) => throw new PlatformNotSupportedException(NoDom);

    public override int GetHashCode() => throw new PlatformNotSupportedException(NoDom);

    public object? InvokeScript(string scriptName) => throw new PlatformNotSupportedException(NoDom);

    public object? InvokeScript(string scriptName, object?[]? args) => throw new PlatformNotSupportedException(NoDom);

    public HtmlDocument OpenNew(bool replaceInHistory) => throw new PlatformNotSupportedException(NoDom);

    public void Write(string text) => throw new PlatformNotSupportedException(NoDom);

    public static bool operator ==(HtmlDocument? left, HtmlDocument? right) => throw new PlatformNotSupportedException(NoDom);

    public static bool operator !=(HtmlDocument? left, HtmlDocument? right) => throw new PlatformNotSupportedException(NoDom);

    public event HtmlElementEventHandler? Click
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? ContextMenuShowing
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? Focusing
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? LosingFocus
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? MouseDown
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? MouseLeave
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? MouseMove
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? MouseOver
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? MouseUp
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? Stop
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }
}
