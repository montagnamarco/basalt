using System.Collections;
using System.Collections.Generic;

namespace System.Drawing.Printing;

public partial class PrinterSettings
{
    public class PrinterResolutionCollection : ICollection
    {
        private readonly List<PrinterResolution> _items;

        public PrinterResolutionCollection(PrinterResolution[] array)
        {
            ArgumentNullException.ThrowIfNull(array);
            _items = new List<PrinterResolution>(array);
        }

        public int Count => _items.Count;

        public virtual PrinterResolution this[int index] => _items[index];

        public int Add(PrinterResolution printerResolution)
        {
            ArgumentNullException.ThrowIfNull(printerResolution);
            _items.Add(printerResolution);
            return _items.Count - 1;
        }

        public void CopyTo(PrinterResolution[] array, int index) => _items.CopyTo(array, index);

        public IEnumerator GetEnumerator() => _items.GetEnumerator();

        void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;
    }
}
