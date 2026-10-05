using System.ComponentModel;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    public Task SetPropertyValueAsync(object component, string property, object? value)
    {
        var descriptor = TypeDescriptor.GetProperties(component)[property]
            ?? throw new ArgumentException("The property does not exist.", nameof(property));
        if (descriptor.IsReadOnly)
        {
            throw new InvalidOperationException("The property is read-only.");
        }

        if (value is not null && !descriptor.PropertyType.IsInstanceOfType(value))
        {
            throw new ArgumentException("The value has the wrong property type.", nameof(value));
        }

        if (component is Forms.Control control && _controls.Values.Any(candidate => ReferenceEquals(candidate, control)))
            return ApplyPropertyChangesAsync([ValueChange(control, property, value)]);

        var expression = SerializePropertyValue(value);
        return ApplyPropertyExpressionAsync(component, property, expression);
    }

    private string SerializePropertyValue(object? value)
    {
        if (value is Forms.Control control && _controls.Values.Any(candidate => ReferenceEquals(candidate, control)))
        {
            var name = NameOf(control);
            return name == "Me" ? "Me" : "Me." + name;
        }

        if (value is IComponent component)
        {
            return "Me." + NameOfComponent(component);
        }

        return WinFormsDesignerValues.Serialize(value);
    }

    public Task SetNestedPropertyAsync(object component, string parent, string child, string? text)
    {
        if (text is null)
        {
            // Reset this child from the built-in constructor default, retaining
            // the other members of the current parent value.
            using var defaults = (IDisposable)Activator.CreateInstance(component.GetType())!;
            text = WinFormsExpandableProperties.Children(defaults, parent)
                .Single(property => property.Name == child).DefaultValue
                ?? throw new InvalidOperationException("The child has no default value.");
        }
        var value = WinFormsExpandableProperties.Rebuild(component, parent, child, text);
        try
        {
            return SetPropertyValueAsync(component, parent, value);
        }
        finally
        {
            if (value is Font { IsSystemFont: false } font)
            {
                font.Dispose();
            }
        }
    }

    private Task ApplyPropertyExpressionAsync(object component, string property, string expression)
    {
        if (component is Forms.Control control && _controls.Values.Any(value => ReferenceEquals(value, control)))
        {
            var name = NameOf(control);
            return ApplyAsync(document => document.SetProperty(name, property, expression));
        }
        var componentName = NameOfComponent((IComponent)component);
        return ApplyAsync(document => document.SetComponentProperty(componentName, property, expression));
    }
}
