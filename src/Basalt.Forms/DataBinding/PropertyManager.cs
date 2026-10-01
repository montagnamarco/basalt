using System.Collections;
using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>A BindingManagerBase over a single object instead of a list: Count is always 0 or 1, Position always 0.</summary>
public class PropertyManager : BindingManagerBase
{
    private object? _dataSource;

    public override int Count => _dataSource is null ? 0 : 1;

    public override object? Current => _dataSource;

    public override int Position
    {
        get => 0;
        set { }
    }

    public override void AddNew() => throw new NotSupportedException("A PropertyManager has no list to add to.");

    public override void CancelCurrentEdit()
    {
        if (_dataSource is IEditableObject editable) editable.CancelEdit();

        PushData();
    }

    public override void EndCurrentEdit()
    {
        if (_dataSource is IEditableObject editable) editable.EndEdit();
    }

    protected override string GetListName(ArrayList? listAccessors) => _dataSource?.GetType().Name ?? string.Empty;

    protected override void OnCurrentChanged(EventArgs ea) => onCurrentChangedHandler?.Invoke(this, ea);

    protected override void OnCurrentItemChanged(EventArgs ea) => RaiseCurrentItemChanged(ea);

    public override void RemoveAt(int index) => throw new NotSupportedException("A PropertyManager has no list to remove from.");

    public override void ResumeBinding() => EndSuspendBinding();

    public override void SuspendBinding() => BeginSuspendBinding();

    protected override void UpdateIsBinding() => PushData();

    /// <summary>Not public in Microsoft's PropertyManager either: BindingContext sets the tracked object through this.</summary>
    internal void SetDataSource(object? dataSource)
    {
        if (ReferenceEquals(_dataSource, dataSource)) return;

        _dataSource = dataSource;
        OnCurrentChanged(EventArgs.Empty);
        OnCurrentItemChanged(EventArgs.Empty);
    }
}
