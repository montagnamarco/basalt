using System.Collections;

namespace System.Windows.Forms;

/// <summary>
/// A live query over the current native document. The collection becomes
/// invalid when its browser navigates to another document.
/// </summary>
public sealed class HtmlElementCollection : ICollection, IEnumerable
{
    private const string NoDom = "This Internet Explorer collection feature is not available in the native WebView.";
    private readonly NativeDomBridge? _bridge;
    private readonly HtmlDocument? _document;
    private readonly string? _expression;
    private NativeDomBridge Bridge => _bridge ?? throw new PlatformNotSupportedException(NoDom);

    internal HtmlElementCollection()
    {
    }

    internal HtmlElementCollection(NativeDomBridge bridge, HtmlDocument document, string expression)
    {
        _bridge = bridge;
        _document = document;
        _expression = expression;
    }

    public int Count => Bridge.Evaluate($"({_expression}).length").GetInt32();

    public HtmlElement this[int index]
    {
        get
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            var id = Bridge.Register($"({_expression})[{index}]");
            return new HtmlElement(Bridge, id, _document!);
        }
    }

    public HtmlElement? this[string elementId]
    {
        get
        {
            var id = Bridge.Register($"Array.from({_expression}).find(n=>n.id==={NativeDomBridge.Literal(elementId)})");
            return id == 0 ? null : new HtmlElement(Bridge, id, _document!);
        }
    }

    public HtmlElementCollection GetElementsByName(string name) =>
        new(Bridge, _document!, $"Array.from({_expression}).filter(n=>n.getAttribute('name')==={NativeDomBridge.Literal(name)})");

    public IEnumerator GetEnumerator()
    {
        var count = Count;
        for (var i = 0; i < count; i++) yield return this[i];
    }

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        foreach (HtmlElement element in this) array.SetValue(element, index++);
    }
}
