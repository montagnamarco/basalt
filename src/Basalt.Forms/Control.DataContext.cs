using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// The data context of command binding (.NET 8): an ambient value a control
/// inherits from its parent until it is given its own. Setting it to null
/// goes back to inheriting.
/// </summary>
public partial class Control
{
    private static readonly object s_dataContextChangedEvent = new();

    private object? _dataContext;
    private bool _hasDataContext;

    [Bindable(true)]
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public virtual object? DataContext
    {
        get => _hasDataContext ? _dataContext : _parent?.DataContext;
        set
        {
            if (Equals(value, DataContext) && (value is not null || !_hasDataContext)) return;

            _dataContext = value;
            _hasDataContext = value is not null;
            OnDataContextChanged(EventArgs.Empty);
        }
    }

    public event EventHandler? DataContextChanged
    {
        add => Events.AddHandler(s_dataContextChangedEvent, value);
        remove => Events.RemoveHandler(s_dataContextChangedEvent, value);
    }

    protected virtual void OnDataContextChanged(EventArgs e)
    {
        if (IsDisposingInHierarchy()) return;

        ((EventHandler?)Events[s_dataContextChangedEvent])?.Invoke(this, e);

        foreach (var child in ChildrenSnapshot()) child.OnParentDataContextChanged(e);
    }

    /// <summary>
    /// The parent's context changed: a child that inherits it changes too; a
    /// child whose own value now equals the parent's goes back to inheriting.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnParentDataContextChanged(EventArgs e)
    {
        if (_hasDataContext)
        {
            if (!Equals(_dataContext, _parent?.DataContext)) return;

            _hasDataContext = false;
            _dataContext = null;
        }

        OnDataContextChanged(e);
    }
}
