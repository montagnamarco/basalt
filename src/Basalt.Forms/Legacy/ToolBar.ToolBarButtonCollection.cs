using System.Collections;
using System.ComponentModel;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

public partial class ToolBar
{
    [Obsolete("`ToolBar` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `ToolStrip` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    public class ToolBarButtonCollection : ICollection, IEnumerable, IList
    {
        public ToolBarButtonCollection(ToolBar owner) => throw new PlatformNotSupportedException();

        public int Count => throw new PlatformNotSupportedException();

        public bool IsReadOnly => throw new PlatformNotSupportedException();

        public virtual ToolBarButton this[int index]
        {
            get => throw new PlatformNotSupportedException();
            set => throw new PlatformNotSupportedException();
        }

        public virtual ToolBarButton this[string key] => throw new PlatformNotSupportedException();

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

        void IList.Remove(object? value) => throw new PlatformNotSupportedException();

        void IList.Insert(int index, object? value) => throw new PlatformNotSupportedException();

        void ICollection.CopyTo(Array array, int index) => throw new PlatformNotSupportedException();

        // Clear() and RemoveAt(int) below already match IList's signatures, so
        // they satisfy the interface implicitly; no explicit forwarder needed.

        public int Add(ToolBarButton button) => throw new PlatformNotSupportedException();

        public int Add(string text) => throw new PlatformNotSupportedException();

        public void AddRange(ToolBarButton[] buttons) => throw new PlatformNotSupportedException();

        public void Clear() => throw new PlatformNotSupportedException();

        public bool Contains(ToolBarButton button) => throw new PlatformNotSupportedException();

        public virtual bool ContainsKey(string key) => throw new PlatformNotSupportedException();

        public IEnumerator GetEnumerator() => throw new PlatformNotSupportedException();

        public int IndexOf(ToolBarButton button) => throw new PlatformNotSupportedException();

        public virtual int IndexOfKey(string key) => throw new PlatformNotSupportedException();

        public void Insert(int index, ToolBarButton button) => throw new PlatformNotSupportedException();

        public void Remove(ToolBarButton button) => throw new PlatformNotSupportedException();

        public void RemoveAt(int index) => throw new PlatformNotSupportedException();

        public virtual void RemoveByKey(string key) => throw new PlatformNotSupportedException();
    }
}
