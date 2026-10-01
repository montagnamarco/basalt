namespace System.Windows.Forms;

/// <summary>The row styles of a <see cref="TableLayoutPanel"/>.</summary>
public class TableLayoutRowStyleCollection : TableLayoutStyleCollection
{
    internal TableLayoutRowStyleCollection()
    {
    }

    public new RowStyle this[int index]
    {
        get => (RowStyle)Items[index];
        set => SetAt(index, value);
    }

    public int Add(RowStyle rowStyle) => AddCore(rowStyle);

    public bool Contains(RowStyle rowStyle) => Items.Contains(rowStyle);

    public int IndexOf(RowStyle rowStyle) => Items.IndexOf(rowStyle);

    public void Insert(int index, RowStyle rowStyle) => InsertCore(index, rowStyle);

    public void Remove(RowStyle rowStyle) => RemoveCore(rowStyle);
}
