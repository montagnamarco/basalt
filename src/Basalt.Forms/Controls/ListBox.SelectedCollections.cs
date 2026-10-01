using System.Collections;

namespace System.Windows.Forms;

public partial class ListBox
{
    /// <summary>The positions of the selected items, always in ascending order.</summary>
    public class SelectedIndexCollection : IList, ICollection, IEnumerable
    {
        private readonly ListBox _owner;

        public SelectedIndexCollection(ListBox owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            _owner = owner;
        }

        public int Count => _owner._selectedIndices.Count;

        public bool IsReadOnly => false;

        public int this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));

                return _owner._selectedIndices[index];
            }
        }

        public void Add(int index) => _owner.SetSelected(index, true);

        public void Clear() => _owner.ClearSelected();

        public bool Contains(int selectedIndex) => _owner._selectedIndices.Contains(selectedIndex);

        public void CopyTo(Array destination, int index) => ((ICollection)_owner._selectedIndices).CopyTo(destination, index);

        public IEnumerator GetEnumerator() => _owner._selectedIndices.GetEnumerator();

        public int IndexOf(int selectedIndex) => _owner._selectedIndices.IndexOf(selectedIndex);

        public void Remove(int index) => _owner.SetSelected(index, false);

        bool IList.IsFixedSize => false;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        object? IList.this[int index]
        {
            get => this[index];
            set => throw new NotSupportedException();
        }

        int IList.Add(object? value)
        {
            var index = Convert.ToInt32(value);
            Add(index);

            return IndexOf(index);
        }

        void IList.Insert(int index, object? value) => throw new NotSupportedException();

        void IList.Remove(object? value) => Remove(Convert.ToInt32(value));

        void IList.RemoveAt(int index) => throw new NotSupportedException();

        bool IList.Contains(object? value) => value is int index && Contains(index);

        int IList.IndexOf(object? value) => value is int index ? IndexOf(index) : -1;
    }

    /// <summary>The selected items themselves, in the same order as <see cref="SelectedIndices"/>.</summary>
    public class SelectedObjectCollection : IList, ICollection, IEnumerable
    {
        private readonly ListBox _owner;

        public SelectedObjectCollection(ListBox owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            _owner = owner;
        }

        public int Count => _owner._selectedIndices.Count;

        public bool IsReadOnly => false;

        public object? this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));

                return _owner._items[_owner._selectedIndices[index]];
            }
            set
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
                ArgumentNullException.ThrowIfNull(value);

                var newItemIndex = _owner._items.IndexOf(value);
                if (newItemIndex < 0) return;

                var oldItemIndex = _owner._selectedIndices[index];
                _owner.SetSelected(oldItemIndex, false);
                _owner.SetSelected(newItemIndex, true);
            }
        }

        public void Add(object value)
        {
            ArgumentNullException.ThrowIfNull(value);

            var index = _owner._items.IndexOf(value);
            if (index < 0) throw new ArgumentException("The object is not an item of the ListBox.", nameof(value));

            _owner.SetSelected(index, true);
        }

        public void Clear() => _owner.ClearSelected();

        public bool Contains(object? selectedObject)
        {
            var index = selectedObject is null ? -1 : _owner._items.IndexOf(selectedObject);

            return index >= 0 && _owner.GetSelected(index);
        }

        public void CopyTo(Array destination, int index)
        {
            foreach (var selectedIndex in _owner._selectedIndices) destination.SetValue(_owner._items[selectedIndex], index++);
        }

        public IEnumerator GetEnumerator()
        {
            foreach (var selectedIndex in _owner._selectedIndices) yield return _owner._items[selectedIndex];
        }

        public int IndexOf(object? selectedObject)
        {
            var itemIndex = selectedObject is null ? -1 : _owner._items.IndexOf(selectedObject);

            return itemIndex >= 0 ? _owner._selectedIndices.IndexOf(itemIndex) : -1;
        }

        public void Remove(object value)
        {
            var index = _owner._items.IndexOf(value);
            if (index >= 0) _owner.SetSelected(index, false);
        }

        bool IList.IsFixedSize => false;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        int IList.Add(object? value)
        {
            Add(value!);

            return Count - 1;
        }

        void IList.Insert(int index, object? value) => throw new NotSupportedException();

        void IList.RemoveAt(int index) => _owner.SetSelected(_owner._selectedIndices[index], false);

        void IList.Remove(object? value)
        {
            if (value is not null) Remove(value);
        }
    }
}
