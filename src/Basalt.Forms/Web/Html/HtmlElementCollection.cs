using System.Collections;

namespace System.Windows.Forms;

/// <summary>
/// WinForms lists a DOM node's children, or the results of a tag-name
/// search, in an HtmlElementCollection. Basalt has no HTML engine and so no
/// DOM to enumerate: nothing in this assembly calls the constructor below,
/// matching WinForms, where this type has no public one either. Every
/// member still exists, for source compatibility, and refuses the moment
/// code reaches past the null checks that would normally stop it first.
/// </summary>
public sealed class HtmlElementCollection : ICollection, IEnumerable
{
    private const string NoDom = "There is no HTML DOM behind this control: HtmlElementCollection has nothing to enumerate.";

    internal HtmlElementCollection()
    {
    }

    public int Count => throw new PlatformNotSupportedException(NoDom);

    public HtmlElement this[int index] => throw new PlatformNotSupportedException(NoDom);

    public HtmlElement? this[string elementId] => throw new PlatformNotSupportedException(NoDom);

    public HtmlElementCollection GetElementsByName(string name) => throw new PlatformNotSupportedException(NoDom);

    public IEnumerator GetEnumerator() => throw new PlatformNotSupportedException(NoDom);

    bool ICollection.IsSynchronized => throw new PlatformNotSupportedException(NoDom);

    object ICollection.SyncRoot => throw new PlatformNotSupportedException(NoDom);

    void ICollection.CopyTo(Array array, int index) => throw new PlatformNotSupportedException(NoDom);
}
