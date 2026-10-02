using System.Collections;
using System.ComponentModel;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

public abstract partial class Menu
{
    [Obsolete("`Menu` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `ToolStripDropDown` and `ToolStripDropDownMenu` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    [ListBindable(false)]
    public class MenuItemCollection : ICollection, IEnumerable, IList
    {
        public MenuItemCollection(Menu owner) => throw new PlatformNotSupportedException();

        public int Count => throw new PlatformNotSupportedException();

        public bool IsReadOnly => throw new PlatformNotSupportedException();

        public virtual MenuItem this[int index] => throw new PlatformNotSupportedException();

        public virtual MenuItem this[string key] => throw new PlatformNotSupportedException();

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

        public virtual MenuItem Add(string caption) => throw new PlatformNotSupportedException();

        public virtual MenuItem Add(string caption, EventHandler onClick) => throw new PlatformNotSupportedException();

        public virtual MenuItem Add(string caption, MenuItem[] items) => throw new PlatformNotSupportedException();

        public virtual int Add(MenuItem item) => throw new PlatformNotSupportedException();

        public virtual int Add(int index, MenuItem item) => throw new PlatformNotSupportedException();

        public virtual void AddRange(MenuItem[] items) => throw new PlatformNotSupportedException();

        public virtual void Clear() => throw new PlatformNotSupportedException();

        public bool Contains(MenuItem value) => throw new PlatformNotSupportedException();

        public virtual bool ContainsKey(string key) => throw new PlatformNotSupportedException();

        public void CopyTo(Array dest, int index) => throw new PlatformNotSupportedException();

        public MenuItem[] Find(string key, bool searchAllChildren) => throw new PlatformNotSupportedException();

        public IEnumerator GetEnumerator() => throw new PlatformNotSupportedException();

        public int IndexOf(MenuItem value) => throw new PlatformNotSupportedException();

        public virtual int IndexOfKey(string key) => throw new PlatformNotSupportedException();

        public virtual void Remove(MenuItem item) => throw new PlatformNotSupportedException();

        public virtual void RemoveAt(int index) => throw new PlatformNotSupportedException();

        public virtual void RemoveByKey(string key) => throw new PlatformNotSupportedException();
    }
}
