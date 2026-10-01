using System.Collections;
using System.Collections.Generic;

namespace System.Drawing.Printing;

public partial class PrinterSettings
{
    public class PaperSourceCollection : ICollection
    {
        private readonly List<PaperSource> _items;

        public PaperSourceCollection(PaperSource[] array)
        {
            ArgumentNullException.ThrowIfNull(array);
            _items = new List<PaperSource>(array);
        }

        public int Count => _items.Count;

        public virtual PaperSource this[int index] => _items[index];

        public int Add(PaperSource paperSource)
        {
            ArgumentNullException.ThrowIfNull(paperSource);
            _items.Add(paperSource);
            return _items.Count - 1;
        }

        public void CopyTo(PaperSource[] array, int index) => _items.CopyTo(array, index);

        public IEnumerator GetEnumerator() => _items.GetEnumerator();

        void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;
    }
}
