using System.Collections;
using System.ComponentModel;

namespace System.Windows.Forms;

public partial class DomainUpDown
{
    /// <summary>The strings DomainUpDown cycles through; an ArrayList, exactly as Microsoft declares it.</summary>
    public class DomainUpDownItemCollection : ArrayList
    {
        private readonly DomainUpDown _owner;

        internal DomainUpDownItemCollection(DomainUpDown owner) => _owner = owner;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public override object? this[int index]
        {
            get => base[index];
            set
            {
                base[index] = value;
                _owner.NotifyItemReplaced(index);
            }
        }

        public override int Add(object? item)
        {
            ArgumentNullException.ThrowIfNull(item);

            var index = _owner.ShouldInsertSorted ? InsertSorted(item) : base.Add(item);
            _owner.NotifyItemInserted(index);

            return index;
        }

        public override void Insert(int index, object? item)
        {
            ArgumentNullException.ThrowIfNull(item);

            // A sorted collection keeps its order even when the caller asks for a specific position.
            if (_owner.ShouldInsertSorted) index = InsertSorted(item);
            else base.Insert(index, item);

            _owner.NotifyItemInserted(index);
        }

        public override void Remove(object? item)
        {
            var index = IndexOf(item);
            if (index >= 0) RemoveAt(index);
        }

        public override void RemoveAt(int item)
        {
            base.RemoveAt(item);
            _owner.NotifyItemRemoved(item);
        }

        private int InsertSorted(object item)
        {
            var position = 0;
            while (position < Count && CompareItemText(base[position], item) <= 0) position++;

            base.Insert(position, item);

            return position;
        }
    }
}
