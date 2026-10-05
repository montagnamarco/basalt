namespace System.Windows.Forms;

/// <summary>
/// Owns the bindings aimed at one position in one list (CurrencyManager) or
/// at one plain object (PropertyManager), and tells them when to push data
/// into their controls: on construction, and whenever Current or Position
/// changes.
/// </summary>
public abstract class BindingManagerBase
{
    /// <summary>
    /// The backing fields for CurrentChanged and PositionChanged:
    /// Microsoft keeps them as named, protected fields instead of an event
    /// list because this class, unlike Control, is not a Component.
    /// </summary>
    protected EventHandler? onCurrentChangedHandler;

    protected EventHandler? onPositionChangedHandler;

    private readonly BindingsCollection _bindings = new();
    private int _suspendCount;

    public BindingManagerBase()
    {
    }

    public BindingsCollection Bindings => _bindings;

    public abstract int Count { get; }

    public abstract object? Current { get; }

    public bool IsBindingSuspended => _suspendCount > 0;

    public abstract int Position { get; set; }

    public event BindingCompleteEventHandler? BindingComplete;

    public event EventHandler? CurrentChanged
    {
        add => onCurrentChangedHandler += value;
        remove => onCurrentChangedHandler -= value;
    }

    public event EventHandler? CurrentItemChanged;

    public event BindingManagerDataErrorEventHandler? DataError;

    public event EventHandler? PositionChanged
    {
        add => onPositionChangedHandler += value;
        remove => onPositionChangedHandler -= value;
    }

    public abstract void AddNew();

    public abstract void CancelCurrentEdit();

    public abstract void EndCurrentEdit();

    public virtual global::System.ComponentModel.PropertyDescriptorCollection GetItemProperties() =>
        Current is null ? global::System.ComponentModel.PropertyDescriptorCollection.Empty : global::System.ComponentModel.TypeDescriptor.GetProperties(Current);

    protected virtual global::System.ComponentModel.PropertyDescriptorCollection GetItemProperties(Collections.ArrayList dataSources, Collections.ArrayList listAccessors) =>
        GetItemProperties();

    protected virtual global::System.ComponentModel.PropertyDescriptorCollection GetItemProperties(
        Type listType, int offset, Collections.ArrayList dataSources, Collections.ArrayList listAccessors) =>
        global::System.ComponentModel.TypeDescriptor.GetProperties(listType);

    protected abstract string GetListName(Collections.ArrayList? listAccessors);

    protected void OnBindingComplete(BindingCompleteEventArgs args) => BindingComplete?.Invoke(this, args);

    protected abstract void OnCurrentChanged(EventArgs e);

    protected abstract void OnCurrentItemChanged(EventArgs e);

    protected void OnDataError(Exception e) => DataError?.Invoke(this, new BindingManagerDataErrorEventArgs(e));

    /// <summary>Raises CurrentItemChanged: both managers call this from their own OnCurrentItemChanged override.</summary>
    internal void RaiseCurrentItemChanged(EventArgs e) => CurrentItemChanged?.Invoke(this, e);

    /// <summary>Lets a Binding, which does not inherit this class, raise the manager's BindingComplete.</summary>
    internal void NotifyBindingComplete(BindingCompleteEventArgs e) => OnBindingComplete(e);

    /// <summary>Lets a Binding report a conversion or update failure through the manager's DataError.</summary>
    internal void NotifyDataError(Exception e) => OnDataError(e);

    protected void PullData()
    {
        if (IsBindingSuspended) return;

        foreach (var binding in BoundBindings()) binding.PullFromControl();
    }

    protected void PushData()
    {
        if (IsBindingSuspended) return;

        foreach (var binding in BoundBindings()) binding.PushToControl();
    }

    public abstract void RemoveAt(int index);

    public abstract void ResumeBinding();

    public abstract void SuspendBinding();

    protected abstract void UpdateIsBinding();

    /// <summary>
    /// The counting both overrides of SuspendBinding/ResumeBinding share;
    /// `private protected` so it helps CurrencyManager and PropertyManager
    /// without becoming part of the public surface neither of them adds.
    /// </summary>
    private protected void BeginSuspendBinding()
    {
        _suspendCount++;
        if (_suspendCount == 1) UpdateIsBinding();
    }

    private protected void EndSuspendBinding()
    {
        if (_suspendCount == 0) return;

        _suspendCount--;
        if (_suspendCount == 0) UpdateIsBinding();
    }

    private Binding[] BoundBindings()
    {
        var bindings = new Binding[Bindings.Count];
        for (var i = 0; i < bindings.Length; i++) bindings[i] = Bindings[i];

        return bindings;
    }
}
