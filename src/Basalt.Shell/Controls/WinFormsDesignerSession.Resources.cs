using System.ComponentModel;
using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    private WinFormsResourceDocument? _resources;
    private readonly HashSet<(string Control, string Property)> _resourceProperties = [];

    internal string? ResourceSource => _resources?.Source;

    internal void UpdateResources(WinFormsResourceDocument? resources)
    {
        if (ResourceSource == resources?.Source) return;
        _resources = resources;
        Replace(Document);
    }

    private void ApplyResourceProperties(WinFormsDesignerControl model, Forms.Control control, List<string> problems)
    {
        foreach (var operation in Document.ResourceOperations.Where(operation =>
            operation.ControlName.Equals(model.Name, StringComparison.OrdinalIgnoreCase)))
        {
            if (operation.PropertyName is { } property)
            {
                ApplyPreviewAssignment(model, control, property, () =>
                {
                    var descriptor = TypeDescriptor.GetProperties(control)[property]
                        ?? throw new InvalidOperationException();
                    if (operation.ResourceKey is { } key)
                        return ReadResourceValue(key, descriptor.PropertyType);
                    return EvaluatePreviewProperty(operation.Expression!, descriptor.PropertyType);
                }, operation.ResourceKey is not null, problems);
            }
            else if (operation.ResourceKey is { } prefix)
            {
                if (_resources is null)
                {
                    problems.Add(model.Name + ": " + Localizer.Get(StringKeys.WinFormsDesignerUnsupportedExpression));
                    continue;
                }
                foreach (var key in EntryNamesUnder(prefix))
                {
                    var name = key[(prefix.Length + 1)..];
                    if (TypeDescriptor.GetProperties(control)[name] is not { IsReadOnly: false } descriptor)
                        continue;
                    ApplyPreviewAssignment(model, control, name,
                        () => ReadResourceValue(key, descriptor.PropertyType), true, problems);
                }
            }
        }
    }

    /// <summary>
    /// The entry names under a prefix. While a culture is shown its own entries count too, so a value
    /// the culture adds where the neutral resource has none still reaches the surface.
    /// </summary>
    private IEnumerable<string> EntryNamesUnder(string prefix)
    {
        var dotted = prefix + ".";
        IEnumerable<string> names = _resources is null
            ? []
            : _resources.Entries.Select(entry => entry.Name);
        if (_culture is not null && _cultureResources is not null)
            names = names.Concat(_cultureResources.Entries.Select(entry => entry.Name));
        return names.Where(name => name.StartsWith(dotted, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// The entry the surface shows for a key: the culture's own when the culture overrides it, else the neutral one.
    /// </summary>
    private WinFormsResourceEntry? VisibleResourceEntry(string key) =>
        (_culture is null ? null : _cultureResources?.TryGet(key)) ?? _resources?.TryGet(key);

    private object? ReadResourceValue(string key, Type expectedType)
    {
        var entry = VisibleResourceEntry(key);
        if (entry is null || !WinFormsDesignerResources.TryRead(entry, expectedType, out var value))
            throw new FormatException(Localizer.Get(StringKeys.WinFormsDesignerUnsupportedExpression));
        return value;
    }

    private void ApplyPreviewAssignment(WinFormsDesignerControl model, Forms.Control control, string property,
        Func<object?> evaluate, bool resourceBacked, List<string> problems)
    {
        var descriptor = TypeDescriptor.GetProperties(control)[property];
        if (descriptor is null || descriptor.IsReadOnly) return;
        if (resourceBacked) _resourceProperties.Add((model.Name, descriptor.Name));
        try
        {
            var value = evaluate();
            if (value is System.Drawing.Font { IsSystemFont: false } font) _previewFonts.Add(font);
            descriptor.SetValue(control, value);
            _unresolvedProperties.Remove((model.Name, descriptor.Name));
        }
        catch (Exception exception)
        {
            _unresolvedProperties.Add((model.Name, descriptor.Name));
            problems.Add(model.Name + "." + property + ": " + exception.GetBaseException().Message);
        }
    }
}
