using System.Collections;
using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>The bindings owned by one BindingManagerBase, or by one control's ControlBindingsCollection.</summary>
public class BindingsCollection : BaseCollection
{
    private readonly ArrayList _list = [];

    // Microsoft does not let user code construct this directly either: a BindingManagerBase
    // owns one, and ControlBindingsCollection (same assembly) is the only public subclass.
    internal BindingsCollection()
    {
    }

    public override int Count => _list.Count;

    public Binding this[int index] => (Binding)_list[index]!;

    protected override ArrayList List => _list;

    public event CollectionChangeEventHandler? CollectionChanged;

    public event CollectionChangeEventHandler? CollectionChanging;

    /// <summary>Lets a BindingManagerBase, which is not a subclass, reach the otherwise protected Add/Remove.</summary>
    internal void AddInternal(Binding binding) => Add(binding);

    internal void RemoveInternal(Binding binding) => Remove(binding);

    protected void Add(Binding binding)
    {
        OnCollectionChanging(new CollectionChangeEventArgs(CollectionChangeAction.Add, binding));
        AddCore(binding);
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Add, binding));
    }

    protected virtual void AddCore(Binding dataBinding)
    {
        ArgumentNullException.ThrowIfNull(dataBinding);

        _list.Add(dataBinding);
    }

    protected void Clear()
    {
        OnCollectionChanging(new CollectionChangeEventArgs(CollectionChangeAction.Refresh, null));
        ClearCore();
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Refresh, null));
    }

    protected virtual void ClearCore() => _list.Clear();

    protected virtual void OnCollectionChanged(CollectionChangeEventArgs ccevent) => CollectionChanged?.Invoke(this, ccevent);

    protected virtual void OnCollectionChanging(CollectionChangeEventArgs e) => CollectionChanging?.Invoke(this, e);

    protected void Remove(Binding binding)
    {
        OnCollectionChanging(new CollectionChangeEventArgs(CollectionChangeAction.Remove, binding));
        RemoveCore(binding);
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Remove, binding));
    }

    protected void RemoveAt(int index) => Remove(this[index]);

    protected virtual void RemoveCore(Binding dataBinding) => _list.Remove(dataBinding);

    protected bool ShouldSerializeMyAll() => Count > 0;
}
