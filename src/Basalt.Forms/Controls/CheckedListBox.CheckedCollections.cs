using System.Collections;

namespace System.Windows.Forms;

public partial class CheckedListBox
{
    /// <summary>Every item whose check state is Checked or Indeterminate, computed live from the item list.</summary>
    private IEnumerable<int> CheckedIndexSequence()
    {
        for (var index = 0; index < Items.Count; index++)
        {
            if (GetItemCheckState(index) != CheckState.Unchecked) yield return index;
        }
    }

    /// <summary>The positions of the checked items, always in ascending order; a read-only live view.</summary>
    public class CheckedIndexCollection : IList, ICollection, IEnumerable
    {
        private readonly CheckedListBox _owner;

        internal CheckedIndexCollection(CheckedListBox owner)
        {
            _owner = owner;
        }

        public int Count => _owner.CheckedIndexSequence().Count();

        public bool IsReadOnly => true;

        public int this[int index]
        {
            get
            {
                var position = 0;
                foreach (var checkedIndex in _owner.CheckedIndexSequence())
                {
                    if (position == index) return checkedIndex;
                    position++;
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        /// <summary>Whether the item at this position in the whole list is checked.</summary>
        public bool Contains(int index) => index >= 0 && index < _owner.Items.Count && _owner.GetItemChecked(index);

        public void CopyTo(Array dest, int index)
        {
            foreach (var checkedIndex in _owner.CheckedIndexSequence()) dest.SetValue(checkedIndex, index++);
        }

        public IEnumerator GetEnumerator() => _owner.CheckedIndexSequence().GetEnumerator();

        public int IndexOf(int index)
        {
            var position = 0;
            foreach (var checkedIndex in _owner.CheckedIndexSequence())
            {
                if (checkedIndex == index) return position;
                position++;
            }

            return -1;
        }

        bool IList.IsFixedSize => true;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        object? IList.this[int index]
        {
            get => this[index];
            set => throw new NotSupportedException();
        }

        int IList.Add(object? value) => throw new NotSupportedException();

        void IList.Clear() => throw new NotSupportedException();

        bool IList.Contains(object? value) => value is int index && Contains(index);

        int IList.IndexOf(object? value) => value is int index ? IndexOf(index) : -1;

        void IList.Insert(int index, object? value) => throw new NotSupportedException();

        void IList.Remove(object? value) => throw new NotSupportedException();

        void IList.RemoveAt(int index) => throw new NotSupportedException();
    }

    /// <summary>The checked items themselves, in the same order as <see cref="CheckedIndices"/>; a read-only live view.</summary>
    public class CheckedItemCollection : IList, ICollection, IEnumerable
    {
        private readonly CheckedListBox _owner;

        internal CheckedItemCollection(CheckedListBox owner)
        {
            _owner = owner;
        }

        public int Count => _owner.CheckedIndexSequence().Count();

        public bool IsReadOnly => true;

        public object? this[int index]
        {
            get
            {
                var position = 0;
                foreach (var checkedIndex in _owner.CheckedIndexSequence())
                {
                    if (position == index) return _owner.Items[checkedIndex];
                    position++;
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
            set => throw new NotSupportedException();
        }

        public bool Contains(object? item)
        {
            if (item is null) return false;

            var itemIndex = _owner.Items.IndexOf(item);
            return itemIndex >= 0 && _owner.GetItemChecked(itemIndex);
        }

        public void CopyTo(Array dest, int index)
        {
            foreach (var checkedIndex in _owner.CheckedIndexSequence()) dest.SetValue(_owner.Items[checkedIndex], index++);
        }

        public IEnumerator GetEnumerator()
        {
            foreach (var checkedIndex in _owner.CheckedIndexSequence()) yield return _owner.Items[checkedIndex];
        }

        public int IndexOf(object? item)
        {
            if (item is null) return -1;

            var itemIndex = _owner.Items.IndexOf(item);
            if (itemIndex < 0 || !_owner.GetItemChecked(itemIndex)) return -1;

            var position = 0;
            foreach (var checkedIndex in _owner.CheckedIndexSequence())
            {
                if (checkedIndex == itemIndex) return position;
                position++;
            }

            return -1;
        }

        bool IList.IsFixedSize => true;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        int IList.Add(object? value) => throw new NotSupportedException();

        void IList.Clear() => throw new NotSupportedException();

        bool IList.Contains(object? value) => Contains(value);

        int IList.IndexOf(object? value) => IndexOf(value);

        void IList.Insert(int index, object? value) => throw new NotSupportedException();

        void IList.Remove(object? value) => throw new NotSupportedException();

        void IList.RemoveAt(int index) => throw new NotSupportedException();
    }
}
