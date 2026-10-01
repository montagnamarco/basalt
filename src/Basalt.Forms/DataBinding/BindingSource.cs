using System.Collections;
using System.ComponentModel;
using System.Data;

namespace System.Windows.Forms;

/// <summary>
/// Wraps any data source (an IList, a DataTable/DataSet through IListSource, a plain
/// IEnumerable, a single business object, a Type, or another BindingSource) behind one
/// list-like, position-aware component: the Windows Forms designer's usual go-between for
/// a form and its data. Owns its own <see cref="CurrencyManager"/>, so every control bound
/// through this BindingSource (and a <see cref="BindingNavigator"/> pointed at it) shares
/// the same current row.
/// </summary>
public class BindingSource : Component, IList, ICollection, IEnumerable, IBindingList, IBindingListView,
    ICancelAddNew, ISupportInitialize, ISupportInitializeNotification, ITypedList, ICurrencyManagerProvider
{
    private object? _dataSource;
    private string _dataMember = string.Empty;
    private CurrencyManager? _currencyManager;
    private Type _itemType = typeof(object);

    /// <summary>Dispose unhooked the manager's events; a later DataSource hooks them again.</summary>
    private bool _managerDetached;
    private bool? _allowNewSetByUser;
    private bool _raiseListChangedEvents = true;
    private string? _filter;
    private string? _sort;
    private int _initializeCount;
    private bool _initialized = true;
    private int _pendingNewIndex = -1;
    private BindingSource? _chainedParent;

    public BindingSource()
    {
        ResetList();
    }

    public BindingSource(IContainer container)
        : this()
    {
        container?.Add(this);
    }

    public BindingSource(object? dataSource, string dataMember)
    {
        _dataSource = dataSource;
        _dataMember = dataMember ?? string.Empty;
        ResetList();
    }

    [Browsable(false)]
    public virtual bool AllowEdit
    {
        get
        {
            if (List is IBindingList bindingList) return bindingList.AllowEdit;

            return !List.IsReadOnly;
        }
    }

    public virtual bool AllowNew
    {
        get
        {
            if (_allowNewSetByUser.HasValue) return _allowNewSetByUser.Value;

            if (List is IBindingList bindingList) return bindingList.AllowNew;
            if (List.IsReadOnly || List.IsFixedSize) return false;

            // Without an AddingNew handler to supply the item, AddNew has to construct one itself.
            return AddingNew is not null || _itemType.IsValueType || _itemType.GetConstructor(Type.EmptyTypes) is not null;
        }
        set => _allowNewSetByUser = value;
    }

    [Browsable(false)]
    public virtual bool AllowRemove
    {
        get
        {
            if (List is IBindingList bindingList) return bindingList.AllowRemove;

            return !List.IsReadOnly && !List.IsFixedSize;
        }
    }

    [Browsable(false)]
    public virtual int Count => List.Count;

    [Browsable(false)]
    public virtual CurrencyManager? CurrencyManager => _currencyManager;

    [Browsable(false)]
    public object? Current => _currencyManager?.Current;

    public string DataMember
    {
        get => _dataMember;
        set
        {
            _dataMember = value ?? string.Empty;
            OnDataMemberChanged(EventArgs.Empty);

            if (_initializeCount == 0) ResetList();
        }
    }

    public object? DataSource
    {
        get => _dataSource;
        set
        {
            _dataSource = value;
            OnDataSourceChanged(EventArgs.Empty);

            if (_initializeCount == 0) ResetList();
        }
    }

    public virtual string? Filter
    {
        get => _filter;
        set
        {
            if (List is IBindingListView view)
            {
                view.Filter = value;
                _filter = value;
                return;
            }

            if (string.IsNullOrEmpty(value))
            {
                _filter = null;
                return;
            }

            throw new NotSupportedException("The underlying list does not support filtering.");
        }
    }

    [Browsable(false)]
    public bool IsBindingSuspended => _currencyManager?.IsBindingSuspended ?? false;

    [Browsable(false)]
    public virtual bool IsFixedSize => List.IsFixedSize;

    [Browsable(false)]
    public virtual bool IsReadOnly => List.IsReadOnly;

    [Browsable(false)]
    public virtual bool IsSorted => List is IBindingList { IsSorted: true };

    [Browsable(false)]
    public virtual bool IsSynchronized => List.IsSynchronized;

    [Browsable(false)]
    public virtual object? this[int index]
    {
        get => List[index];
        set
        {
            List[index] = value;
            NotifyIfPlainList(ListChangedType.ItemChanged, index);
        }
    }

    [Browsable(false)]
    public IList List => _currencyManager!.List;

    public int Position
    {
        get => _currencyManager?.Position ?? -1;
        set
        {
            if (_currencyManager is not null) _currencyManager.Position = value;
        }
    }

    [Browsable(false)]
    public bool RaiseListChangedEvents
    {
        get => _raiseListChangedEvents;
        set => _raiseListChangedEvents = value;
    }

    public string? Sort
    {
        get => _sort;
        set
        {
            _sort = value;

            if (string.IsNullOrEmpty(value))
            {
                RemoveSort();
                return;
            }

            ApplySortString(value);
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual ListSortDescriptionCollection SortDescriptions =>
        List is IBindingListView view ? view.SortDescriptions : new ListSortDescriptionCollection([]);

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual ListSortDirection SortDirection => List is IBindingList { IsSorted: true } bindingList ? bindingList.SortDirection : ListSortDirection.Ascending;

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual PropertyDescriptor? SortProperty => List is IBindingList { IsSorted: true } bindingList ? bindingList.SortProperty : null;

    [Browsable(false)]
    public virtual bool SupportsAdvancedSorting => List is IBindingListView { SupportsAdvancedSorting: true };

    [Browsable(false)]
    public virtual bool SupportsChangeNotification => true;

    [Browsable(false)]
    public virtual bool SupportsFiltering => List is IBindingListView { SupportsFiltering: true };

    [Browsable(false)]
    public virtual bool SupportsSearching => List is IBindingList { SupportsSearching: true };

    [Browsable(false)]
    public virtual bool SupportsSorting => List is IBindingList { SupportsSorting: true };

    [Browsable(false)]
    public virtual object SyncRoot => List.SyncRoot;

    public event AddingNewEventHandler? AddingNew;

    public event BindingCompleteEventHandler? BindingComplete;

    public event EventHandler? CurrentChanged;

    public event EventHandler? CurrentItemChanged;

    public event BindingManagerDataErrorEventHandler? DataError;

    public event EventHandler? DataMemberChanged;

    public event EventHandler? DataSourceChanged;

    public event ListChangedEventHandler? ListChanged;

    public event EventHandler? PositionChanged;

    public virtual int Add(object? value)
    {
        var index = List.Add(value);
        NotifyIfPlainList(ListChangedType.ItemAdded, index);

        return index;
    }

    public virtual object AddNew()
    {
        if (!AllowNew) throw new InvalidOperationException("AddNew is not allowed on this BindingSource.");

        EndEdit();

        var addingNewArgs = new AddingNewEventArgs();
        OnAddingNew(addingNewArgs);

        object newItem;
        int index;

        if (addingNewArgs.NewObject is { } suppliedItem)
        {
            newItem = suppliedItem;
            index = List.Add(newItem);
            if (index < 0) index = List.IndexOf(newItem);
            NotifyIfPlainList(ListChangedType.ItemAdded, index);
        }
        else if (List is IBindingList { AllowNew: true } bindingList)
        {
            newItem = bindingList.AddNew()!;
            index = List.IndexOf(newItem);
        }
        else
        {
            newItem = Activator.CreateInstance(_itemType) ?? throw new InvalidOperationException($"Cannot create a new '{_itemType}'.");
            index = List.Add(newItem);
            NotifyIfPlainList(ListChangedType.ItemAdded, index);
        }

        Position = index;
        _pendingNewIndex = index;

        return newItem;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual void ApplySort(PropertyDescriptor property, ListSortDirection sort)
    {
        if (List is not IBindingList { SupportsSorting: true } bindingList) throw new NotSupportedException("The underlying list does not support sorting.");

        bindingList.ApplySort(property, sort);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual void ApplySort(ListSortDescriptionCollection sorts)
    {
        if (List is not IBindingListView view) throw new NotSupportedException("The underlying list does not support multi-column sorting.");

        view.ApplySort(sorts);
    }

    public void CancelEdit() => _currencyManager?.CancelCurrentEdit();

    public virtual void Clear()
    {
        List.Clear();
        NotifyIfPlainList(ListChangedType.Reset, -1);
    }

    public virtual bool Contains(object? value) => List.Contains(value);

    public virtual void CopyTo(Array arr, int index) => List.CopyTo(arr, index);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DetachCurrencyManager();
            DetachChainedParent();

            // Kept, so List and Count still answer; a DataSource set later re-attaches it.
            _managerDetached = true;
        }

        base.Dispose(disposing);
    }

    public void EndEdit() => _currencyManager?.EndCurrentEdit();

    public int Find(string propertyName, object key)
    {
        var descriptor = TypeDescriptor.GetProperties(_itemType).Find(propertyName, true)
            ?? throw new ArgumentException($"'{propertyName}' is not a property of '{_itemType}'.", nameof(propertyName));

        return Find(descriptor, key);
    }

    public virtual int Find(PropertyDescriptor prop, object key)
    {
        if (List is not IBindingList { SupportsSearching: true } bindingList) throw new NotSupportedException("The underlying list does not support searching.");

        return bindingList.Find(prop, key);
    }

    public virtual IEnumerator GetEnumerator() => List.GetEnumerator();

    public virtual PropertyDescriptorCollection GetItemProperties(PropertyDescriptor[]? listAccessors)
    {
        if (List is ITypedList typedList) return typedList.GetItemProperties(listAccessors);

        return TypeDescriptor.GetProperties(_itemType);
    }

    public virtual string GetListName(PropertyDescriptor[]? listAccessors) =>
        List is ITypedList typedList ? typedList.GetListName(listAccessors) : _itemType.Name;

    public virtual CurrencyManager GetRelatedCurrencyManager(string? dataMember)
    {
        if (string.IsNullOrEmpty(dataMember)) return _currencyManager!;

        var current = Current;
        if (current is null) return new CurrencyManager(new ArrayList());

        var descriptor = TypeDescriptor.GetProperties(current).Find(dataMember, true)
            ?? throw new ArgumentException($"'{dataMember}' is not a property of '{current.GetType()}'.", nameof(dataMember));

        return new CurrencyManager(ResolveDirect(descriptor.GetValue(current)));
    }

    public virtual int IndexOf(object? value) => List.IndexOf(value);

    public virtual void Insert(int index, object? value)
    {
        List.Insert(index, value);
        NotifyIfPlainList(ListChangedType.ItemAdded, index);
    }

    public void MoveFirst() => Position = 0;

    public void MoveLast() => Position = Count - 1;

    public void MoveNext() => Position++;

    public void MovePrevious() => Position--;

    protected virtual void OnAddingNew(AddingNewEventArgs e) => AddingNew?.Invoke(this, e);

    protected virtual void OnBindingComplete(BindingCompleteEventArgs e) => BindingComplete?.Invoke(this, e);

    protected virtual void OnCurrentChanged(EventArgs e) => CurrentChanged?.Invoke(this, e);

    protected virtual void OnCurrentItemChanged(EventArgs e) => CurrentItemChanged?.Invoke(this, e);

    protected virtual void OnDataError(BindingManagerDataErrorEventArgs e) => DataError?.Invoke(this, e);

    protected virtual void OnDataMemberChanged(EventArgs e) => DataMemberChanged?.Invoke(this, e);

    protected virtual void OnDataSourceChanged(EventArgs e) => DataSourceChanged?.Invoke(this, e);

    protected virtual void OnListChanged(ListChangedEventArgs e) => ListChanged?.Invoke(this, e);

    protected virtual void OnPositionChanged(EventArgs e) => PositionChanged?.Invoke(this, e);

    public virtual void Remove(object? value)
    {
        var index = List.IndexOf(value);
        if (index < 0) return;

        List.RemoveAt(index);
        NotifyIfPlainList(ListChangedType.ItemDeleted, index);
    }

    public virtual void RemoveAt(int index)
    {
        List.RemoveAt(index);
        NotifyIfPlainList(ListChangedType.ItemDeleted, index);
    }

    public void RemoveCurrent()
    {
        if (Position < 0) throw new InvalidOperationException("There is no current item to remove.");

        RemoveAt(Position);
    }

    public virtual void RemoveFilter() => Filter = null;

    public virtual void RemoveSort()
    {
        switch (List)
        {
            case IBindingListView view:
                view.RemoveSort();
                break;

            case IBindingList { SupportsSorting: true } bindingList:
                bindingList.RemoveSort();
                break;
        }

        _sort = null;
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public virtual void ResetAllowNew() => _allowNewSetByUser = null;

    public void ResetBindings(bool metadataChanged)
    {
        _currencyManager?.Refresh();
        RaiseListChanged(new ListChangedEventArgs(metadataChanged ? ListChangedType.PropertyDescriptorAdded : ListChangedType.Reset, -1));
    }

    public void ResetCurrentItem()
    {
        _currencyManager?.Refresh();
        RaiseListChanged(new ListChangedEventArgs(ListChangedType.ItemChanged, Position));
    }

    public void ResetItem(int itemIndex)
    {
        if (itemIndex == Position) _currencyManager?.Refresh();

        RaiseListChanged(new ListChangedEventArgs(ListChangedType.ItemChanged, itemIndex));
    }

    public void ResumeBinding() => _currencyManager?.ResumeBinding();

    public void SuspendBinding() => _currencyManager?.SuspendBinding();

    void IBindingList.AddIndex(PropertyDescriptor property)
    {
        // No secondary index to maintain: Find() always walks the underlying list or delegates to it.
    }

    void IBindingList.RemoveIndex(PropertyDescriptor property)
    {
    }

    // A list that tracks its own pending new item (a DataView's new row) cancels or commits it
    // itself; for a plain list the BindingSource removes the item it added.
    void ICancelAddNew.CancelNew(int itemIndex)
    {
        if (List is ICancelAddNew inner)
        {
            inner.CancelNew(itemIndex);
        }
        else if (_pendingNewIndex == itemIndex)
        {
            RemoveAt(itemIndex);
        }

        if (_pendingNewIndex == itemIndex) _pendingNewIndex = -1;
    }

    void ICancelAddNew.EndNew(int itemIndex)
    {
        if (List is ICancelAddNew inner) inner.EndNew(itemIndex);
        if (_pendingNewIndex == itemIndex) _pendingNewIndex = -1;
    }

    void ISupportInitialize.BeginInit()
    {
        _initializeCount++;
        _initialized = false;
    }

    void ISupportInitialize.EndInit()
    {
        if (_initializeCount > 0) _initializeCount--;
        if (_initializeCount > 0) return;

        ResetList();
        _initialized = true;
        Initialized?.Invoke(this, EventArgs.Empty);
    }

    bool ISupportInitializeNotification.IsInitialized => _initialized;

    private event EventHandler? Initialized;

    event EventHandler? ISupportInitializeNotification.Initialized
    {
        add => Initialized += value;
        remove => Initialized -= value;
    }

    private void ApplySortString(string sort)
    {
        // DataView parses "Name ASC, Age DESC" itself through its own Sort property: IBindingListView
        // has no such string property, only the PropertyDescriptor-based ApplySort below.
        if (List is DataView dataView)
        {
            dataView.Sort = sort;
            return;
        }

        if (List is IBindingListView view)
        {
            view.ApplySort(ParseSortDescriptions(sort));
            return;
        }

        if (List is not IBindingList { SupportsSorting: true } bindingList) throw new NotSupportedException("The underlying list does not support sorting.");

        // A single "Name" or "Name DESC" clause: IBindingList.ApplySort only takes one property.
        var (descriptor, direction) = ParseSortClause(sort.Split(',')[0]);
        bindingList.ApplySort(descriptor, direction);
    }

    private ListSortDescriptionCollection ParseSortDescriptions(string sort)
    {
        var clauses = sort.Split(',');
        var descriptions = new ListSortDescription[clauses.Length];

        for (var i = 0; i < clauses.Length; i++)
        {
            var (descriptor, direction) = ParseSortClause(clauses[i]);
            descriptions[i] = new ListSortDescription(descriptor, direction);
        }

        return new ListSortDescriptionCollection(descriptions);
    }

    private (PropertyDescriptor Descriptor, ListSortDirection Direction) ParseSortClause(string clause)
    {
        var parts = clause.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var propertyName = parts[0];
        var direction = parts.Length > 1 && parts[1].Equals("DESC", StringComparison.OrdinalIgnoreCase)
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        var descriptor = TypeDescriptor.GetProperties(_itemType).Find(propertyName, true)
            ?? throw new ArgumentException($"'{propertyName}' is not a property of '{_itemType}'.", nameof(clause));

        return (descriptor, direction);
    }

    private void AttachChainedParent()
    {
        if (_dataMember.Length == 0 || _dataSource is not BindingSource parent) return;

        _chainedParent = parent;
        _chainedParent.PositionChanged += HandleChainedParentPositionChanged;
    }

    private void AttachCurrencyManager()
    {
        var manager = _currencyManager!;
        manager.PositionChanged += HandleManagerPositionChanged;
        manager.CurrentChanged += HandleManagerCurrentChanged;
        manager.CurrentItemChanged += HandleManagerCurrentItemChanged;
        manager.BindingComplete += HandleManagerBindingComplete;
        manager.DataError += HandleManagerDataError;
        manager.ListChanged += HandleManagerListChanged;
    }

    private void DetachChainedParent()
    {
        if (_chainedParent is null) return;

        _chainedParent.PositionChanged -= HandleChainedParentPositionChanged;
        _chainedParent = null;
    }

    private void DetachCurrencyManager()
    {
        if (_currencyManager is null) return;

        _currencyManager.PositionChanged -= HandleManagerPositionChanged;
        _currencyManager.CurrentChanged -= HandleManagerCurrentChanged;
        _currencyManager.CurrentItemChanged -= HandleManagerCurrentItemChanged;
        _currencyManager.BindingComplete -= HandleManagerBindingComplete;
        _currencyManager.DataError -= HandleManagerDataError;
        _currencyManager.ListChanged -= HandleManagerListChanged;
    }

    private void HandleChainedParentPositionChanged(object? sender, EventArgs e) => ResetList();

    private void HandleManagerBindingComplete(object? sender, BindingCompleteEventArgs e) => OnBindingComplete(e);

    private void HandleManagerCurrentChanged(object? sender, EventArgs e) => OnCurrentChanged(e);

    private void HandleManagerCurrentItemChanged(object? sender, EventArgs e) => OnCurrentItemChanged(e);

    private void HandleManagerDataError(object? sender, BindingManagerDataErrorEventArgs e) => OnDataError(e);

    private void HandleManagerListChanged(object? sender, ListChangedEventArgs e) => RaiseListChanged(e);

    private void HandleManagerPositionChanged(object? sender, EventArgs e)
    {
        if (_pendingNewIndex >= 0 && _currencyManager!.Position != _pendingNewIndex) _pendingNewIndex = -1;

        OnPositionChanged(e);
    }

    /// <summary>Appends an item to a plain (non-IBindingList) list and fires ListChanged manually: nothing else will.</summary>
    private void NotifyIfPlainList(ListChangedType type, int index)
    {
        if (List is IBindingList) return;

        _currencyManager?.Refresh();
        RaiseListChanged(new ListChangedEventArgs(type, index));
    }

    private void RaiseListChanged(ListChangedEventArgs e)
    {
        if (!_raiseListChangedEvents) return;

        OnListChanged(e);
    }

    /// <summary>Turns a raw DataSource value into the concrete IList a CurrencyManager can walk.</summary>
    private IList ResolveDirect(object? source)
    {
        if (source is null) return new ArrayList();
        if (source is Type) return new ArrayList();
        if (source is ICurrencyManagerProvider provider) return provider.GetRelatedCurrencyManager(null).List;

        var resolved = ListBindingHelper.GetList(source);

        if (resolved is IList concreteList) return concreteList;

        if (resolved is IEnumerable enumerable)
        {
            var snapshot = new ArrayList();
            foreach (var item in enumerable) snapshot.Add(item);

            return snapshot;
        }

        return new ArrayList { resolved };
    }

    /// <summary>Navigates from a chained BindingSource's current row to the list named by DataMember.</summary>
    private IList ResolveFromDataMember(string dataMember)
    {
        if (_dataSource is not BindingSource parent)
        {
            throw new ArgumentException(
                $"DataMember '{dataMember}' requires a DataSource that already tracks a current row, such as another BindingSource.",
                nameof(dataMember));
        }

        var current = parent.Current;
        if (current is null) return new ArrayList();

        var descriptor = TypeDescriptor.GetProperties(current).Find(dataMember, true)
            ?? throw new ArgumentException($"'{dataMember}' is not a property of '{current.GetType()}'.", nameof(dataMember));

        return ResolveDirect(descriptor.GetValue(current));
    }

    /// <summary>Rebuilds the CurrencyManager from the current DataSource/DataMember: called on every relevant property set.</summary>
    private void ResetList()
    {
        DetachChainedParent();

        var list = ResolveList();

        _itemType = _dataSource is Type explicitType && _dataMember.Length == 0 ? explicitType : InferItemType(list);

        // One manager for the BindingSource's whole life: controls bound through BindingContext
        // resolved it once, so a new list is given to the same manager rather than to a new one.
        AttachChainedParent();

        if (_currencyManager is null)
        {
            _currencyManager = new CurrencyManager(list);
            AttachCurrencyManager();
            RaiseListChanged(new ListChangedEventArgs(ListChangedType.Reset, -1));
        }
        else
        {
            if (_managerDetached)
            {
                AttachCurrencyManager();
                _managerDetached = false;
            }

            // The manager reports the reset itself, and ListChanged relays it once.
            _currencyManager.SetDataSource(list);
        }
    }

    private IList ResolveList()
    {
        if (_dataMember.Length == 0) return ResolveDirect(_dataSource);

        if (_dataSource is DataSet dataSet && dataSet.Tables.Contains(_dataMember)) return dataSet.Tables[_dataMember]!.DefaultView;

        return ResolveFromDataMember(_dataMember);
    }

    private static Type InferItemType(IList list)
    {
        if (list is ITypedList typedList)
        {
            var properties = typedList.GetItemProperties(null);
            if (properties.Count > 0) return properties[0].ComponentType;
        }

        foreach (var candidate in list.GetType().GetInterfaces())
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IList<>)) return candidate.GetGenericArguments()[0];
        }

        return list.Count > 0 ? list[0]!.GetType() : typeof(object);
    }
}
