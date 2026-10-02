using System.Collections;
using System.ComponentModel;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

public partial class StatusBar
{
    [Obsolete("`StatusBar` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `StatusStrip` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    [ListBindable(false)]
    public class StatusBarPanelCollection : ICollection, IEnumerable, IList
    {
        public StatusBarPanelCollection(StatusBar owner) => throw new PlatformNotSupportedException();

        public int Count => throw new PlatformNotSupportedException();

        public bool IsReadOnly => throw new PlatformNotSupportedException();

        public virtual StatusBarPanel this[int index]
        {
            get => throw new PlatformNotSupportedException();
            set => throw new PlatformNotSupportedException();
        }

        public virtual StatusBarPanel this[string key] => throw new PlatformNotSupportedException();

        object? IList.this[int index]
        {
            get => throw new PlatformNotSupportedException();
            set => throw new PlatformNotSupportedException();
        }

        bool IList.IsFixedSize => throw new PlatformNotSupportedException();

        bool ICollection.IsSynchronized => throw new PlatformNotSupportedException();

        object ICollection.SyncRoot => throw new PlatformNotSupportedException();

        int IList.Add(object? value) => throw new PlatformNotSupportedException();

        bool IList.Contains(object? value) => throw new PlatformNotSupportedException();

        int IList.IndexOf(object? value) => throw new PlatformNotSupportedException();

        void IList.Insert(int index, object? value) => throw new PlatformNotSupportedException();

        void IList.Remove(object? value) => throw new PlatformNotSupportedException();

        void ICollection.CopyTo(Array array, int index) => throw new PlatformNotSupportedException();

        // Clear() and RemoveAt(int) below already match IList's signatures, so
        // they satisfy the interface implicitly; no explicit forwarder needed.

        public virtual StatusBarPanel Add(string text) => throw new PlatformNotSupportedException();

        public virtual int Add(StatusBarPanel value) => throw new PlatformNotSupportedException();

        public virtual void AddRange(StatusBarPanel[] panels) => throw new PlatformNotSupportedException();

        public virtual void Clear() => throw new PlatformNotSupportedException();

        public bool Contains(StatusBarPanel panel) => throw new PlatformNotSupportedException();

        public virtual bool ContainsKey(string key) => throw new PlatformNotSupportedException();

        public IEnumerator GetEnumerator() => throw new PlatformNotSupportedException();

        public int IndexOf(StatusBarPanel panel) => throw new PlatformNotSupportedException();

        public virtual int IndexOfKey(string key) => throw new PlatformNotSupportedException();

        public virtual void Insert(int index, StatusBarPanel value) => throw new PlatformNotSupportedException();

        public virtual void Remove(StatusBarPanel value) => throw new PlatformNotSupportedException();

        public virtual void RemoveAt(int index) => throw new PlatformNotSupportedException();

        public virtual void RemoveByKey(string key) => throw new PlatformNotSupportedException();
    }
}
