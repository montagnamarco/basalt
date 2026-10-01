using System.Collections;
using System.Collections.Generic;

namespace System.Drawing.Printing;

public partial class PrinterSettings
{
    public class PaperSizeCollection : ICollection
    {
        private readonly List<PaperSize> _items;

        public PaperSizeCollection(PaperSize[] array)
        {
            ArgumentNullException.ThrowIfNull(array);
            _items = new List<PaperSize>(array);
        }

        public int Count => _items.Count;

        public virtual PaperSize this[int index] => _items[index];

        public int Add(PaperSize paperSize)
        {
            ArgumentNullException.ThrowIfNull(paperSize);
            _items.Add(paperSize);
            return _items.Count - 1;
        }

        public void CopyTo(PaperSize[] array, int index) => _items.CopyTo(array, index);

        public IEnumerator GetEnumerator() => _items.GetEnumerator();

        void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;
    }
}
