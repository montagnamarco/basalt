using System.ComponentModel;
using System.Drawing;
using Basalt.Designer;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    public IReadOnlyList<DesignableProperty> CommonProperties(IReadOnlyList<Forms.Control> controls)
    {
        ValidatePropertyGroup(controls);
        return WinFormsMultiPropertyInspector.Describe(controls.Cast<object>().ToArray(),
            (control, property) => IsExplicit((Forms.Control)control, property))
            .Select(property =>
            {
                var states = controls.Select(control =>
                    WinFormsPropertyResetMetadata.Describe(control,
                        TypeDescriptor.GetProperties(control)[property.Name]!,
                        SourceValueState(control, property.Name), CanResetResourceProperty(control, property.Name))).ToArray();
                return property with
                {
                    IsModifiedOverride = states.Any(state => state.IsModified),
                    CanResetOverride = states.All(state => state.CanReset),
                };
            }).ToArray();
    }

    public Task SetGroupPropertyAsync(IReadOnlyList<Forms.Control> controls, string property, string? text)
    {
        ValidatePropertyGroup(controls);
        var common = CommonProperties(controls);
        if (!common.Any(candidate => candidate.Name.Equals(property, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("The property is not editable on every selected control.", nameof(property));

        // Convert and serialize every target before changing the source. A custom converter
        // can reject one instance even when its property has the same public value type.
        var changes = new List<PropertyChange>(controls.Count);
        foreach (var control in controls)
        {
            if (text is null)
            {
                changes.Add(ResetChange(control, property));
                continue;
            }
            var descriptor = TypeDescriptor.GetProperties(control)[property]
                ?? throw new ArgumentException("The property does not exist.", nameof(property));
            if (descriptor.IsReadOnly)
                throw new InvalidOperationException("The property is read-only on one selected control.");
            var value = descriptor.Converter.ConvertFromInvariantString(text);
            try { changes.Add(ValueChange(control, property, value)); }
            finally { if (value is Font { IsSystemFont: false } font) font.Dispose(); }
        }
        return ApplyPropertyChangesAsync(changes);
    }

    private void ValidatePropertyGroup(IReadOnlyList<Forms.Control> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        if (controls.Count < 2) throw new ArgumentException("Select at least two controls.", nameof(controls));
        var firstParent = controls[0]?.Parent;
        if (firstParent is null) throw new ArgumentException("Selected controls must be parented.", nameof(controls));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var control in controls)
        {
            ArgumentNullException.ThrowIfNull(control);
            if (ReferenceEquals(control, Form) || !ReferenceEquals(control.Parent, firstParent)
                || !names.Add(NameOf(control)))
                throw new ArgumentException("Select distinct sibling controls from this preview.", nameof(controls));
        }
    }
}
