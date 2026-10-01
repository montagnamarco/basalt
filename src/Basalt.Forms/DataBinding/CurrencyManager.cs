using System.Collections;
using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>A position in an IList (plain, IBindingList or the IBindingList a DataView exposes), and the bindings that follow it.</summary>
public class CurrencyManager : BindingManagerBase
{
    private readonly IList _list;

    // Not public in Microsoft's CurrencyManager either: BindingContext is the only place
    // that creates one, keyed by data source and path so controls on the same container share it.
    internal CurrencyManager(object dataSource)
    {
        _list = ToList(dataSource);
        finalType = InferItemType(_list);
        listposition = _list.Count > 0 ? 0 : -1;

        WireList();
        UpdateIsBinding();
    }

    protected Type finalType;

    protected int listposition;

    public override int Count => _list.Count;

    public override object? Current => Position >= 0 && Position < Count ? _list[Position] : null;

    public IList List => _list;

    public override int Position
    {
        get => listposition;
        set
        {
            if (Count == 0)
            {
                ChangePosition(-1);
                return;
            }

            ChangePosition(Math.Clamp(value, 0, Count - 1));
        }
    }

    public event ItemChangedEventHandler? ItemChanged;

    public event ListChangedEventHandler? ListChanged;

    public event EventHandler? MetaDataChanged;

    public override void AddNew()
    {
        if (_list is not IBindingList { AllowNew: true } bindingList)
        {
            if (_list.IsFixedSize || _list.IsReadOnly) throw new InvalidOperationException("The list does not support adding new items.");

            var added = Activator.CreateInstance(finalType);
            var index = _list.Add(added);
            Position = index;

            return;
        }

        var newItem = bindingList.AddNew();
        Position = _list.IndexOf(newItem);
    }

    public override void CancelCurrentEdit()
    {
        if (Current is IEditableObject editable) editable.CancelEdit();

        OnItemChanged(new ItemChangedEventArgs(Position));
        PushData();
    }

    protected void CheckEmpty()
    {
        if (Count == 0) throw new InvalidOperationException("The list has no items.");
    }

    public override void EndCurrentEdit()
    {
        if (Current is IEditableObject editable) editable.EndEdit();
    }

    public override PropertyDescriptorCollection GetItemProperties() => TypeDescriptor.GetProperties(finalType);

    protected override string GetListName(ArrayList? listAccessors) => finalType.Name;

    protected override void OnCurrentChanged(EventArgs e) => onCurrentChangedHandler?.Invoke(this, e);

    protected override void OnCurrentItemChanged(EventArgs e) => RaiseCurrentItemChanged(e);

    protected virtual void OnItemChanged(ItemChangedEventArgs e) => ItemChanged?.Invoke(this, e);

    protected void OnMetaDataChanged(EventArgs e) => MetaDataChanged?.Invoke(this, e);

    protected virtual void OnPositionChanged(EventArgs e) => onPositionChangedHandler?.Invoke(this, e);

    /// <summary>Re-reads Count and Current without assuming the underlying list told us anything changed.</summary>
    public void Refresh()
    {
        var position = Math.Min(listposition, Count - 1);
        if (position == -1 && Count > 0) position = 0;

        AfterListChange(position, -1);
        OnCurrentItemChanged(EventArgs.Empty);
    }

    public override void RemoveAt(int index)
    {
        _list.RemoveAt(index);

        // A binding list reports the removal itself, through OnListChanged.
        if (_list is IBindingList) return;

        ItemRemoved(index);
    }

    /// <summary>
    /// After a removal the current record stays current: a row removed before
    /// it moves the position back by one, so bound controls keep editing the
    /// same record.
    /// </summary>
    private void ItemRemoved(int index)
    {
        var position = listposition;
        if (index < position) position--;
        if (position >= Count) position = Count - 1;

        AfterListChange(position, index);
    }

    /// <summary>A row inserted before the current one moves the position forward, keeping the record current.</summary>
    private void ItemAdded(int index)
    {
        var position = listposition;
        if (position == -1 && Count > 0) position = 0;
        else if (index >= 0 && index <= position && Count > 1) position++;

        AfterListChange(position, index);
    }

    private void AfterListChange(int position, int changedIndex)
    {
        var moved = position != listposition;
        listposition = position;

        if (moved) OnPositionChanged(EventArgs.Empty);

        OnItemChanged(new ItemChangedEventArgs(changedIndex));
        OnCurrentChanged(EventArgs.Empty);
        PushData();
    }

    public override void ResumeBinding() => EndSuspendBinding();

    public override void SuspendBinding() => BeginSuspendBinding();

    protected override void UpdateIsBinding() => PushData();

    private void ChangePosition(int newPosition)
    {
        if (newPosition == listposition) return;

        listposition = newPosition;
        OnPositionChanged(EventArgs.Empty);
        OnCurrentChanged(EventArgs.Empty);
        OnCurrentItemChanged(EventArgs.Empty);
        PushData();
    }

    private void WireList()
    {
        if (_list is IBindingList bindingList) bindingList.ListChanged += OnListChanged;
    }

    private void OnListChanged(object? sender, ListChangedEventArgs e)
    {
        ListChanged?.Invoke(this, e);

        switch (e.ListChangedType)
        {
            case ListChangedType.ItemDeleted:
                ItemRemoved(e.NewIndex);
                break;

            case ListChangedType.ItemAdded:
                ItemAdded(e.NewIndex);
                break;

            default:
            {
                var position = listposition;
                if (position == -1 && Count > 0) position = 0;
                if (position >= Count) position = Count - 1;

                AfterListChange(position, e.NewIndex);
                break;
            }
        }
    }

    private static IList ToList(object dataSource)
    {
        var list = ListBindingHelper.GetList(dataSource);

        return list as IList ?? throw new ArgumentException("The data source does not resolve to an IList.", nameof(dataSource));
    }

    private static Type InferItemType(IList list)
    {
        if (list is ITypedList typedList)
        {
            var properties = typedList.GetItemProperties(null);
            if (properties.Count > 0) return properties[0].ComponentType;
        }

        var listType = list.GetType();

        foreach (var candidate in listType.GetInterfaces())
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IList<>)) return candidate.GetGenericArguments()[0];
        }

        return list.Count > 0 ? list[0]!.GetType() : typeof(object);
    }
}
