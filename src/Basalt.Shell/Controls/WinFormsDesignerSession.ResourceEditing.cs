using System.ComponentModel;
using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    private sealed record PropertyChange(string ControlName, string Property, string? Expression,
        string? ResourceKey = null, string? ResourceValue = null, string? ResourceType = null);

    internal Func<string, Task>? PrepareResourceEdit { get; set; }
    internal Action<string, string>? CommitResourceSource { get; set; }

    private string? ResourceKeyFor(string name, string property)
    {
        string? key = null;
        foreach (var operation in Document.ResourceOperations.Where(operation =>
            operation.ControlName.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            if (operation.PropertyName?.Equals(property, StringComparison.OrdinalIgnoreCase) == true)
                key = operation.ResourceKey;
            else if (operation.PropertyName is null && operation.ResourceKey is { } prefix)
                key = prefix + "." + property;
        }
        return key;
    }

    internal bool CanResetResourceProperty(Forms.Control control, string property)
    {
        var descriptor = TypeDescriptor.GetProperties(control)[property];
        if (_resources is null || descriptor is null || descriptor.IsReadOnly
            || ResourceKeyFor(NameOf(control), property) is null)
            return false;
        return WinFormsDesignerResources.TryWrite(descriptor.GetValue(control), descriptor.PropertyType, out _, out _);
    }

    private PropertyChange ValueChange(Forms.Control control, string property, object? value)
    {
        var name = NameOf(control);
        var expression = SerializePropertyValue(value);
        var key = ResourceKeyFor(name, property);
        if (key is null) return new(name, property, expression);
        var descriptor = TypeDescriptor.GetProperties(control)[property]
            ?? throw new ArgumentException("The property does not exist.", nameof(property));
        if (_resources is null)
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerUnsupportedExpression));
        if (!WinFormsDesignerResources.TryWrite(value, descriptor.PropertyType, out var text, out var type))
        {
            // Properties not represented in the resource document can still use the
            // normal VB serializer, which appends an override after ApplyResources.
            if (_resources.TryGet(key) is null) return new(name, property, expression);
            throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsDesignerUnsupportedExpression));
        }
        return new(name, property, expression, key, text, type);
    }

    private PropertyChange ResetChange(Forms.Control control, string property)
    {
        if (ResourceKeyFor(NameOf(control), property) is null)
            return new(NameOf(control), property, null);
        // These previews contain only built-in Forms controls. Constructor defaults
        // avoid invoking application reset hooks and are serialized before disposal.
        using var defaults = (Forms.Control)Activator.CreateInstance(control.GetType())!;
        var descriptor = TypeDescriptor.GetProperties(defaults)[property]
            ?? throw new ArgumentException("The property does not exist.", nameof(property));
        return ValueChange(control, property, descriptor.GetValue(defaults));
    }

    private async Task ApplyPropertyChangesAsync(IReadOnlyList<PropertyChange> changes)
    {
        var original = Document;
        var beforeResources = _resources;
        var resourceChanges = changes.Where(change => change.ResourceKey is not null).ToArray();
        if (resourceChanges.GroupBy(change => change.ResourceKey, StringComparer.Ordinal).Any(group =>
            group.Select(change => (change.ResourceValue, change.ResourceType)).Distinct().Count() > 1))
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerUnsupportedExpression));
        if (resourceChanges.Length > 0 && PrepareResourceEdit is not null)
            await PrepareResourceEdit(beforeResources!.Source);
        if (!ReferenceEquals(original, Document) || !ReferenceEquals(beforeResources, _resources))
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));

        var edited = await Task.Run(() =>
        {
            var document = original;
            var resources = beforeResources;
            foreach (var change in changes)
            {
                if (change.ResourceKey is { } key)
                    resources = new WinFormsResourceDocument(resources!.SetValue(key,
                        change.ResourceValue!, change.ResourceType));
                else
                    document = new WinFormsDesignerDocument(document.SetProperty(
                        change.ControlName, change.Property, change.Expression));
            }
            return (Document: document, Resources: resources);
        });
        if (!ReferenceEquals(original, Document) || !ReferenceEquals(beforeResources, _resources))
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        var resourceChanged = beforeResources?.Source != edited.Resources?.Source;
        if (!resourceChanged && edited.Document.Source == original.Source) return;

        void RestoreResources(WinFormsResourceDocument? expected, WinFormsResourceDocument? replacement)
        {
            if (_resources?.Source != expected?.Source)
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            if (expected is not null && replacement is not null)
                CommitResourceSource?.Invoke(expected.Source, replacement.Source);
            _resources = replacement;
        }

        await ApplyAsync(_ => edited.Document.Source,
            resourceChanged ? () => RestoreResources(beforeResources, edited.Resources) : null,
            resourceChanged ? () => RestoreResources(edited.Resources, beforeResources) : null,
            resourceChanged ? () => RestoreResources(beforeResources, edited.Resources) : null,
            commitUnchangedSource: resourceChanged,
            undoPrepare: resourceChanged ? () => PrepareResourceEdit?.Invoke(edited.Resources!.Source) ?? Task.CompletedTask : null,
            redoPrepare: resourceChanged ? () => PrepareResourceEdit?.Invoke(beforeResources!.Source) ?? Task.CompletedTask : null);
    }
}
