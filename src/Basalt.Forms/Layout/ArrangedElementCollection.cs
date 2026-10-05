using System.Collections;

namespace System.Windows.Forms.Layout;

/// <summary>
/// The children a layout engine arranges, in z-order: index 0 is the one on
/// top. Read-only from outside; <see cref="Control.ControlCollection"/>
/// adds the ways to change it.
/// </summary>
public class ArrangedElementCollection : IList
{
    private protected readonly List<IArrangedElement> Items;

    private protected ArrangedElementCollection()
    {
        Items = [];
    }

    internal ArrangedElementCollection(List<IArrangedElement> items)
    {
        Items = items;
    }

    public virtual int Count => Items.Count;

    /// <summary>False: the collection itself is read-only, but the controls' collection that derives from it is not.</summary>
    public virtual bool IsReadOnly => false;

    public void CopyTo(Array array, int index) => ((ICollection)Items).CopyTo(array, index);

    /// <summary>Equal when both hold the same elements in the same order.</summary>
    public override bool Equals(object? obj) => obj is ArrangedElementCollection other && Items.SequenceEqual(other.Items);

    public override int GetHashCode() => base.GetHashCode();

    public virtual IEnumerator GetEnumerator() => Items.ToArray().GetEnumerator();

    internal IArrangedElement ElementAt(int index) => Items[index];

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => IsReadOnly;

    object? IList.this[int index]
    {
        get => Items[index];
        set => throw new NotSupportedException();
    }

    int IList.Add(object? value) => AddFromList(value);

    void IList.Clear() => ClearFromList();

    bool IList.Contains(object? value) => value is IArrangedElement element && Items.Contains(element);

    int IList.IndexOf(object? value) => value is IArrangedElement element ? Items.IndexOf(element) : -1;

    void IList.Insert(int index, object? value) => throw new NotSupportedException();

    void IList.Remove(object? value) => RemoveFromList(value);

    void IList.RemoveAt(int index) => RemoveFromList(Items[index]);

    // What the non-generic list does in a collection that can change; this one cannot.
    private protected virtual int AddFromList(object? value) => throw new NotSupportedException();

    private protected virtual void RemoveFromList(object? value) => throw new NotSupportedException();

    private protected virtual void ClearFromList() => throw new NotSupportedException();
}
