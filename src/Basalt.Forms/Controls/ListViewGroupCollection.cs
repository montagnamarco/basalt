using System.Collections;

namespace System.Windows.Forms;

/// <summary>The ListView's <see cref="ListView.Groups"/>: the headers Items can be clustered under.</summary>
public class ListViewGroupCollection : IList
{
    private readonly ListView _owner;

    internal ListViewGroupCollection(ListView owner)
    {
        _owner = owner;
    }

    private List<ListViewGroup> Groups => _owner.GroupsCore;

    public int Count => Groups.Count;

    public ListViewGroup this[int index]
    {
        get
        {
            if (index < 0 || index >= Groups.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return Groups[index];
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (index < 0 || index >= Groups.Count) throw new ArgumentOutOfRangeException(nameof(index));

            Groups[index].SetListView(null);
            Groups[index] = value;
            value.SetListView(_owner);
            _owner.InvalidateHeader();
        }
    }

    public ListViewGroup? this[string key]
    {
        get => Groups.Find(g => string.Equals(g.Name, key, StringComparison.OrdinalIgnoreCase));
        set
        {
            var index = Groups.FindIndex(g => string.Equals(g.Name, key, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && value is not null) this[index] = value;
        }
    }

    public int Add(ListViewGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        Groups.Add(group);
        group.SetListView(_owner);
        _owner.InvalidateHeader();

        return Groups.Count - 1;
    }

    public ListViewGroup Add(string? key, string? headerText)
    {
        var group = new ListViewGroup(key, headerText);
        Add(group);

        return group;
    }

    public void AddRange(params ListViewGroup[] groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        foreach (var group in groups) Add(group);
    }

    public void AddRange(ListViewGroupCollection groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        foreach (ListViewGroup group in groups) Add(group);
    }

    public void Clear()
    {
        foreach (var group in Groups) group.SetListView(null);
        Groups.Clear();
        _owner.InvalidateHeader();
    }

    public bool Contains(ListViewGroup value) => Groups.Contains(value);

    public void CopyTo(Array array, int index) => ((ICollection)Groups).CopyTo(array, index);

    public IEnumerator GetEnumerator() => Groups.GetEnumerator();

    public int IndexOf(ListViewGroup value) => Groups.IndexOf(value);

    public void Insert(int index, ListViewGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        Groups.Insert(index, group);
        group.SetListView(_owner);
        _owner.InvalidateHeader();
    }

    public void Remove(ListViewGroup group)
    {
        if (Groups.Remove(group)) group.SetListView(null);
        _owner.InvalidateHeader();
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= Groups.Count) throw new ArgumentOutOfRangeException(nameof(index));

        Groups[index].SetListView(null);
        Groups.RemoveAt(index);
        _owner.InvalidateHeader();
    }

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => false;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = (ListViewGroup)value!;
    }

    int IList.Add(object? value) => Add((ListViewGroup)value!);

    bool IList.Contains(object? value) => value is ListViewGroup group && Contains(group);

    int IList.IndexOf(object? value) => value is ListViewGroup group ? IndexOf(group) : -1;

    void IList.Insert(int index, object? value) => Insert(index, (ListViewGroup)value!);

    void IList.Remove(object? value)
    {
        if (value is ListViewGroup group) Remove(group);
    }
}
