namespace Basalt.Designer;

/// <summary>Finds properties that every selected Forms control can edit with the same value type.</summary>
public static class WinFormsMultiPropertyInspector
{
    public static IReadOnlyList<DesignableProperty> Describe(IReadOnlyList<object> controls,
        Func<object, string, bool> isExplicit)
    {
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(isExplicit);
        if (controls.Count == 0) return [];

        var descriptions = controls.Select(control => WinFormsPropertyInspector.Describe(control)).ToArray();
        var others = descriptions.Skip(1).Select(properties =>
            properties.ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
        var common = new List<DesignableProperty>();
        foreach (var property in descriptions[0])
        {
            // Field identity and event bindings require a semantic single-control rename.
            if (property.Name.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
            if (!others.All(properties => properties.TryGetValue(property.Name, out var candidate)
                && candidate.ValueType == property.ValueType && candidate.EditorKind == property.EditorKind
                && candidate.AllowedValues.SequenceEqual(property.AllowedValues, StringComparer.OrdinalIgnoreCase)))
                continue;

            try
            {
                var values = controls.Select(control => WinFormsPropertyInspector.ReadValue(control, property.Name)).ToArray();
                var mixed = values.Skip(1).Any(value => !string.Equals(value, values[0], StringComparison.Ordinal));
                var wasSet = controls.Any(control => isExplicit(control, property.Name));
                common.Add(property with
                {
                    CurrentValue = !mixed && wasSet ? values[0] : null,
                    DefaultValue = mixed ? null : values[0],
                    IsMixed = mixed,
                    WasSet = wasSet,
                });
            }
            catch
            {
                // A getter or converter that cannot be read safely is not an editable group property.
            }
        }

        return common;
    }
}
