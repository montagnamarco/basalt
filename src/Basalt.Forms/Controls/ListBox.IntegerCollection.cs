using System.Collections;

namespace System.Windows.Forms;

public partial class ListBox
{
    /// <summary>The pixel positions of <see cref="CustomTabOffsets"/>, kept in the order they were added.</summary>
    public class IntegerCollection : IList, ICollection, IEnumerable
    {
        private readonly ListBox _owner;
        private readonly List<int> _items = [];

        public IntegerCollection(ListBox owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            _owner = owner;
        }

        public int Count => _items.Count;

        public int this[int index]
        {
            get
            {
                if (index < 0 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index));

                return _items[index];
            }
            set
            {
                if (index < 0 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index));

                _items[index] = value;
                _owner.Invalidate();
            }
        }

        public int Add(int item)
        {
            _items.Add(item);
            _owner.Invalidate();

            return _items.Count - 1;
        }

        public void AddRange(params int[] items)
        {
            ArgumentNullException.ThrowIfNull(items);
            _items.AddRange(items);
            _owner.Invalidate();
        }

        public void AddRange(IntegerCollection value)
        {
            ArgumentNullException.ThrowIfNull(value);
            _items.AddRange(value._items);
            _owner.Invalidate();
        }

        public void Clear()
        {
            if (_items.Count == 0) return;

            _items.Clear();
            _owner.Invalidate();
        }

        public bool Contains(int item) => _items.Contains(item);

        public void CopyTo(Array destination, int index) => ((ICollection)_items).CopyTo(destination, index);

        public int IndexOf(int item) => _items.IndexOf(item);

        public void Remove(int item)
        {
            if (_items.Remove(item)) _owner.Invalidate();
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index));

            _items.RemoveAt(index);
            _owner.Invalidate();
        }

        bool IList.IsFixedSize => false;

        bool IList.IsReadOnly => false;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        object? IList.this[int index]
        {
            get => this[index];
            set => this[index] = Convert.ToInt32(value);
        }

        int IList.Add(object? value)
        {
            var index = Add(Convert.ToInt32(value));
            return index;
        }

        void IList.Insert(int index, object? value)
        {
            if (index < 0 || index > _items.Count) throw new ArgumentOutOfRangeException(nameof(index));

            _items.Insert(index, Convert.ToInt32(value));
            _owner.Invalidate();
        }

        void IList.Remove(object? value) => Remove(Convert.ToInt32(value));

        bool IList.Contains(object? value) => value is int item && Contains(item);

        int IList.IndexOf(object? value) => value is int item ? IndexOf(item) : -1;

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}
