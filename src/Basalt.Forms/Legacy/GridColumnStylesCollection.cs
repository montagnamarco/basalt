using System.Collections;
using System.ComponentModel;

#pragma warning disable WFDEV006
#pragma warning disable CS0618

namespace System.Windows.Forms;

[Obsolete("`DataGrid` is provided for binary compatibility with .NET Framework and is not intended to be used directly from your code. Use `DataGridView` instead.", false, DiagnosticId = "WFDEV006", UrlFormat = "https://aka.ms/winforms-warnings/{0}")]
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
[ListBindable(false)]
public class GridColumnStylesCollection : BaseCollection, IList
{
    // Microsoft's reference signature has no public constructor: only
    // DataGridTableStyle can create one, and its own getter already throws.
    internal GridColumnStylesCollection()
    {
    }

    public DataGridColumnStyle this[int index] => throw new PlatformNotSupportedException();

    public DataGridColumnStyle this[string columnName] => throw new PlatformNotSupportedException();

    public DataGridColumnStyle this[PropertyDescriptor propertyDesciptor] => throw new PlatformNotSupportedException();

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

    public virtual int Add(DataGridColumnStyle column) => throw new PlatformNotSupportedException();

    public void AddRange(DataGridColumnStyle[] columns) => throw new PlatformNotSupportedException();

    public void Clear() => throw new PlatformNotSupportedException();

    public bool Contains(PropertyDescriptor propertyDescriptor) => throw new PlatformNotSupportedException();

    public bool Contains(DataGridColumnStyle column) => throw new PlatformNotSupportedException();

    public bool Contains(string name) => throw new PlatformNotSupportedException();

    public int IndexOf(DataGridColumnStyle element) => throw new PlatformNotSupportedException();

    protected void OnCollectionChanged(CollectionChangeEventArgs e) => throw new PlatformNotSupportedException();

    public void Remove(DataGridColumnStyle column) => throw new PlatformNotSupportedException();

    public void RemoveAt(int index) => throw new PlatformNotSupportedException();

    public void ResetPropertyDescriptors() => throw new PlatformNotSupportedException();
}
