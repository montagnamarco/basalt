using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;

namespace System.Windows.Forms;

public partial class DataGridViewComboBoxCell
{
    /// <summary>The items a combo cell lists, in display order; the cell's own storage when it is not bound through DataSource.</summary>
    [ListBindable(false)]
    public class ObjectCollection : IList, ICollection, IEnumerable
    {
        private readonly DataGridViewComboBoxCell _owner;
        private readonly List<object> _items = [];

        public ObjectCollection(DataGridViewComboBoxCell owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            _owner = owner;
        }

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public virtual object? this[int index]
        {
            get => _items[index];
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _items[index] = value;
                _owner.DataGridView?.InvalidateCellCore(_owner);
            }
        }

        public int Add(object item)
        {
            ArgumentNullException.ThrowIfNull(item);

            _items.Add(item);
            if (_owner.Sorted) Sort();

            return _items.IndexOf(item);
        }

        public void AddRange(params object[] items) => AddRange((ReadOnlySpan<object>)items);

        public void AddRange(ObjectCollection value)
        {
            ArgumentNullException.ThrowIfNull(value);
            AddRange(value._items.ToArray());
        }

        public void Clear() => _items.Clear();

        public bool Contains(object? value) => value is not null && _items.Contains(value);

        public void CopyTo(object[] destination, int arrayIndex) => _items.CopyTo(destination, arrayIndex);

        public IEnumerator GetEnumerator() => _items.GetEnumerator();

        public int IndexOf(object? value) => value is null ? -1 : _items.IndexOf(value);

        public void Insert(int index, object? item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _items.Insert(index, item);
        }

        public void Remove(object? value)
        {
            if (value is not null) _items.Remove(value);
        }

        public void RemoveAt(int index) => _items.RemoveAt(index);

        /// <summary>Used by the cell's own GetFormattedValue/ParseFormattedValue to walk the underlying items without going through the public collection API.</summary>
        internal IReadOnlyList<object> Snapshot => _items;

        private void AddRange(ReadOnlySpan<object> items)
        {
            foreach (var item in items) Add(item);
        }

        private void Sort() => _items.Sort((left, right) => string.Compare(_owner.GetItemDisplayText(left), _owner.GetItemDisplayText(right), StringComparison.CurrentCultureIgnoreCase));

        bool IList.IsFixedSize => false;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

        int IList.Add(object? value) => Add(value!);

        bool IList.Contains(object? value) => Contains(value);

        int IList.IndexOf(object? value) => IndexOf(value);

        void IList.Insert(int index, object? value) => Insert(index, value);

        void IList.Remove(object? value) => Remove(value);
    }
}
