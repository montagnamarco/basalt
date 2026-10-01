using System.Collections;

namespace System.Windows.Forms;

/// <summary>
/// The read-only face every WinForms collection that is not a list of
/// controls shares: a count, an enumerator and a sync root, backed by
/// whatever array the derived collection keeps.
/// </summary>
/// <remarks>
/// Not created for its own sake: <see cref="BindingsCollection"/> needs it as
/// a base per Microsoft's public surface. Other families that add a
/// Microsoft collection deriving from BaseCollection (DataGridView's cell,
/// column and selection collections; the old DataGrid's style collections)
/// should reuse this type rather than redeclare it.
/// </remarks>
public class BaseCollection : MarshalByRefObject, ICollection, IEnumerable
{
    public virtual int Count => List?.Count ?? 0;

    public bool IsReadOnly => false;

    public bool IsSynchronized => false;

    protected virtual ArrayList? List => null;

    public object SyncRoot => this;

    public void CopyTo(Array ar, int index) => List?.CopyTo(ar, index);

    public IEnumerator GetEnumerator() => (List ?? new ArrayList()).GetEnumerator();
}
