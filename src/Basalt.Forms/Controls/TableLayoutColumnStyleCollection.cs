namespace System.Windows.Forms;

/// <summary>The column styles of a <see cref="TableLayoutPanel"/>.</summary>
public class TableLayoutColumnStyleCollection : TableLayoutStyleCollection
{
    internal TableLayoutColumnStyleCollection()
    {
    }

    public new ColumnStyle this[int index]
    {
        get => (ColumnStyle)Items[index];
        set => SetAt(index, value);
    }

    public int Add(ColumnStyle columnStyle) => AddCore(columnStyle);

    public bool Contains(ColumnStyle columnStyle) => Items.Contains(columnStyle);

    public int IndexOf(ColumnStyle columnStyle) => Items.IndexOf(columnStyle);

    public void Insert(int index, ColumnStyle columnStyle) => InsertCore(index, columnStyle);

    public void Remove(ColumnStyle columnStyle) => RemoveCore(columnStyle);
}
