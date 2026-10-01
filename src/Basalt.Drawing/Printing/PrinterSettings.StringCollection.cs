using System.Collections;
using System.Collections.Generic;

namespace System.Drawing.Printing;

public partial class PrinterSettings
{
    /// <summary>A read-only-in-practice list of strings, used for InstalledPrinters.</summary>
    public class StringCollection : ICollection, IEnumerable<string>
    {
        private readonly List<string> _items;

        public StringCollection(string[] value)
        {
            ArgumentNullException.ThrowIfNull(value);
            _items = new List<string>(value);
        }

        public int Count => _items.Count;

        public virtual string this[int index] => _items[index];

        public int Add(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            _items.Add(value);
            return _items.Count - 1;
        }

        public void CopyTo(string[] array, int index) => _items.CopyTo(array, index);

        public IEnumerator GetEnumerator() => _items.GetEnumerator();

        IEnumerator<string> IEnumerable<string>.GetEnumerator() => _items.GetEnumerator();

        void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;
    }
}
