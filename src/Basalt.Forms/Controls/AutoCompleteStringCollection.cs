using System.Collections;
using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// The strings <see cref="TextBox.AutoCompleteCustomSource"/> and
/// <see cref="ComboBox.AutoCompleteCustomSource"/> suggest from: a plain list
/// of candidates, in no particular order, that raises
/// <see cref="CollectionChanged"/> on every edit so a control already showing
/// a suggestion list can refresh it.
/// </summary>
public class AutoCompleteStringCollection : ICollection, IEnumerable, IList
{
    private readonly List<string> _items = [];

    public event CollectionChangeEventHandler? CollectionChanged;

    public int Count => _items.Count;

    public bool IsReadOnly => false;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public string this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            var removed = _items[index];
            _items[index] = value;
            OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Remove, removed));
            OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Add, value));
        }
    }

    public int Add(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _items.Add(value);
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Add, value));

        return _items.Count - 1;
    }

    public void AddRange(string[] value)
    {
        ArgumentNullException.ThrowIfNull(value);

        foreach (var item in value) Add(item);
    }

    public void Clear()
    {
        if (_items.Count == 0) return;

        _items.Clear();
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Refresh, null));
    }

    public bool Contains(string value) => value is not null && _items.Contains(value);

    public void CopyTo(string[] array, int index) => _items.CopyTo(array, index);

    public IEnumerator GetEnumerator() => _items.GetEnumerator();

    public int IndexOf(string value) => value is null ? -1 : _items.IndexOf(value);

    public void Insert(int index, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _items.Insert(index, value);
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Add, value));
    }

    public void Remove(string value)
    {
        if (value is null || !_items.Remove(value)) return;

        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Remove, value));
    }

    public void RemoveAt(int index)
    {
        var value = _items[index];
        _items.RemoveAt(index);
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Remove, value));
    }

    protected void OnCollectionChanged(CollectionChangeEventArgs e) => CollectionChanged?.Invoke(this, e);

    bool IList.IsFixedSize => false;

    void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    int IList.Add(object? value) => Add((string)value!);

    bool IList.Contains(object? value) => value is string s && Contains(s);

    int IList.IndexOf(object? value) => value is string s ? IndexOf(s) : -1;

    void IList.Insert(int index, object? value) => Insert(index, (string)value!);

    void IList.Remove(object? value)
    {
        if (value is string s) Remove(s);
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = (string)value!;
    }
}
