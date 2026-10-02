using System.Collections;

namespace System.Windows.Forms;

/// <summary>
/// WinForms lists a DOM window's frames in an HtmlWindowCollection. Basalt
/// has no HTML engine and so no frames to enumerate: nothing in this
/// assembly calls the constructor below, matching WinForms, where this
/// type has no public one either. Every member still exists, for source
/// compatibility, and refuses the moment code reaches past the null checks
/// that would normally stop it first.
/// </summary>
public class HtmlWindowCollection : ICollection, IEnumerable
{
    private const string NoDom = "There is no HTML DOM behind this control: HtmlWindowCollection has nothing to enumerate.";

    internal HtmlWindowCollection()
    {
    }

    public int Count => throw new PlatformNotSupportedException(NoDom);

    public HtmlWindow this[int index] => throw new PlatformNotSupportedException(NoDom);

    public HtmlWindow? this[string windowId] => throw new PlatformNotSupportedException(NoDom);

    public IEnumerator GetEnumerator() => throw new PlatformNotSupportedException(NoDom);

    bool ICollection.IsSynchronized => throw new PlatformNotSupportedException(NoDom);

    object ICollection.SyncRoot => throw new PlatformNotSupportedException(NoDom);

    void ICollection.CopyTo(Array array, int index) => throw new PlatformNotSupportedException(NoDom);
}
