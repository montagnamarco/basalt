using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>Anything a Binding can attach to: a component with a BindingContext and a bindings list.</summary>
public interface IBindableComponent : IComponent, IDisposable
{
    BindingContext? BindingContext { get; set; }

    ControlBindingsCollection DataBindings { get; }
}
