using System.Collections;
using System.ComponentModel;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

[Obsolete("`DataGrid` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `DataGridView` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
[ListBindable(false)]
public class GridTableStylesCollection : BaseCollection, IList
{
    // Microsoft's reference signature has no public constructor: only
    // DataGrid can create one, and its own getter already throws.
    internal GridTableStylesCollection()
    {
    }

    public DataGridTableStyle this[int index] => throw new PlatformNotSupportedException();

    public DataGridTableStyle this[string tableName] => throw new PlatformNotSupportedException();

    object? IList.this[int index]
    {
        get => throw new PlatformNotSupportedException();
        set => throw new PlatformNotSupportedException();
    }

    bool IList.IsFixedSize => throw new PlatformNotSupportedException();

    int IList.Add(object? value) => throw new PlatformNotSupportedException();

    bool IList.Contains(object? value) => throw new PlatformNotSupportedException();

    int IList.IndexOf(object? value) => throw new PlatformNotSupportedException();

    void IList.Insert(int index, object? value) => throw new PlatformNotSupportedException();

    void IList.Remove(object? value) => throw new PlatformNotSupportedException();

    // Clear() and RemoveAt(int) below already match IList's signatures, so
    // they satisfy the interface implicitly; no explicit forwarder needed.

    public event CollectionChangeEventHandler? CollectionChanged
    {
        add => throw new PlatformNotSupportedException();
        remove => throw new PlatformNotSupportedException();
    }

    public virtual int Add(DataGridTableStyle table) => throw new PlatformNotSupportedException();

    public virtual void AddRange(DataGridTableStyle[] tables) => throw new PlatformNotSupportedException();

    public void Clear() => throw new PlatformNotSupportedException();

    public bool Contains(DataGridTableStyle table) => throw new PlatformNotSupportedException();

    public bool Contains(string name) => throw new PlatformNotSupportedException();

    protected void OnCollectionChanged(CollectionChangeEventArgs e) => throw new PlatformNotSupportedException();

    public void Remove(DataGridTableStyle table) => throw new PlatformNotSupportedException();

    public void RemoveAt(int index) => throw new PlatformNotSupportedException();
}
