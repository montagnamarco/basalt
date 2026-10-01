using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// A component with a BindingContext and a Bindings collection, but no
/// window of its own: the base Microsoft gives <see cref="ToolStripItem"/>,
/// which binds like a control without being one.
/// </summary>
/// <remarks>
/// Placed here because <see cref="ToolStripItem"/> is this round's only user;
/// a future control family that needs it too should move it beside
/// <see cref="IBindableComponent"/> in DataBinding/.
/// </remarks>
public abstract class BindableComponent : Component, IBindableComponent
{
    private BindingContext? _bindingContext;
    private ControlBindingsCollection? _dataBindings;

    protected BindableComponent()
    {
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BindingContext? BindingContext
    {
        get => _bindingContext;
        set
        {
            if (_bindingContext == value) return;

            _bindingContext = value;
            OnBindingContextChanged(EventArgs.Empty);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    [RefreshProperties(RefreshProperties.All)]
    [ParenthesizePropertyName(true)]
    public ControlBindingsCollection DataBindings => _dataBindings ??= new ControlBindingsCollection(this);

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public event EventHandler? BindingContextChanged
    {
        add => Events.AddHandler(s_bindingContextEvent, value);
        remove => Events.RemoveHandler(s_bindingContextEvent, value);
    }

    private static readonly object s_bindingContextEvent = new();

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnBindingContextChanged(EventArgs e) => ((EventHandler?)Events[s_bindingContextEvent])?.Invoke(this, e);
}
