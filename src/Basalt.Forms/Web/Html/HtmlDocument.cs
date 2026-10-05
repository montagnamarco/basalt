using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A bounded facade over the current native WebView document. IE COM objects
/// and DOM event sinks remain unavailable.
/// </summary>
public sealed class HtmlDocument
{
    private const string NoDom = "This Internet Explorer DOM feature is not available in the native WebView.";
    private readonly NativeDomBridge? _bridge;

    private NativeDomBridge Bridge => _bridge ?? throw new PlatformNotSupportedException(NoDom);

    internal HtmlDocument()
    {
    }

    internal HtmlDocument(NativeDomBridge bridge) => _bridge = bridge;

    public HtmlElement? ActiveElement => throw new PlatformNotSupportedException(NoDom);

    public Color ActiveLinkColor
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlElementCollection All => new(Bridge, this, "document.querySelectorAll('*')");

    public Color BackColor
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlElement? Body => Element("document.body");

    public string Cookie
    {
        get => Bridge.String("document.cookie") ?? string.Empty;
        set => Bridge.Evaluate($"(document.cookie={NativeDomBridge.Literal(value)})");
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

    public HtmlElementCollection Forms => new(Bridge, this, "document.forms");

    public HtmlElementCollection Images => new(Bridge, this, "document.images");

    public Color LinkColor
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlElementCollection Links => new(Bridge, this, "document.links");

    public bool RightToLeft
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public string Title
    {
        get => Bridge.String("document.title") ?? string.Empty;
        set => Bridge.Evaluate($"(document.title={NativeDomBridge.Literal(value)})");
    }

    public Uri Url => new(Bridge.String("document.URL") ?? "about:blank");

    public Color VisitedLinkColor
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlWindow Window => throw new PlatformNotSupportedException(NoDom);

    public void AttachEventHandler(string eventName, EventHandler eventHandler) => throw new PlatformNotSupportedException(NoDom);

    public HtmlElement CreateElement(string elementTag) => throw new PlatformNotSupportedException(NoDom);

    public void DetachEventHandler(string eventName, EventHandler eventHandler) => throw new PlatformNotSupportedException(NoDom);

    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public void ExecCommand(string command, bool showUI, object? value) => throw new PlatformNotSupportedException(NoDom);

    public void Focus() => throw new PlatformNotSupportedException(NoDom);

    public HtmlElement? GetElementById(string id) => Element($"document.getElementById({NativeDomBridge.Literal(id)})");

    public HtmlElement? GetElementFromPoint(Point point) => throw new PlatformNotSupportedException(NoDom);

    public HtmlElementCollection GetElementsByTagName(string tagName) =>
        new(Bridge, this, $"document.getElementsByTagName({NativeDomBridge.Literal(tagName)})");

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    public object? InvokeScript(string scriptName) => InvokeScript(scriptName, null);

    public object? InvokeScript(string scriptName, object?[]? args)
    {
        var arguments = System.Text.Json.JsonSerializer.Serialize(args ?? []);
        var result = Bridge.Evaluate($"(()=>{{const f=window[{NativeDomBridge.Literal(scriptName)}];return typeof f==='function'?f.apply(window,{arguments}):null}})()");
        return result.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => null,
            System.Text.Json.JsonValueKind.String => result.GetString(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Number => result.GetDouble(),
            _ => result,
        };
    }

    public HtmlDocument OpenNew(bool replaceInHistory) => throw new PlatformNotSupportedException(NoDom);

    public void Write(string text) => throw new PlatformNotSupportedException(NoDom);

    public static bool operator ==(HtmlDocument? left, HtmlDocument? right) => ReferenceEquals(left, right);

    public static bool operator !=(HtmlDocument? left, HtmlDocument? right) => !ReferenceEquals(left, right);

    private HtmlElement? Element(string expression)
    {
        var id = Bridge.Register(expression);
        return id == 0 ? null : new HtmlElement(Bridge, id, this);
    }

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
