using System.ComponentModel;
using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    /// <summary>
    /// One property edit. A null <see cref="ResourceValue"/> on a resource-backed change removes the
    /// entry instead: a culture that matches the neutral value keeps no override of its own.
    /// </summary>
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
        if (descriptor is null || descriptor.IsReadOnly || ResourceKeyFor(NameOf(control), property) is not { } key)
            return false;
        // Inside a culture, reset only removes the culture's own value, so it applies only when there is one.
        if (_culture is not null)
            return _cultureResources?.TryGet(key) is not null;
        return _resources is not null
            && WinFormsDesignerResources.TryWrite(descriptor.GetValue(control), descriptor.PropertyType, out _, out _);
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
        return _culture is null
            ? new(name, property, expression, key, text, type)
            : CultureValueChange(name, property, expression, key, text, type);
    }

    /// <summary>
    /// A culture's .resx holds only the values that differ from the neutral one. A value equal to the
    /// neutral entry therefore removes the culture's override, so the surface falls back to neutral.
    /// </summary>
    private PropertyChange CultureValueChange(string name, string property, string expression, string key,
        string text, string? type)
    {
        var neutral = _resources?.TryGet(key);
        var matchesNeutral = neutral is not null && neutral.Value == text && neutral.TypeName == type;
        return matchesNeutral
            ? new(name, property, expression, key)
            : new(name, property, expression, key, text, type);
    }

    private PropertyChange ResetChange(Forms.Control control, string property)
    {
        var key = ResourceKeyFor(NameOf(control), property);
        if (key is null)
            return new(NameOf(control), property, null);
        // Inside a culture, reset drops the culture's override so the neutral value shows again.
        if (_culture is not null)
            return new(NameOf(control), property, null, key);
        // These previews contain only built-in Forms controls. Constructor defaults
        // avoid invoking application reset hooks and are serialized before disposal.
        using var defaults = (Forms.Control)Activator.CreateInstance(control.GetType())!;
        var descriptor = TypeDescriptor.GetProperties(defaults)[property]
            ?? throw new ArgumentException("The property does not exist.", nameof(property));
        return ValueChange(control, property, descriptor.GetValue(defaults));
    }

    /// <summary>The .resx an edit writes: the culture's while a culture is shown, the neutral one otherwise.</summary>
    private WinFormsResourceDocument? ResourcesBeingEdited(bool culture) => culture ? _cultureResources : _resources;

    /// <summary>
    /// Opens the .resx an edit writes, as <see cref="PrepareResourceEdit"/> does for the neutral one. A culture's file
    /// that does not exist yet is created holding <paramref name="replacement"/>, the text the edit writes into it, so
    /// the file is created once with its final content.
    /// </summary>
    private Task PrepareEdit(bool editsCulture, string? culture, string expected, string replacement) =>
        editsCulture
            ? PrepareCultureResourceEdit?.Invoke(culture!, expected, replacement) ?? Task.CompletedTask
            : PrepareResourceEdit?.Invoke(expected) ?? Task.CompletedTask;

    private async Task ApplyPropertyChangesAsync(IReadOnlyList<PropertyChange> changes)
    {
        // Captured once: a language switch during the edit must not redirect this edit's undo or its .resx.
        var culture = _culture;
        var editsCulture = culture is not null;
        var original = Document;
        var beforeResources = ResourcesBeingEdited(editsCulture);
        var resourceChanges = changes.Where(change => change.ResourceKey is not null).ToArray();
        if (resourceChanges.GroupBy(change => change.ResourceKey, StringComparer.Ordinal).Any(group =>
            group.Select(change => (change.ResourceValue, change.ResourceType)).Distinct().Count() > 1))
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerUnsupportedExpression));

        var edited = await Task.Run(() =>
        {
            var document = original;
            var resources = beforeResources;
            foreach (var change in changes)
            {
                if (change.ResourceKey is { } key)
                    resources = new WinFormsResourceDocument(change.ResourceValue is null
                        ? resources!.Remove(key)
                        : resources!.SetValue(key, change.ResourceValue, change.ResourceType));
                else
                    document = new WinFormsDesignerDocument(document.SetProperty(
                        change.ControlName, change.Property, change.Expression));
            }
            return (Document: document, Resources: resources);
        });
        if (!ReferenceEquals(original, Document) || !ReferenceEquals(beforeResources, ResourcesBeingEdited(editsCulture)))
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        // A culture's .resx holds only text. A change that reaches Designer.vb (a property that is not
        // localizable, a Font still in code, Anchor, Dock, TextAlign) is refused here, before anything is written.
        if (editsCulture && edited.Document.Source != original.Source)
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerNotInLanguage));
        var resourceChanged = beforeResources?.Source != edited.Resources?.Source;
        if (!resourceChanged && edited.Document.Source == original.Source) return;

        // The .resx is opened, or a culture's file created, only for an edit that writes something: a no-op or a refused
        // edit must not leave a culture file behind.
        if (resourceChanged)
            await PrepareEdit(editsCulture, culture, beforeResources!.Source, edited.Resources!.Source);
        if (!ReferenceEquals(original, Document) || !ReferenceEquals(beforeResources, ResourcesBeingEdited(editsCulture)))
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));

        void RestoreResources(WinFormsResourceDocument? expected, WinFormsResourceDocument? replacement)
        {
            // An edit made in a culture is checked against that culture's buffer, which is the only one the
            // commit writes. The shown copy is compared only while that culture is still the one on screen.
            // The commit can run after a language switch: ApplyAsync awaits its parse before it commits. The
            // culture's file is then still written, but the copy on screen belongs to another language and stays.
            var shown = !editsCulture || _culture == culture;
            if (shown && ResourcesBeingEdited(editsCulture)?.Source != expected?.Source)
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            if (expected is not null && replacement is not null)
            {
                if (editsCulture)
                    CommitCultureResourceSource?.Invoke(culture!, expected.Source, replacement.Source);
                else
                    CommitResourceSource?.Invoke(expected.Source, replacement.Source);
            }
            if (!shown) return;
            if (editsCulture)
                _cultureResources = replacement;
            else
                _resources = replacement;
        }

        await ApplyAsync(_ => edited.Document.Source,
            resourceChanged ? () => RestoreResources(beforeResources, edited.Resources) : null,
            resourceChanged ? () => RestoreResources(edited.Resources, beforeResources) : null,
            resourceChanged ? () => RestoreResources(beforeResources, edited.Resources) : null,
            commitUnchangedSource: resourceChanged,
            // An undo or redo that finds the culture's file gone recreates it with the text it restores.
            undoPrepare: resourceChanged
                ? () => PrepareEdit(editsCulture, culture, edited.Resources!.Source, beforeResources!.Source) : null,
            redoPrepare: resourceChanged
                ? () => PrepareEdit(editsCulture, culture, beforeResources!.Source, edited.Resources!.Source) : null);
    }
}
