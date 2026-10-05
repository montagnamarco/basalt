using System.ComponentModel;
using Basalt.Core.Localization;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private Task SetGridPropertyAsync(object component, string property, string? text)
    {
        var session = _session!;
        if (property == "Name" && !ReferenceEquals(component, session.Form))
        {
            var name = component is Forms.Control control && session.Components.Values.All(value => !ReferenceEquals(value, control))
                ? session.NameOf(control)
                : session.NameOfComponent((IComponent)component);
            return RenameFieldAsync(name, text);
        }

        var descriptor = TypeDescriptor.GetProperties(component)[property]
            ?? throw new ArgumentException("The property does not exist.", nameof(property));
        if (text is not null && typeof(IComponent).IsAssignableFrom(descriptor.PropertyType))
        {
            var referenceName = text == Localizer.Get(StringKeys.PropertyGridNone) ? null : text;
            return session.SetPropertyReferenceAsync(component, property, referenceName);
        }

        if (component is Forms.Control visual && session.Components.Values.All(value => !ReferenceEquals(value, visual)))
        {
            return session.SetPropertyAsync(visual, property, text);
        }

        return session.SetComponentPropertyAsync((IComponent)component, property, text);
    }
}
