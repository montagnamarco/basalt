using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;

namespace System.Windows.Forms;

public partial class ComboBox
{
    /// <summary>
    /// The items a ComboBox shows, in display order. Sorted keeps that order
    /// by the item's text as it is inserted; bound to a DataSource, the list
    /// itself is read-only and only the binding machinery may repopulate it.
    /// </summary>
    [ListBindable(false)]
    public class ObjectCollection : IList, ICollection, IEnumerable, IComparer<ObjectCollection.Entry>
    {
        private readonly ComboBox _owner;

        public ObjectCollection(ComboBox owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            _owner = owner;
        }

        public int Count => _owner._items.Count;

        public bool IsReadOnly => false;

        public virtual object? this[int index]
        {
            get
            {
                if (index < 0 || index >= _owner._items.Count) throw new ArgumentOutOfRangeException(nameof(index));

                return _owner._items[index];
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (index < 0 || index >= _owner._items.Count) throw new ArgumentOutOfRangeException(nameof(index));

                EnsureNotDataBound();
                _owner.SetItemCore(index, value);
            }
        }

        public int Add(object item)
        {
            ArgumentNullException.ThrowIfNull(item);
            EnsureNotDataBound();

            var index = _owner.Sorted ? SortedInsertionIndex(item) : _owner._items.Count;
            _owner._items.Insert(index, item);
            OnInserted(index);

            return index;
        }

        public void AddRange(params object[] items)
        {
            ArgumentNullException.ThrowIfNull(items);
            EnsureNotDataBound();

            _owner.AddItemsCore(items);
        }

        public void Clear()
        {
            EnsureNotDataBound();
            if (_owner._items.Count == 0) return;

            _owner._items.Clear();
            _owner.SetSelectedIndexCore(-1);
            _owner.Invalidate();
        }

        public bool Contains(object? value) => value is not null && _owner._items.Contains(value);

        public void CopyTo(object[] destination, int arrayIndex) => _owner._items.CopyTo(destination, arrayIndex);

        public IEnumerator GetEnumerator() => _owner._items.GetEnumerator();

        public int IndexOf(object? value) => value is null ? -1 : _owner._items.IndexOf(value);

        public void Insert(int index, object? item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (index < 0 || index > _owner._items.Count) throw new ArgumentOutOfRangeException(nameof(index));

            EnsureNotDataBound();

            // A sorted list keeps its order even for an explicit Insert, as Add does.
            var actualIndex = _owner.Sorted ? SortedInsertionIndex(item) : index;
            _owner._items.Insert(actualIndex, item);
            OnInserted(actualIndex);
        }

        public void Remove(object? value)
        {
            if (value is null) return;

            var index = _owner._items.IndexOf(value);
            if (index >= 0) RemoveAt(index);
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _owner._items.Count) throw new ArgumentOutOfRangeException(nameof(index));

            EnsureNotDataBound();
            _owner._items.RemoveAt(index);
            OnRemoved(index);
        }

        /// <summary>Replaces every item from the bound list, bypassing the DataSource guard that rejects direct edits.</summary>
        internal void ResetFromBinding(IList items)
        {
            _owner._items.Clear();
            foreach (var item in items) _owner._items.Add(item!);

            _owner.SetSelectedIndexCore(-1);
            _owner.Invalidate();
        }

        private void EnsureNotDataBound()
        {
            if (_owner.DataSource is not null) throw new ArgumentException("The items collection cannot be modified when the DataSource property is set.");
        }

        private void OnInserted(int index)
        {
            if (_owner._selectedIndex >= index) _owner._selectedIndex++;

            _owner.Invalidate();
        }

        private void OnRemoved(int index)
        {
            if (_owner._selectedIndex == index) _owner.SetSelectedIndexCore(-1);
            else if (_owner._selectedIndex > index) _owner._selectedIndex--;

            _owner.Invalidate();
        }

        /// <summary>Where a new item belongs so the collection stays ordered by its display text.</summary>
        private int SortedInsertionIndex(object item)
        {
            var text = _owner.GetItemText(item);
            var low = 0;
            var high = _owner._items.Count;

            while (low < high)
            {
                var middle = (low + high) / 2;
                var comparison = string.Compare(_owner.GetItemText(_owner._items[middle]), text, StringComparison.CurrentCultureIgnoreCase);

                if (comparison <= 0) low = middle + 1;
                else high = middle;
            }

            return low;
        }

        /// <summary>Resorts every item alphabetically by its display text, keeping the selection on the same item; what Sorted=true and ComboBox.Sort() both call.</summary>
        internal void SortEntries()
        {
            if (_owner._items.Count == 0) return;

            var entries = new Entry[_owner._items.Count];
            for (var i = 0; i < entries.Length; i++) entries[i] = new Entry(_owner._items[i], i);

            // A stable sort by display text: Array.Sort is not stable on its own, so Entry
            // remembers each item's original position and the comparer breaks ties with it.
            Array.Sort(entries, this);

            var selectedItem = _owner.SelectedItem;

            _owner._items.Clear();
            foreach (var entry in entries) _owner._items.Add(entry.Item);

            // Through the same path SelectedIndex's own setter uses, so moving the selected item to a
            // new index (the item is unchanged, but its index is) still raises SelectedIndexChanged,
            // exactly as WinForms raises it for any index change, same item or not.
            if (selectedItem is not null) _owner.SetSelectedIndexCore(_owner._items.IndexOf(selectedItem));
        }

        /// <summary>By display text, then by original position, so equal-text items keep their relative order.</summary>
        int IComparer<Entry>.Compare(Entry? x, Entry? y)
        {
            var byText = string.Compare(_owner.GetItemText(x!.Item), _owner.GetItemText(y!.Item), StringComparison.CurrentCultureIgnoreCase);
            return byText != 0 ? byText : x.OriginalIndex.CompareTo(y.OriginalIndex);
        }

        /// <summary>One item as it stood before a sort: wrapping it with that position is what makes the sort stable.</summary>
        private sealed class Entry
        {
            public Entry(object item, int originalIndex)
            {
                Item = item;
                OriginalIndex = originalIndex;
            }

            public object Item { get; }

            public int OriginalIndex { get; }
        }

        bool IList.IsFixedSize => false;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        void ICollection.CopyTo(Array array, int index) => ((ICollection)_owner._items).CopyTo(array, index);

        int IList.Add(object? value) => Add(value!);

        bool IList.Contains(object? value) => Contains(value);

        int IList.IndexOf(object? value) => IndexOf(value);

        void IList.Insert(int index, object? value) => Insert(index, value);

        void IList.Remove(object? value) => Remove(value);

        object? IList.this[int index]
        {
            get => this[index];
            set => this[index] = value;
        }
    }
}
