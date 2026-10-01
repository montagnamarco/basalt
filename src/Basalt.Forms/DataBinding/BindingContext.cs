using System.Collections;
using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// One BindingManagerBase per data source and navigation path ("Customers"
/// then "Customers.Orders"), shared by every control in the same container
/// that binds to the same source: two TextBoxes bound to the same list
/// position together, because they share the same CurrencyManager.
/// </summary>
[DefaultEvent("CollectionChanged")]
public class BindingContext : ICollection, IEnumerable
{
    /// <summary>
    /// Keyed by data source identity (never by value, two different objects
    /// never collide) and by path; held weakly so a data source that is
    /// otherwise unreachable does not keep its manager, and this context,
    /// alive forever.
    /// </summary>
    private readonly Dictionary<Key, WeakReference<BindingManagerBase>> _managers = new(KeyComparer.Instance);

    public bool IsReadOnly => false;

    public BindingManagerBase this[object dataSource] => this[dataSource, string.Empty];

    public BindingManagerBase this[object dataSource, string? dataMember] => GetOrCreateManager(dataSource, dataMember ?? string.Empty);

    public event CollectionChangeEventHandler? CollectionChanged;

    int ICollection.Count => _managers.Count;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    protected void Add(object dataSource, BindingManagerBase listManager) => AddCore(dataSource, listManager);

    protected virtual void AddCore(object dataSource, BindingManagerBase listManager)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(listManager);

        _managers[new Key(dataSource, string.Empty)] = new WeakReference<BindingManagerBase>(listManager);
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Add, listManager));
    }

    protected void Clear() => ClearCore();

    protected virtual void ClearCore()
    {
        _managers.Clear();
        OnCollectionChanged(new CollectionChangeEventArgs(CollectionChangeAction.Refresh, null));
    }

    public bool Contains(object dataSource) => Contains(dataSource, string.Empty);

    public bool Contains(object dataSource, string? dataMember) =>
        _managers.TryGetValue(new Key(dataSource, dataMember ?? string.Empty), out var reference) && reference.TryGetTarget(out _);

    protected virtual void OnCollectionChanged(CollectionChangeEventArgs ccevent) => CollectionChanged?.Invoke(this, ccevent);

    protected void Remove(object dataSource) => RemoveCore(dataSource);

    protected virtual void RemoveCore(object dataSource)
    {
        var keysForSource = _managers.Keys.Where(key => ReferenceEquals(key.DataSource, dataSource)).ToList();
        foreach (var key in keysForSource) _managers.Remove(key);
    }

    /// <summary>Re-attaches a binding to the manager its own BindingContext now resolves to.</summary>
    public static void UpdateBinding(BindingContext? newBindingContext, Binding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        binding.Unbind();
        if (newBindingContext is not null) binding.TryBind();
    }

    void ICollection.CopyTo(Array array, int index)
    {
        foreach (var reference in _managers.Values)
        {
            if (reference.TryGetTarget(out var manager)) array.SetValue(manager, index++);
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        foreach (var reference in _managers.Values)
        {
            if (reference.TryGetTarget(out var manager)) yield return manager;
        }
    }

    private BindingManagerBase GetOrCreateManager(object dataSource, string dataMember)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        RemoveCollected();

        var key = new Key(dataSource, dataMember);
        if (_managers.TryGetValue(key, out var existingReference) && existingReference.TryGetTarget(out var existingManager))
        {
            return existingManager;
        }

        var manager = CreateManager(dataSource, dataMember);

        // AddCore (Microsoft's protected extension point) only ever keys by the data source
        // itself: it is the right path for the common, empty-path case. A navigated path such
        // as "Orders" has no protected hook to go through, so it is cached directly.
        if (dataMember.Length == 0) AddCore(dataSource, manager);
        else _managers[key] = new WeakReference<BindingManagerBase>(manager);

        return manager;
    }

    /// <summary>Builds the manager for one data source and path, recursing one segment at a time for "A.B.C".</summary>
    private BindingManagerBase CreateManager(object dataSource, string dataMember)
    {
        if (dataMember.Length == 0) return CreateManagerForList(dataSource);

        var lastDot = dataMember.LastIndexOf('.');
        var parentPath = lastDot < 0 ? string.Empty : dataMember[..lastDot];
        var segment = lastDot < 0 ? dataMember : dataMember[(lastDot + 1)..];

        var parent = this[dataSource, parentPath];
        var current = parent.Current;
        if (current is null) return new PropertyManager();

        var descriptor = TypeDescriptor.GetProperties(current).Find(segment, true)
            ?? throw new ArgumentException($"'{segment}' is not a property of '{current.GetType()}'.", nameof(dataMember));

        var value = descriptor.GetValue(current);

        return value is null ? new PropertyManager() : CreateManagerForList(value);
    }

    private static BindingManagerBase CreateManagerForList(object dataSource)
    {
        if (dataSource is ICurrencyManagerProvider provider) return provider.GetRelatedCurrencyManager(null);

        var list = ListBindingHelper.GetList(dataSource);
        if (list is IList concreteList) return new CurrencyManager(concreteList);

        var manager = new PropertyManager();
        manager.SetDataSource(dataSource);

        return manager;
    }

    /// <summary>
    /// A data source identified by reference and held weakly, as WinForms
    /// holds it: a form rebound to a new result set must not keep every old
    /// one alive. The hash is taken while the source is alive, so the key can
    /// still be found (and removed) after it is collected.
    /// </summary>
    private sealed class Key
    {
        private readonly WeakReference<object> _dataSource;

        public Key(object dataSource, string path)
        {
            _dataSource = new WeakReference<object>(dataSource);
            Path = path;
            Hash = HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(dataSource), path);
        }

        public object? DataSource => _dataSource.TryGetTarget(out var target) ? target : null;

        public string Path { get; }

        public int Hash { get; }

        public bool IsAlive => _dataSource.TryGetTarget(out _);
    }

    private sealed class KeyComparer : IEqualityComparer<Key>
    {
        public static readonly KeyComparer Instance = new();

        public bool Equals(Key? x, Key? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null || x.Hash != y.Hash) return false;

            var source = x.DataSource;

            return source is not null && ReferenceEquals(source, y.DataSource) && string.Equals(x.Path, y.Path, StringComparison.Ordinal);
        }

        public int GetHashCode(Key obj) => obj.Hash;
    }

    /// <summary>Drops the entries whose data source or manager was collected.</summary>
    private void RemoveCollected()
    {
        var dead = _managers.Where(entry => !entry.Key.IsAlive || !entry.Value.TryGetTarget(out _)).Select(entry => entry.Key).ToList();
        foreach (var key in dead) _managers.Remove(key);
    }
}
