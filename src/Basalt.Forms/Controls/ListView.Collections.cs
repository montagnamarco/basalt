using System.Collections;
using System.ComponentModel;

namespace System.Windows.Forms;

public partial class ListView
{
    // --- Internal bookkeeping shared by the nested collections and by ListViewItem/ColumnHeader/ListViewGroup ---

    internal int IndexOfItem(ListViewItem item) => _items.IndexOf(item);

    /// <summary>The backing list ListViewGroupCollection reorders and ComputeDetailsLayout reads, in Groups order.</summary>
    internal List<ListViewGroup> GroupsCore => _groups;

    internal void InsertItemCore(int index, ListViewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.ListView is not null) throw new ArgumentException("The item already belongs to a ListView.", nameof(item));

        _items.Insert(index, item);
        item.SetListView(this);

        // A pre-checked item joining the list is already checked, as WinForms does not refire ItemCheck for it.
        if (item.Checked) _checkedItems.Add(item);

        if (_sorting != SortOrder.None) Sort();

        UpdateStyles();
        Invalidate();
    }

    internal void RemoveItemCore(ListViewItem item)
    {
        var index = IndexOfItem(item);
        if (index < 0) return;

        // A selected item leaves the selection with it, and that is reported like any other deselection.
        var wasSelected = _selected.Remove(item);
        if (wasSelected) item.SelectedCore = false;

        _items.RemoveAt(index);
        _checkedItems.Remove(item);
        if (ReferenceEquals(_focusedItem, item)) _focusedItem = null;

        item.SetListView(null);

        UpdateStyles();
        Invalidate();

        if (wasSelected)
        {
            OnItemSelectionChanged(new ListViewItemSelectionChangedEventArgs(item, index, false));
            OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    /// <summary>Selects or deselects one item, enforcing MultiSelect and raising ItemSelectionChanged then SelectedIndexChanged.</summary>
    internal void SetItemSelected(ListViewItem item, bool value)
    {
        if (item.SelectedCore == value) return;

        if (value && !_multiSelect)
        {
            foreach (var other in _selected.ToArray())
            {
                if (ReferenceEquals(other, item)) continue;

                other.SelectedCore = false;
                _selected.Remove(other);
                InvalidateItem(other);
                OnItemSelectionChanged(new ListViewItemSelectionChangedEventArgs(other, IndexOfItem(other), false));
            }
        }

        item.SelectedCore = value;
        if (value) _selected.Add(item);
        else _selected.Remove(item);

        InvalidateItem(item);
        OnItemSelectionChanged(new ListViewItemSelectionChangedEventArgs(item, IndexOfItem(item), value));
        OnSelectedIndexChanged(EventArgs.Empty);
    }

    /// <summary>Applies a requested check-state change through ItemCheck, which may substitute the value actually applied.</summary>
    internal void SetItemChecked(ListViewItem item, bool desiredChecked)
    {
        var current = item.CheckedCore ? CheckState.Checked : CheckState.Unchecked;
        var requested = desiredChecked ? CheckState.Checked : CheckState.Unchecked;

        var args = new ItemCheckEventArgs(IndexOfItem(item), requested, current);
        OnItemCheck(args);

        var finalChecked = args.NewValue == CheckState.Checked;
        if (finalChecked == item.CheckedCore) return;

        item.CheckedCore = finalChecked;
        if (finalChecked) _checkedItems.Add(item);
        else _checkedItems.Remove(item);

        InvalidateItem(item);
        OnItemChecked(new ItemCheckedEventArgs(item));
    }

    internal void InvalidateItem(ListViewItem? item)
    {
        if (!IsHandleCreated) return;

        var index = item is null ? -1 : IndexOfItem(item);
        if (index >= 0) Invalidate(GetItemRect(index));
        else Invalidate();
    }

    internal void InvalidateHeader() => Invalidate();

    /// <summary>A column's Width changed: raises ColumnWidthChanged and relays out everything after it.</summary>
    internal void NotifyColumnWidthChanged(ColumnHeader column)
    {
        OnColumnWidthChanged(new ColumnWidthChangedEventArgs(column.Index));
        UpdateStyles();
        Invalidate();
    }

    /// <summary>Moves a column to a new position in the Columns order, which is also its DisplayIndex.</summary>
    internal void MoveColumnDisplayIndex(ColumnHeader column, int newIndex)
    {
        var oldIndex = _columns.IndexOf(column);
        if (oldIndex < 0) return;

        newIndex = Math.Clamp(newIndex, 0, _columns.Count - 1);
        if (newIndex == oldIndex) return;

        _columns.RemoveAt(oldIndex);
        _columns.Insert(newIndex, column);
        Invalidate();
    }

    /// <summary>A live view of Items scoped to one group, for ListViewGroup.Items.</summary>
    internal ListViewItemCollection ItemsInGroup(ListViewGroup group) => new(this, group);

    // --- Nested collections --------------------------------------------------

    [ListBindable(false)]
    public class ListViewItemCollection : IList
    {
        private readonly ListView? _owner;
        private readonly ListViewGroup? _group;
        private readonly List<ListViewItem> _detached = [];

        internal ListViewItemCollection(ListView owner)
        {
            _owner = owner;
        }

        internal ListViewItemCollection(ListView? owner, ListViewGroup group)
        {
            _owner = owner;
            _group = group;
        }

        /// <summary>The items this collection exposes, in Items order: every one when unfiltered, or only this group's.</summary>
        private List<ListViewItem> Source => _owner is null
            ? _detached
            : _group is null ? _owner._items : _owner._items.Where(i => i.Group == _group).ToList();

        public int Count => Source.Count;

        public bool IsReadOnly => false;

        public virtual ListViewItem this[int index]
        {
            get
            {
                var source = Source;
                if (index < 0 || index >= source.Count) throw new ArgumentOutOfRangeException(nameof(index));
                return source[index];
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (_owner is null) return;

                var realIndex = _owner.IndexOfItem(this[index]);
                _owner.RemoveItemCore(this[index]);
                _owner.InsertItemCore(realIndex, value);
            }
        }

        public virtual ListViewItem? this[string key] => Source.Find(i => string.Equals(i.Name, key, StringComparison.OrdinalIgnoreCase));

        public virtual ListViewItem Add(string? text) => Add(new ListViewItem(text));

        public virtual ListViewItem Add(string? text, int imageIndex) => Add(new ListViewItem(text, imageIndex));

        public virtual ListViewItem Add(ListViewItem value)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (_group is not null) value.Group = _group;

            if (_owner is null)
            {
                _detached.Add(value);
                return value;
            }

            _owner.InsertItemCore(_owner._items.Count, value);
            return value;
        }

        public virtual ListViewItem Add(string? text, string? imageKey) => Add(new ListViewItem(text, imageKey));

        public virtual ListViewItem Add(string? key, string? text, string? imageKey)
        {
            var item = new ListViewItem(text, imageKey) { Name = key ?? string.Empty };
            return Add(item);
        }

        public virtual ListViewItem Add(string? key, string? text, int imageIndex)
        {
            var item = new ListViewItem(text, imageIndex) { Name = key ?? string.Empty };
            return Add(item);
        }

        public void AddRange(params ListViewItem[] items)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (var item in items) Add(item);
        }

        public void AddRange(ListViewItemCollection items)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (ListViewItem item in items) Add(item);
        }

        public virtual void Clear()
        {
            foreach (var item in Source.ToArray()) Remove(item);
        }

        public bool Contains(ListViewItem item) => Source.Contains(item);

        public virtual bool ContainsKey(string? key) => key is not null && this[key] is not null;

        public void CopyTo(Array dest, int index) => ((ICollection)Source).CopyTo(dest, index);

        public ListViewItem[] Find(string key, bool searchAllSubItems)
        {
            var matches = new List<ListViewItem>();
            foreach (var item in Source)
            {
                if (string.Equals(item.Name, key, StringComparison.OrdinalIgnoreCase)) matches.Add(item);
            }

            return matches.ToArray();
        }

        public IEnumerator GetEnumerator() => Source.GetEnumerator();

        public int IndexOf(ListViewItem item) => Source.IndexOf(item);

        public virtual int IndexOfKey(string? key)
        {
            if (key is null) return -1;

            var found = this[key];
            return found is null ? -1 : IndexOf(found);
        }

        public ListViewItem Insert(int index, ListViewItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (_group is not null) item.Group = _group;

            if (_owner is null)
            {
                _detached.Insert(index, item);
                return item;
            }

            _owner.InsertItemCore(index, item);
            return item;
        }

        public ListViewItem Insert(int index, string? text) => Insert(index, new ListViewItem(text));

        public ListViewItem Insert(int index, string? text, int imageIndex) => Insert(index, new ListViewItem(text, imageIndex));

        public ListViewItem Insert(int index, string? text, string? imageKey) => Insert(index, new ListViewItem(text, imageKey));

        public virtual ListViewItem Insert(int index, string? key, string? text, string? imageKey)
        {
            var item = new ListViewItem(text, imageKey) { Name = key ?? string.Empty };
            return Insert(index, item);
        }

        public virtual ListViewItem Insert(int index, string? key, string? text, int imageIndex)
        {
            var item = new ListViewItem(text, imageIndex) { Name = key ?? string.Empty };
            return Insert(index, item);
        }

        public virtual void Remove(ListViewItem item)
        {
            if (_owner is not null) _owner.RemoveItemCore(item);
            else _detached.Remove(item);
        }

        public virtual void RemoveAt(int index)
        {
            var source = Source;
            if (index < 0 || index >= source.Count) throw new ArgumentOutOfRangeException(nameof(index));

            Remove(source[index]);
        }

        public virtual void RemoveByKey(string key)
        {
            var item = this[key];
            if (item is not null) Remove(item);
        }

        bool IList.IsFixedSize => false;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        object? IList.this[int index]
        {
            get => this[index];
            set => this[index] = (ListViewItem)value!;
        }

        int IList.Add(object? value)
        {
            var added = Add((ListViewItem)value!);
            return IndexOf(added);
        }

        bool IList.Contains(object? value) => value is ListViewItem item && Contains(item);

        int IList.IndexOf(object? value) => value is ListViewItem item ? IndexOf(item) : -1;

        void IList.Insert(int index, object? value) => Insert(index, (ListViewItem)value!);

        void IList.Remove(object? value)
        {
            if (value is ListViewItem item) Remove(item);
        }
    }

    [ListBindable(false)]
    public class ColumnHeaderCollection : IList
    {
        private readonly ListView _owner;

        internal ColumnHeaderCollection(ListView owner)
        {
            _owner = owner;
        }

        public int Count => _owner._columns.Count;

        public bool IsReadOnly => false;

        public virtual ColumnHeader this[int index]
        {
            get
            {
                if (index < 0 || index >= _owner._columns.Count) throw new ArgumentOutOfRangeException(nameof(index));
                return _owner._columns[index];
            }
        }

        public virtual ColumnHeader? this[string? key] => _owner._columns.Find(c => string.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase));

        public virtual ColumnHeader Add(string? text, int width, HorizontalAlignment textAlign)
        {
            var column = new ColumnHeader { Text = text ?? string.Empty, Width = width, TextAlign = textAlign };
            Add(column);
            return column;
        }

        public virtual int Add(ColumnHeader value)
        {
            ArgumentNullException.ThrowIfNull(value);

            _owner._columns.Add(value);
            value.SetListView(_owner);
            _owner.UpdateStyles();
            _owner.Invalidate();

            return _owner._columns.Count - 1;
        }

        public virtual ColumnHeader Add(string? text) => Add(text, 60, HorizontalAlignment.Left);

        public virtual ColumnHeader Add(string? text, int width) => Add(text, width, HorizontalAlignment.Left);

        public virtual ColumnHeader Add(string? key, string? text)
        {
            var column = Add(text, 60, HorizontalAlignment.Left);
            column.Name = key;
            return column;
        }

        public virtual ColumnHeader Add(string? key, string? text, int width)
        {
            var column = Add(text, width, HorizontalAlignment.Left);
            column.Name = key;
            return column;
        }

        public virtual void AddRange(params ColumnHeader[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            foreach (var value in values) Add(value);
        }

        public virtual void Clear()
        {
            foreach (var column in _owner._columns) column.SetListView(null);
            _owner._columns.Clear();
            _owner.UpdateStyles();
            _owner.Invalidate();
        }

        public bool Contains(ColumnHeader? value) => value is not null && _owner._columns.Contains(value);

        public virtual bool ContainsKey(string? key) => this[key] is not null;

        public IEnumerator GetEnumerator() => _owner._columns.GetEnumerator();

        public int IndexOf(ColumnHeader? value) => value is null ? -1 : _owner._columns.IndexOf(value);

        public virtual int IndexOfKey(string? key)
        {
            var found = this[key];
            return found is null ? -1 : IndexOf(found);
        }

        public void Insert(int index, ColumnHeader value)
        {
            ArgumentNullException.ThrowIfNull(value);

            _owner._columns.Insert(index, value);
            value.SetListView(_owner);
            _owner.UpdateStyles();
            _owner.Invalidate();
        }

        public void Insert(int index, string? text, int width, HorizontalAlignment textAlign) =>
            Insert(index, new ColumnHeader { Text = text ?? string.Empty, Width = width, TextAlign = textAlign });

        public void Insert(int index, string? text) => Insert(index, text, 60, HorizontalAlignment.Left);

        public void Insert(int index, string? text, int width) => Insert(index, text, width, HorizontalAlignment.Left);

        public virtual void Remove(ColumnHeader column)
        {
            if (_owner._columns.Remove(column))
            {
                column.SetListView(null);
                _owner.UpdateStyles();
                _owner.Invalidate();
            }
        }

        public virtual void RemoveAt(int index)
        {
            if (index < 0 || index >= _owner._columns.Count) throw new ArgumentOutOfRangeException(nameof(index));
            Remove(_owner._columns[index]);
        }

        public virtual void RemoveByKey(string? key)
        {
            var column = this[key];
            if (column is not null) Remove(column);
        }

        bool IList.IsFixedSize => false;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        object? IList.this[int index]
        {
            get => this[index];
            set => throw new NotSupportedException("Columns cannot be replaced in place; remove and insert instead.");
        }

        int IList.Add(object? value) => Add((ColumnHeader)value!);

        bool IList.Contains(object? value) => value is ColumnHeader column && Contains(column);

        int IList.IndexOf(object? value) => value is ColumnHeader column ? IndexOf(column) : -1;

        void IList.Insert(int index, object? value) => Insert(index, (ColumnHeader)value!);

        void IList.Remove(object? value)
        {
            if (value is ColumnHeader column) Remove(column);
        }

        void ICollection.CopyTo(Array array, int index) => ((ICollection)_owner._columns).CopyTo(array, index);
    }

    [ListBindable(false)]
    public class SelectedIndexCollection : IList
    {
        private readonly ListView _owner;

        internal SelectedIndexCollection(ListView owner)
        {
            _owner = owner;
        }

        private List<int> Source => _owner._items
            .Select((item, index) => (item, index))
            .Where(pair => _owner._selected.Contains(pair.item))
            .Select(pair => pair.index)
            .ToList();

        public int Count => Source.Count;

        public bool IsReadOnly => true;

        public int this[int index]
        {
            get
            {
                var source = Source;
                if (index < 0 || index >= source.Count) throw new ArgumentOutOfRangeException(nameof(index));
                return source[index];
            }
        }

        public int Add(int itemIndex)
        {
            if (itemIndex < 0 || itemIndex >= _owner._items.Count) throw new ArgumentOutOfRangeException(nameof(itemIndex));

            _owner.SetItemSelected(_owner._items[itemIndex], true);
            return IndexOf(itemIndex);
        }

        public void Clear()
        {
            foreach (var item in _owner._selected.ToArray()) _owner.SetItemSelected(item, false);
        }

        public bool Contains(int selectedIndex) => Source.Contains(selectedIndex);

        public void CopyTo(Array dest, int index) => ((ICollection)Source).CopyTo(dest, index);

        public IEnumerator GetEnumerator() => Source.GetEnumerator();

        public int IndexOf(int selectedIndex) => Source.IndexOf(selectedIndex);

        public void Remove(int itemIndex)
        {
            if (itemIndex < 0 || itemIndex >= _owner._items.Count) return;
            _owner.SetItemSelected(_owner._items[itemIndex], false);
        }

        bool IList.IsFixedSize => false;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        object? IList.this[int index]
        {
            get => this[index];
            set => throw new NotSupportedException("SelectedIndices is read-only through its indexer; use Add and Remove.");
        }

        int IList.Add(object? value) => Add((int)value!);

        bool IList.Contains(object? value) => value is int i && Contains(i);

        int IList.IndexOf(object? value) => value is int i ? IndexOf(i) : -1;

        void IList.Insert(int index, object? value) => throw new NotSupportedException("SelectedIndices does not support Insert.");

        void IList.Remove(object? value)
        {
            if (value is int i) Remove(i);
        }

        void IList.RemoveAt(int index) => Remove(this[index]);
    }

    [ListBindable(false)]
    public class SelectedListViewItemCollection : IList
    {
        private readonly ListView _owner;

        internal SelectedListViewItemCollection(ListView owner)
        {
            _owner = owner;
        }

        private List<ListViewItem> Source => _owner._items.Where(i => _owner._selected.Contains(i)).ToList();

        public int Count => Source.Count;

        public bool IsReadOnly => true;

        public ListViewItem this[int index]
        {
            get
            {
                var source = Source;
                if (index < 0 || index >= source.Count) throw new ArgumentOutOfRangeException(nameof(index));
                return source[index];
            }
        }

        public virtual ListViewItem? this[string? key] => Source.Find(i => string.Equals(i.Name, key, StringComparison.OrdinalIgnoreCase));

        public void Clear()
        {
            foreach (var item in _owner._selected.ToArray()) _owner.SetItemSelected(item, false);
        }

        public bool Contains(ListViewItem? item) => item is not null && Source.Contains(item);

        public virtual bool ContainsKey(string? key) => this[key] is not null;

        public void CopyTo(Array dest, int index) => ((ICollection)Source).CopyTo(dest, index);

        public IEnumerator GetEnumerator() => Source.GetEnumerator();

        public int IndexOf(ListViewItem? item) => item is null ? -1 : Source.IndexOf(item);

        public virtual int IndexOfKey(string? key)
        {
            var found = this[key];
            return found is null ? -1 : IndexOf(found);
        }

        bool IList.IsFixedSize => false;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        object? IList.this[int index]
        {
            get => this[index];
            set => throw new NotSupportedException("SelectedItems is read-only through its indexer; set ListViewItem.Selected instead.");
        }

        int IList.Add(object? value) => throw new NotSupportedException("SelectedItems does not support Add; set ListViewItem.Selected instead.");

        bool IList.Contains(object? value) => value is ListViewItem item && Contains(item);

        int IList.IndexOf(object? value) => value is ListViewItem item ? IndexOf(item) : -1;

        void IList.Insert(int index, object? value) => throw new NotSupportedException("SelectedItems does not support Insert.");

        void IList.Remove(object? value)
        {
            if (value is ListViewItem item) _owner.SetItemSelected(item, false);
        }

        void IList.RemoveAt(int index) => _owner.SetItemSelected(this[index], false);
    }

    [ListBindable(false)]
    public class CheckedIndexCollection : IList
    {
        private readonly ListView _owner;

        internal CheckedIndexCollection(ListView owner)
        {
            _owner = owner;
        }

        private List<int> Source => _owner._items
            .Select((item, index) => (item, index))
            .Where(pair => _owner._checkedItems.Contains(pair.item))
            .Select(pair => pair.index)
            .ToList();

        public int Count => Source.Count;

        public bool IsReadOnly => true;

        public int this[int index]
        {
            get
            {
                var source = Source;
                if (index < 0 || index >= source.Count) throw new ArgumentOutOfRangeException(nameof(index));
                return source[index];
            }
        }

        public bool Contains(int checkedIndex) => Source.Contains(checkedIndex);

        public IEnumerator GetEnumerator() => Source.GetEnumerator();

        public int IndexOf(int checkedIndex) => Source.IndexOf(checkedIndex);

        void IList.Clear() => throw new NotSupportedException("CheckedIndices is read-only; set ListViewItem.Checked instead.");

        bool IList.IsFixedSize => true;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        object? IList.this[int index]
        {
            get => this[index];
            set => throw new NotSupportedException("CheckedIndices is read-only; set ListViewItem.Checked instead.");
        }

        int IList.Add(object? value) => throw new NotSupportedException("CheckedIndices is read-only; set ListViewItem.Checked instead.");

        bool IList.Contains(object? value) => value is int i && Contains(i);

        int IList.IndexOf(object? value) => value is int i ? IndexOf(i) : -1;

        void IList.Insert(int index, object? value) => throw new NotSupportedException("CheckedIndices is read-only.");

        void IList.Remove(object? value) => throw new NotSupportedException("CheckedIndices is read-only.");

        void IList.RemoveAt(int index) => throw new NotSupportedException("CheckedIndices is read-only.");

        void ICollection.CopyTo(Array array, int index) => ((ICollection)Source).CopyTo(array, index);
    }

    [ListBindable(false)]
    public class CheckedListViewItemCollection : IList
    {
        private readonly ListView _owner;

        internal CheckedListViewItemCollection(ListView owner)
        {
            _owner = owner;
        }

        private List<ListViewItem> Source => _owner._items.Where(i => _owner._checkedItems.Contains(i)).ToList();

        public int Count => Source.Count;

        public bool IsReadOnly => true;

        public ListViewItem this[int index]
        {
            get
            {
                var source = Source;
                if (index < 0 || index >= source.Count) throw new ArgumentOutOfRangeException(nameof(index));
                return source[index];
            }
        }

        public virtual ListViewItem? this[string? key] => Source.Find(i => string.Equals(i.Name, key, StringComparison.OrdinalIgnoreCase));

        public bool Contains(ListViewItem? item) => item is not null && Source.Contains(item);

        public virtual bool ContainsKey(string? key) => this[key] is not null;

        public void CopyTo(Array dest, int index) => ((ICollection)Source).CopyTo(dest, index);

        public IEnumerator GetEnumerator() => Source.GetEnumerator();

        public int IndexOf(ListViewItem item) => Source.IndexOf(item);

        public virtual int IndexOfKey(string? key)
        {
            var found = this[key];
            return found is null ? -1 : IndexOf(found);
        }

        void IList.Clear() => throw new NotSupportedException("CheckedItems is read-only; set ListViewItem.Checked instead.");

        bool IList.IsFixedSize => true;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        object? IList.this[int index]
        {
            get => this[index];
            set => throw new NotSupportedException("CheckedItems is read-only; set ListViewItem.Checked instead.");
        }

        int IList.Add(object? value) => throw new NotSupportedException("CheckedItems is read-only; set ListViewItem.Checked instead.");

        bool IList.Contains(object? value) => value is ListViewItem item && Contains(item);

        int IList.IndexOf(object? value) => value is ListViewItem item ? IndexOf(item) : -1;

        void IList.Insert(int index, object? value) => throw new NotSupportedException("CheckedItems is read-only.");

        void IList.Remove(object? value) => throw new NotSupportedException("CheckedItems is read-only.");

        void IList.RemoveAt(int index) => throw new NotSupportedException("CheckedItems is read-only.");
    }
}
