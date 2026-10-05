using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A node in the current native WebView document. IE COM objects and DOM
/// event sinks remain unavailable.
/// </summary>
public sealed class HtmlElement
{
    private const string NoDom = "This Internet Explorer element feature is not available in the native WebView.";
    private readonly NativeDomBridge? _bridge;
    private readonly int _id;
    private readonly HtmlDocument? _document;
    private NativeDomBridge Bridge => _bridge ?? throw new PlatformNotSupportedException(NoDom);
    private string Node => Bridge.Node(_id);

    internal HtmlElement()
    {
    }

    internal HtmlElement(NativeDomBridge bridge, int id, HtmlDocument document)
    {
        _bridge = bridge;
        _id = id;
        _document = document;
    }

    public HtmlElementCollection All => new(Bridge, Document, $"{Node}.querySelectorAll('*')");

    public bool CanHaveChildren => throw new PlatformNotSupportedException(NoDom);

    public HtmlElementCollection Children => new(Bridge, Document, $"{Node}.children");

    public Rectangle ClientRectangle => throw new PlatformNotSupportedException(NoDom);

    public HtmlDocument Document => _document ?? throw new PlatformNotSupportedException(NoDom);

    public object DomElement => throw new PlatformNotSupportedException(NoDom);

    public bool Enabled
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlElement FirstChild => throw new PlatformNotSupportedException(NoDom);

    public string Id
    {
        get => Bridge.String($"{Node}.id") ?? string.Empty;
        set => Bridge.Evaluate($"({Node}.id={NativeDomBridge.Literal(value)})");
    }

    public string InnerHtml
    {
        get => Bridge.String($"{Node}.innerHTML") ?? string.Empty;
        set => Bridge.Evaluate($"({Node}.innerHTML={NativeDomBridge.Literal(value)})");
    }

    public string InnerText
    {
        get => Bridge.String($"{Node}.innerText") ?? string.Empty;
        set => Bridge.Evaluate($"({Node}.innerText={NativeDomBridge.Literal(value)})");
    }

    public string Name
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlElement NextSibling => throw new PlatformNotSupportedException(NoDom);

    public HtmlElement OffsetParent => throw new PlatformNotSupportedException(NoDom);

    public Rectangle OffsetRectangle => throw new PlatformNotSupportedException(NoDom);

    public string OuterHtml
    {
        get => Bridge.String($"{Node}.outerHTML") ?? string.Empty;
        set => Bridge.Evaluate($"({Node}.outerHTML={NativeDomBridge.Literal(value)})");
    }

    public string OuterText
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlElement Parent => throw new PlatformNotSupportedException(NoDom);

    public int ScrollLeft
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public Rectangle ScrollRectangle => throw new PlatformNotSupportedException(NoDom);

    public int ScrollTop
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public string Style
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public short TabIndex
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public string TagName => Bridge.String($"{Node}.tagName") ?? string.Empty;

    public HtmlElement AppendChild(HtmlElement newElement) => throw new PlatformNotSupportedException(NoDom);

    public void AttachEventHandler(string eventName, EventHandler eventHandler) => throw new PlatformNotSupportedException(NoDom);

    public void DetachEventHandler(string eventName, EventHandler eventHandler) => throw new PlatformNotSupportedException(NoDom);

    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public void Focus() => throw new PlatformNotSupportedException(NoDom);

    public string? GetAttribute(string attributeName) =>
        Bridge.String($"{Node}.getAttribute({NativeDomBridge.Literal(attributeName)})");

    public HtmlElementCollection GetElementsByTagName(string tagName) =>
        new(Bridge, Document, $"{Node}.getElementsByTagName({NativeDomBridge.Literal(tagName)})");

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    public HtmlElement InsertAdjacentElement(HtmlElementInsertionOrientation orientation, HtmlElement insertedElement) =>
        throw new PlatformNotSupportedException(NoDom);

    public object? InvokeMember(string methodName) => throw new PlatformNotSupportedException(NoDom);

    public object? InvokeMember(string methodName, params object[] parameter) => throw new PlatformNotSupportedException(NoDom);

    public void RaiseEvent(string eventName) => throw new PlatformNotSupportedException(NoDom);

    public void RemoveFocus() => throw new PlatformNotSupportedException(NoDom);

    public void ScrollIntoView(bool alignWithTop) => throw new PlatformNotSupportedException(NoDom);

    public void SetAttribute(string attributeName, string value) =>
        Bridge.Evaluate($"(()=>{{{Node}.setAttribute({NativeDomBridge.Literal(attributeName)},{NativeDomBridge.Literal(value)});return null}})()");

    public static bool operator ==(HtmlElement? left, HtmlElement? right) => ReferenceEquals(left, right);

    public static bool operator !=(HtmlElement? left, HtmlElement? right) => !ReferenceEquals(left, right);

    public event HtmlElementEventHandler? Click
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? DoubleClick
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? Drag
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? DragEnd
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? DragLeave
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? DragOver
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? Focusing
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? GotFocus
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? KeyDown
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? KeyPress
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? KeyUp
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? LosingFocus
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? LostFocus
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? MouseDown
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? MouseEnter
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
}
