using System.Collections;

namespace System.Windows.Forms;

/// <summary>The column or row styles of a <see cref="TableLayoutPanel"/>, in order.</summary>
public abstract class TableLayoutStyleCollection : ICollection, IEnumerable, IList
{
    private protected readonly List<TableLayoutStyle> Items = [];

    internal TableLayoutStyleCollection()
    {
    }

    /// <summary>The panel to lay out again, and to tell each style about, once it is set.</summary>
    internal TableLayoutPanel? Owner { get; set; }

    public int Count => Items.Count;

    public TableLayoutStyle this[int index]
    {
        get => Items[index];
        set => SetAt(index, value);
    }

    public int Add(TableLayoutStyle style) => AddCore(style);

    public void Clear()
    {
        foreach (var style in Items) style.Owner = null;
        Items.Clear();
        Owner?.PerformLayout();
    }

    public void RemoveAt(int index)
    {
        Items[index].Owner = null;
        Items.RemoveAt(index);
        Owner?.PerformLayout();
    }

    private protected int AddCore(TableLayoutStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        style.Owner = Owner;
        Items.Add(style);
        Owner?.PerformLayout();

        return Items.Count - 1;
    }

    private protected void InsertCore(int index, TableLayoutStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        style.Owner = Owner;
        Items.Insert(index, style);
        Owner?.PerformLayout();
    }

    private protected void RemoveCore(TableLayoutStyle? style)
    {
        if (style is null || !Items.Remove(style)) return;

        style.Owner = null;
        Owner?.PerformLayout();
    }

    private protected void SetAt(int index, TableLayoutStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        Items[index].Owner = null;
        Items[index] = style;
        style.Owner = Owner;
        Owner?.PerformLayout();
    }

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    void ICollection.CopyTo(Array array, int index) => ((ICollection)Items).CopyTo(array, index);

    IEnumerator IEnumerable.GetEnumerator() => Items.GetEnumerator();

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => false;

    object? IList.this[int index]
    {
        get => Items[index];
        set => SetAt(index, (TableLayoutStyle)value!);
    }

    int IList.Add(object? value)
    {
        AddCore((TableLayoutStyle)value!);

        return Items.Count - 1;
    }

    void IList.Clear() => Clear();

    bool IList.Contains(object? value) => value is TableLayoutStyle style && Items.Contains(style);

    int IList.IndexOf(object? value) => value is TableLayoutStyle style ? Items.IndexOf(style) : -1;

    void IList.Insert(int index, object? value) => InsertCore(index, (TableLayoutStyle)value!);

    void IList.Remove(object? value) => RemoveCore(value as TableLayoutStyle);

    void IList.RemoveAt(int index) => RemoveAt(index);
}
