using System.Collections;
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
    public class ObjectCollection : IList, ICollection, IEnumerable
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
