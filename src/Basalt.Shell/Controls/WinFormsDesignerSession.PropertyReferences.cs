using System.ComponentModel;
using Basalt.Designer;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    public IReadOnlyList<string> ReferenceNames(Type expectedType)
    {
        return _controls.Where(pair => expectedType.IsInstanceOfType(pair.Value)).Select(pair => pair.Key)
            .Concat(_components.Where(pair => expectedType.IsInstanceOfType(pair.Value)).Select(pair => pair.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task SetPropertyReferenceAsync(object component, string property, string? name)
    {
        if (name is null)
        {
            return SetPropertyValueAsync(component, property, null);
        }

        object? value = FindComponent(name) ?? (object?)FindControl(name);
        if (value is null)
        {
            throw new ArgumentException("The referenced component is not in this designer.", nameof(name));
        }

        return SetPropertyValueAsync(component, property, value);
    }

    private string? ReadPreviewProperty(object component, string property)
    {
        var descriptor = TypeDescriptor.GetProperties(component)[property]
            ?? throw new ArgumentException("The property does not exist.", nameof(property));
        var value = descriptor.GetValue(component);

        if (value is Forms.Control control && _controls.Values.Any(candidate => ReferenceEquals(candidate, control)))
        {
            return NameOf(control);
        }

        if (value is IComponent reference && _components.Values.Any(candidate => ReferenceEquals(candidate, reference)))
        {
            return NameOfComponent(reference);
        }

        return WinFormsPropertyInspector.ReadDesignerValue(component, property);
    }
}
