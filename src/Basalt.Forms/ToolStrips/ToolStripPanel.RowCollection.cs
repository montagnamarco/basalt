using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

public partial class ToolStripPanel
{
    /// <summary>The panel's rows in layout order.</summary>
    public class ToolStripPanelRowCollection : ArrangedElementCollection
    {
        private readonly ToolStripPanel _owner;

        public ToolStripPanelRowCollection(ToolStripPanel owner)
            : base(GetRowStorage(owner))
        {
            _owner = owner;
        }

        public ToolStripPanelRowCollection(ToolStripPanel owner, ToolStripPanelRow[] value)
            : this(owner)
        {
            ArgumentNullException.ThrowIfNull(value);
            AddRange(value);
        }

        private static List<IArrangedElement> GetRowStorage(ToolStripPanel owner)
        {
            ArgumentNullException.ThrowIfNull(owner);

            // Publicly constructed collections and the panel itself must share the same
            // backing list: inherited IList, Count and enumeration read Items directly.
            return owner._rows?.Items ?? [];
        }

        // Row callbacks can mutate the collection during Join and layout. Each
        // caller walks a stable view so the operation can finish its pass.
        internal IEnumerable<ToolStripPanelRow> RowsInOrder => ToArray();

        internal ToolStripPanelRow[] ToArray() => Items.Cast<ToolStripPanelRow>().ToArray();

        internal void ClearForDispose() => Items.Clear();

        public virtual ToolStripPanelRow this[int index] => (ToolStripPanelRow)Items[index];

        public int Add(ToolStripPanelRow value)
        {
            if (Contains(value)) return IndexOf(value);

            Insert(Count, value);

            return Count - 1;
        }

        private protected override int AddFromList(object? value)
        {
            if (value is not ToolStripPanelRow row)
                throw new ArgumentException("Value must be a ToolStripPanelRow.", nameof(value));

            return Add(row);
        }

        public void AddRange(ToolStripPanelRowCollection value)
        {
            ArgumentNullException.ThrowIfNull(value);
            AddRange(value.ToArray());
        }

        public void AddRange(ToolStripPanelRow[] value)
        {
            ArgumentNullException.ThrowIfNull(value);

            foreach (var row in value) Add(row);
        }

        public virtual void Clear()
        {
            foreach (var row in ToArray()) Remove(row);
        }

        private protected override void ClearFromList() => Clear();

        public bool Contains(ToolStripPanelRow? value) => value is not null && Items.Contains(value);

        public void CopyTo(ToolStripPanelRow[] array, int index) => ToArray().CopyTo(array, index);

        public int IndexOf(ToolStripPanelRow? value) => value is null ? -1 : Items.IndexOf(value);

        public void Insert(int index, ToolStripPanelRow value)
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.ToolStripPanel != _owner)
                throw new ArgumentException("The row belongs to a different ToolStripPanel.", nameof(value));

            if (Contains(value)) throw new ArgumentException("The row is already in this panel.", nameof(value));

            Items.Insert(index, value);
            _owner.PerformLayout();
        }

        public void Remove(ToolStripPanelRow? value)
        {
            if (value is null || !Items.Remove(value)) return;

            // A removed row cannot keep strips parented by a panel that no longer
            // has a row for them. Detach them before layout sees the new row list.
            foreach (var control in value.Controls)
            {
                value.RemoveControlInternal(control);
                _owner.Controls.Remove(control);
            }

            _owner.PerformLayout();
        }

        private protected override void RemoveFromList(object? value)
        {
            if (value is ToolStripPanelRow row) Remove(row);
        }

        public void RemoveAt(int index) => Remove(this[index]);
    }
}
