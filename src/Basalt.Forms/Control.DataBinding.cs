using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// What makes a control bindable: a BindingContext, inherited from the
/// parent unless the control has its own, and the collection of Bindings
/// that use it.
/// </summary>
public partial class Control : IBindableComponent
{
    private BindingContext? _bindingContext;
    private ControlBindingsCollection? _dataBindings;

    /// <summary>
    /// The container that hands out one BindingManagerBase per data source: a
    /// control's own, else its parent's. Most controls never set their own;
    /// a Form or UserControl typically does, so every control inside shares it.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public virtual BindingContext? BindingContext
    {
        get
        {
            if (_bindingContext is not null) return _bindingContext;

            return _parent?.BindingContext;
        }
        set
        {
            if (_bindingContext == value) return;

            _bindingContext = value;
            OnBindingContextChanged(EventArgs.Empty);
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ControlBindingsCollection DataBindings => _dataBindings ??= new ControlBindingsCollection(this);

    protected virtual void OnBindingContextChanged(EventArgs e)
    {
        if (IsDisposingInHierarchy()) return;

        // The bindings follow the control to its new context, as WinForms rebinds them here.
        if (_dataBindings is { } bindings)
        {
            foreach (Binding binding in bindings) BindingContext.UpdateBinding(BindingContext, binding);
        }

        ((EventHandler?)Events[s_bindingContextEvent])?.Invoke(this, e);

        foreach (var child in ChildrenSnapshot()) child.OnParentBindingContextChanged(e);
    }

    protected virtual void OnParentBindingContextChanged(EventArgs e)
    {
        if (_bindingContext is null) OnBindingContextChanged(e);
    }

    /// <summary>A disposed control lets go of its data sources: the bindings stop listening to them.</summary>
    internal virtual void ReleaseBindings() => _dataBindings?.Clear();

    public event EventHandler? BindingContextChanged
    {
        add => Events.AddHandler(s_bindingContextEvent, value);
        remove => Events.RemoveHandler(s_bindingContextEvent, value);
    }

    private static readonly object s_bindingContextEvent = new();
}
