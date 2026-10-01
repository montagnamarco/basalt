using System.Collections;
using System.ComponentModel;

namespace System.Windows.Forms;

public partial class ListBox
{
    /// <summary>
    /// The items a ListBox shows, in display order. Sorted keeps that order
    /// by the item's text as it is inserted; bound to a DataSource, the list
    /// itself is read-only and only the binding machinery may repopulate it.
    /// </summary>
    /// <remarks>The owning ListBox keeps the actual storage; this is a view over it.</remarks>
    [ListBindable(false)]
    public class ObjectCollection : IList, ICollection, IEnumerable
    {
        private readonly ListBox _owner;

        public ObjectCollection(ListBox owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            _owner = owner;
        }

        public ObjectCollection(ListBox owner, ObjectCollection value)
            : this(owner)
        {
            AddRange(value);
        }

        public ObjectCollection(ListBox owner, object[] value)
            : this(owner)
        {
            AddRange(value);
        }

        public int Count => _owner._items.Count;

        public bool IsReadOnly => false;

        public virtual object this[int index]
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

                _owner.EnsureNotDataBound();
                _owner._items[index] = value;
                _owner.OnItemReplaced(index);
            }
        }

        public int Add(object item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _owner.EnsureNotDataBound();

            var index = _owner.Sorted ? SortedInsertionIndex(item) : _owner._items.Count;
            _owner._items.Insert(index, item);
            _owner.OnItemsInserted(index);

            return index;
        }

        public void AddRange(ObjectCollection value)
        {
            ArgumentNullException.ThrowIfNull(value);
            AddRange(value._owner._items.ToArray());
        }

        public void AddRange(object[] items)
        {
            ArgumentNullException.ThrowIfNull(items);
            _owner.EnsureNotDataBound();

            _owner.BeginUpdate();
            try
            {
                foreach (var item in items) Add(item);
            }
            finally
            {
                _owner.EndUpdate();
            }
        }

        public virtual void Clear()
        {
            _owner.EnsureNotDataBound();
            if (_owner._items.Count == 0) return;

            _owner._items.Clear();
            _owner.OnItemsCleared();
        }

        public bool Contains(object value) => _owner._items.Contains(value);

        public void CopyTo(object[] destination, int arrayIndex) => _owner._items.CopyTo(destination, arrayIndex);

        public IEnumerator GetEnumerator() => _owner._items.GetEnumerator();

        public int IndexOf(object value) => _owner._items.IndexOf(value);

        public void Insert(int index, object item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (index < 0 || index > _owner._items.Count) throw new ArgumentOutOfRangeException(nameof(index));

            _owner.EnsureNotDataBound();

            // A sorted list keeps its order even for an explicit Insert, as Add does.
            var actualIndex = _owner.Sorted ? SortedInsertionIndex(item) : index;
            _owner._items.Insert(actualIndex, item);
            _owner.OnItemsInserted(actualIndex);
        }

        public void Remove(object value)
        {
            var index = _owner._items.IndexOf(value);
            if (index >= 0) RemoveAt(index);
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _owner._items.Count) throw new ArgumentOutOfRangeException(nameof(index));

            _owner.EnsureNotDataBound();
            _owner._items.RemoveAt(index);
            _owner.OnItemsRemoved(index);
        }

        /// <summary>Replaces every item from the bound list, bypassing the DataSource guard that rejects direct edits.</summary>
        internal void ResetFromBinding(IList items)
        {
            _owner._items.Clear();
            foreach (var item in items) _owner._items.Add(item!);

            _owner.OnItemsReset();
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

        bool IList.IsFixedSize => false;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        void ICollection.CopyTo(Array array, int index) => ((ICollection)_owner._items).CopyTo(array, index);

        // Explicit, so the nullable object? of IList does not have to match the never-null
        // parameters of the typed members above: the two stay independent overloads.
        int IList.Add(object? value) => Add(value!);

        bool IList.Contains(object? value) => value is not null && Contains(value);

        int IList.IndexOf(object? value) => value is null ? -1 : IndexOf(value);

        void IList.Insert(int index, object? value) => Insert(index, value!);

        void IList.Remove(object? value)
        {
            if (value is not null) Remove(value);
        }

        object? IList.this[int index]
        {
            get => this[index];
            set => this[index] = value!;
        }
    }
}
