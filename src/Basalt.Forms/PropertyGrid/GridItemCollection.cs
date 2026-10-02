using System.Collections;

namespace System.Windows.Forms;

/// <summary>A GridItem's children: read-only from the outside, the grid itself builds it with GridEntryCollection.</summary>
public class GridItemCollection : ICollection, IEnumerable
{
    public static GridItemCollection Empty = new([]);

    private readonly GridItem[] _items;

    internal GridItemCollection(GridItem[] items)
    {
        _items = items;
    }

    public int Count => _items.Length;

    public GridItem this[int index] => _items[index];

    /// <summary>The first item whose Label matches, or null; a category and one of its properties can share no name, so this is unambiguous.</summary>
    public GridItem? this[string label]
    {
        get
        {
            foreach (var item in _items)
            {
                if (item.Label == label) return item;
            }

            return null;
        }
    }

    public IEnumerator GetEnumerator() => _items.GetEnumerator();

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    void ICollection.CopyTo(Array array, int index) => _items.CopyTo(array, index);
}
