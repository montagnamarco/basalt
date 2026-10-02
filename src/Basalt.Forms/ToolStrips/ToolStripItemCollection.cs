using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>The items of a <see cref="ToolStrip"/>, in the order they are drawn and tabbed through.</summary>
public class ToolStripItemCollection : ArrangedElementCollection
{
    private readonly ToolStrip _owner;

    /// <summary>A view of items another strip owns (what a strip displays), which must not take them over.</summary>
    private readonly bool _isView;

    public ToolStripItemCollection(ToolStrip owner, ToolStripItem[] value)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(value);

        _owner = owner;
        AddRange(value);
    }

    private ToolStripItemCollection(ToolStrip owner, IEnumerable<ToolStripItem> shown)
    {
        _owner = owner;
        _isView = true;
        Items.AddRange(shown);
    }

    /// <summary>A read-only list of items shown on a strip without changing their owner, as the overflow drop-down shows its strip's items.</summary>
    internal static ToolStripItemCollection CreateView(ToolStrip owner, IEnumerable<ToolStripItem> shown) => new(owner, shown);

    public override bool IsReadOnly => _isView;

    private void ThrowIfView()
    {
        if (_isView) throw new NotSupportedException("The collection is read-only.");
    }

    public virtual ToolStripItem this[int index]
    {
        get
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index), index, $"Index {index} is out of range.");

            return (ToolStripItem)Items[index];
        }
    }

    public virtual ToolStripItem? this[string key]
    {
        get
        {
            var index = IndexOfKey(key);

            return index >= 0 ? this[index] : null;
        }
    }

    public ToolStripItem Add(string? text) => AddDefault(text, null, null);

    public ToolStripItem Add(Image? image) => AddDefault(null, image, null);

    public ToolStripItem Add(string? text, Image? image) => AddDefault(text, image, null);

    public ToolStripItem Add(string? text, Image? image, EventHandler? onClick) => AddDefault(text, image, onClick);

    private ToolStripItem AddDefault(string? text, Image? image, EventHandler? onClick)
    {
        var item = _owner.CreateDefaultItem(text, image, onClick);
        Add(item);

        return item;
    }

    public int Add(ToolStripItem value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfView();

        value.Owner?.Items.Remove(value);

        Items.Add(value);
        value.SetParentInternal(_owner);

        _owner.DoLayout(value, PropertyNames.Parent);
        _owner.OnItemAdded(new ToolStripItemEventArgs(value));
        _owner.Invalidate(value.Bounds, true);

        return IndexOf(value);
    }

    private protected override int AddFromList(object? value)
    {
        if (value is not ToolStripItem item) throw new ArgumentException("Value must be a ToolStripItem.", nameof(value));

        return Add(item);
    }

    public void AddRange(params ToolStripItem[] toolStripItems)
    {
        ArgumentNullException.ThrowIfNull(toolStripItems);

        foreach (var item in toolStripItems) Add(item);
    }

    public void AddRange(ToolStripItemCollection toolStripItems)
    {
        ArgumentNullException.ThrowIfNull(toolStripItems);

        foreach (ToolStripItem item in toolStripItems) Add(item);
    }

    public virtual void Clear()
    {
        ThrowIfView();
        foreach (var item in Items.ToArray())
        {
            Remove((ToolStripItem)item);
        }
    }

    private protected override void ClearFromList() => Clear();

    public bool Contains(ToolStripItem value) => value is not null && Items.Contains(value);

    public virtual bool ContainsKey(string? key) => IndexOfKey(key) >= 0;

    public void CopyTo(ToolStripItem[] array, int index) => Items.CopyTo(array, index);

    public ToolStripItem[] Find(string key, bool searchAllChildren)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key), "Key cannot be empty.");

        var found = new List<ToolStripItem>();

        foreach (ToolStripItem item in this)
        {
            if (string.Equals(item.Name, key, StringComparison.OrdinalIgnoreCase)) found.Add(item);
        }

        return [.. found];
    }

    public int IndexOf(ToolStripItem value) => value is null ? -1 : Items.IndexOf(value);

    public virtual int IndexOfKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return -1;

        for (var i = 0; i < Count; i++)
        {
            if (string.Equals(this[i].Name, key, StringComparison.OrdinalIgnoreCase)) return i;
        }

        return -1;
    }

    public void Insert(int index, ToolStripItem value)
    {
        ThrowIfView();
        ArgumentNullException.ThrowIfNull(value);

        value.Owner?.Items.Remove(value);

        Items.Insert(index, value);
        value.SetParentInternal(_owner);

        _owner.DoLayout(value, PropertyNames.Parent);
        _owner.OnItemAdded(new ToolStripItemEventArgs(value));
        _owner.Invalidate(value.Bounds, true);
    }

    public void Remove(ToolStripItem value)
    {
        ThrowIfView();
        ArgumentNullException.ThrowIfNull(value);

        if (!Items.Remove(value)) return;

        value.SetParentInternal(null);
        _owner.OnItemRemoved(new ToolStripItemEventArgs(value));
        _owner.DoLayout(value, PropertyNames.Parent);
    }

    private protected override void RemoveFromList(object? value)
    {
        if (value is ToolStripItem item) Remove(item);
    }

    public void RemoveAt(int index) => Remove(this[index]);

    public virtual void RemoveByKey(string? key)
    {
        var index = IndexOfKey(key);
        if (index >= 0) RemoveAt(index);
    }
}
