using System.ComponentModel;
using System.Text;
using Basalt.Core.Localization;
using Basalt.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Shell.Controls;

/// <summary>
/// The form's Localizable switch and its design-time Language. Localizable moves values between the
/// code and the neutral .resx the way Visual Studio does. Language routes value edits into a culture
/// .resx (Form1.it.resx) that holds only the values differing from the neutral ones; the surface shows
/// the culture's value where it has one and the neutral value everywhere else.
/// </summary>
internal sealed partial class WinFormsDesignerSession
{
    /// <summary>The designer flag Visual Studio keeps in the neutral .resx while a form is localizable.</summary>
    /// <remarks>
    /// The name and the type below are the owner's knowledge of Visual Studio output. They were not checked
    /// against a file Visual Studio wrote for this change, so a difference there is a defect in this constant.
    /// </remarks>
    internal const string LocalizableMetadataName = "$this.Localizable";

    private const string BooleanTypeName = "System.Boolean, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

    private string? _culture;
    private WinFormsResourceDocument? _cultureResources;

    /// <summary>The culture the surface shows, or null for "(Default)", the neutral values.</summary>
    internal string? Culture => _culture;

    /// <summary>The culture .resx text, or null while the neutral values are shown.</summary>
    internal string? CultureResourceSource => _cultureResources?.Source;

    /// <summary>
    /// Whether the form is localizable. The neutral .resx's "$this.Localizable" flag decides when the file has
    /// one; without it, the ApplyResources(Me, "$this") call in the Designer.vb decides, as it did before the flag.
    /// </summary>
    internal bool IsLocalizable => _resources?.TryGetMetadata(LocalizableMetadataName) is { } flag
        ? string.Equals(flag.Value.Trim(), bool.TrueString, StringComparison.OrdinalIgnoreCase)
        : Document.IsLocalizable;

    /// <summary>
    /// Opens a culture's .resx buffer before a culture edit: (culture, expected text, replacement text). A file that
    /// does not exist yet is created holding the replacement, the text the edit writes, so the commit finds it there.
    /// </summary>
    internal Func<string, string, string, Task>? PrepareCultureResourceEdit { get; set; }

    /// <summary>Writes a culture edit into that culture's buffer: (culture, expected text, replacement text).</summary>
    internal Action<string, string, string>? CommitCultureResourceSource { get; set; }

    /// <summary>
    /// Checks that a culture's buffer still holds the expected or the replacement text, and writes nothing: (culture,
    /// expected text, replacement text). A control edit calls it for every culture file before it writes any of them.
    /// </summary>
    internal Action<string, string, string>? VerifyCultureResourceSource { get; set; }

    /// <summary>
    /// Shows the surface in <paramref name="culture"/> (null for "(Default)"). A form with no culture file
    /// yet passes null and gets an empty culture document, so the first edit creates the file's content.
    /// The language is design-time view state, so it takes no undo step. The rebuild shows the language, so it is
    /// set before the rebuild; a rebuild that fails puts the previous language back, so the surface and the session agree.
    /// </summary>
    internal void SetCulture(string? culture, WinFormsResourceDocument? cultureResources)
    {
        var previousCulture = _culture;
        var previousResources = _cultureResources;
        _culture = culture;
        _cultureResources = culture is null
            ? null
            : cultureResources ?? new WinFormsResourceDocument(WinFormsResourceDocumentTemplate.Empty);
        try
        {
            Replace(Document);
        }
        catch
        {
            _culture = previousCulture;
            _cultureResources = previousResources;
            throw;
        }
    }

    /// <summary>
    /// Loads the culture .resx for a culture an undo or redo must show, the way the view loads it for
    /// <see cref="SetCulture"/>. Null when the culture has no file yet.
    /// </summary>
    internal Func<string, Task<WinFormsResourceDocument?>>? LoadCultureResources { get; set; }

    /// <summary>
    /// Shows the language an edit was made in, before that edit is undone or redone. Designer.vb is shared by every
    /// language, so the edit can only be applied while its own language is on screen; the surface and the Language
    /// row then show that language afterwards.
    /// </summary>
    private async Task ShowEditCultureAsync(string? culture)
    {
        if (culture == _culture) return;
        WinFormsResourceDocument? cultureResources = null;
        if (culture is not null)
        {
            var load = LoadCultureResources
                ?? throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeUnavailable));
            cultureResources = await load(culture);
        }
        ObjectDisposedException.ThrowIf(_disposed, this);
        SetCulture(culture, cultureResources);
    }

    /// <summary>
    /// Sets the form's Localizable property. True moves each localizable value that is assigned in code
    /// into the neutral .resx and adds the ApplyResources calls; False writes the neutral values back into
    /// code and removes their entries. Only the neutral language can change the switch, because the values
    /// it moves are the neutral ones.
    /// </summary>
    internal Task SetLocalizableAsync(bool localizable)
    {
        if (_culture is not null)
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerLocalizableNeedsDefaultLanguage));
        if (localizable == IsLocalizable) return Task.CompletedTask;
        return localizable ? EnableLocalizationAsync() : DisableLocalizationAsync();
    }

    private async Task EnableLocalizationAsync()
    {
        // The values come from the code, not the surface: the preview scales bounds to the form's
        // AutoScale dimensions, so a live Location is not the Location Visual Studio stores.
        var moved = MovableValues();
        var original = Document;
        var beforeResources = _resources ?? await CreateResourcesAsync();
        if (!ReferenceEquals(original, Document))
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        await PrepareNeutralResourceAsync(beforeResources);
        EnsureUnchanged(original, beforeResources);

        var edited = await Task.Run(() =>
        {
            var resources = beforeResources;
            foreach (var value in moved)
            {
                resources = new WinFormsResourceDocument(resources.SetValue(
                    ResourceNameFor(value.ControlName, value.PropertyName), value.Text, value.TypeName));
            }
            resources = new WinFormsResourceDocument(resources.SetMetadata(LocalizableMetadataName, bool.TrueString, BooleanTypeName));
            var assignments = moved.Select(value => (value.ControlName, value.PropertyName)).ToArray();
            return (Document: new WinFormsDesignerDocument(original.EnableLocalization(assignments)), Resources: resources);
        });
        await CommitLocalizationAsync(original, beforeResources, edited.Document, edited.Resources);
    }

    private async Task DisableLocalizationAsync()
    {
        var original = Document;
        // Without a readable neutral .resx the values cannot be restored, so the switch is refused before
        // anything is written: an unreadable file would otherwise lose every value the ApplyResources calls read.
        var originalResources = _resources
            ?? throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerResourcesUnreadable));
        var (assignments, removedNames, _) = RestoredAssignments(originalResources);
        await PrepareNeutralResourceAsync(originalResources);
        EnsureUnchanged(original, originalResources);

        var edited = await Task.Run(() =>
        {
            var resources = originalResources;
            foreach (var name in removedNames)
                resources = new WinFormsResourceDocument(resources.Remove(name));
            resources = new WinFormsResourceDocument(resources.RemoveMetadata(LocalizableMetadataName));
            return (Document: new WinFormsDesignerDocument(original.DisableLocalization(assignments)), Resources: resources);
        });
        await CommitLocalizationAsync(original, originalResources, edited.Document, edited.Resources);
    }

    /// <summary>
    /// The atomic code-plus-neutral-resx commit of the switch. It is made in (Default), because Localizable changes
    /// only there, so its undo shows (Default) through the history's culture: a culture cannot be chosen while
    /// Localizable is off (the Language row is frozen then), so an undo that stayed in a culture would leave the
    /// user with no way back to the neutral values.
    /// </summary>
    private async Task CommitLocalizationAsync(WinFormsDesignerDocument originalDocument,
        WinFormsResourceDocument originalResources, WinFormsDesignerDocument editedDocument,
        WinFormsResourceDocument editedResources)
    {
        if (!ReferenceEquals(originalDocument, Document) || !ReferenceEquals(originalResources, _resources))
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        var resourceChanged = originalResources.Source != editedResources.Source;
        if (!resourceChanged && editedDocument.Source == originalDocument.Source) return;

        void RestoreResources(WinFormsResourceDocument expected, WinFormsResourceDocument replacement)
        {
            if (_resources?.Source != expected.Source)
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            CommitResourceSource?.Invoke(expected.Source, replacement.Source);
            _resources = replacement;
        }

        await ApplyAsync(_ => editedDocument.Source,
            () => { if (resourceChanged) RestoreResources(originalResources, editedResources); },
            () => { if (resourceChanged) RestoreResources(editedResources, originalResources); },
            () => { if (resourceChanged) RestoreResources(originalResources, editedResources); },
            commitUnchangedSource: resourceChanged,
            undoPrepare: resourceChanged ? () => PrepareNeutralResourceAsync(editedResources) : null,
            redoPrepare: resourceChanged ? () => PrepareNeutralResourceAsync(originalResources) : null);
    }

    /// <summary>
    /// Opens the neutral .resx for the edit the way every other resource edit does, so the commit has a buffer
    /// to write to. The file is opened from disk when the host has not opened it yet.
    /// </summary>
    private Task PrepareNeutralResourceAsync(WinFormsResourceDocument resources) =>
        PrepareResourceEdit?.Invoke(resources.Source) ?? Task.CompletedTask;

    private void EnsureUnchanged(WinFormsDesignerDocument original, WinFormsResourceDocument? resources)
    {
        if (!ReferenceEquals(original, Document) || !ReferenceEquals(resources, _resources))
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
    }

    /// <summary>
    /// Each localizable value the neutral .resx would hold: an explicit assignment in code whose value the
    /// resx codec can write. A value that cannot be evaluated or written (a Font, an image from My.Resources)
    /// stays in code as it was, so the switch never fails on it.
    /// </summary>
    private List<LocalizableValue> MovableValues() =>
        MovableValues(Document, Document.Controls.Prepend(Document.Root), model => FindControl(model.Name)?.GetType());

    /// <summary>
    /// The movable values of <paramref name="models"/> in <paramref name="document"/>. The type of each model comes from
    /// <paramref name="typeOf"/>, so a control that is not in the preview yet (one just added or pasted) is read the same way.
    /// </summary>
    private List<LocalizableValue> MovableValues(WinFormsDesignerDocument document,
        IEnumerable<WinFormsDesignerControl> models, Func<WinFormsDesignerControl, Type?> typeOf)
    {
        var values = new List<LocalizableValue>();
        foreach (var model in models)
        {
            var type = typeOf(model);
            if (type is null) continue;
            foreach (var (property, syntax) in model.PropertySyntax)
            {
                var descriptor = TypeDescriptor.GetProperties(type)[property];
                if (descriptor is null || descriptor.IsReadOnly || !IsLocalizableProperty(descriptor)) continue;
                if (ResourceKeyIn(document, model.Name, property) is not null) continue;
                if (!TryEvaluateForResource(syntax, descriptor.PropertyType, out var value)) continue;
                if (!WinFormsDesignerResources.TryWrite(value, descriptor.PropertyType, out var text, out var typeName))
                    continue;
                values.Add(new(model.Name, descriptor.Name, text, typeName));
            }
        }
        return values;
    }

    /// <summary>The resource key a document already reads a control's property from, or null when none does.</summary>
    private static string? ResourceKeyIn(WinFormsDesignerDocument document, string name, string property)
    {
        string? key = null;
        foreach (var operation in document.ResourceOperations.Where(operation =>
            operation.ControlName.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            if (operation.PropertyName?.Equals(property, StringComparison.OrdinalIgnoreCase) == true)
                key = operation.ResourceKey;
            else if (operation.PropertyName is null && operation.ResourceKey is { } prefix)
                key = prefix + "." + property;
        }
        return key;
    }

    /// <summary>
    /// Evaluates a value for the neutral .resx. Text may be a multi-line Visual Basic expression, which the
    /// preview evaluator does not read; any other expression goes through it. False means the value stays in code.
    /// </summary>
    private bool TryEvaluateForResource(ExpressionSyntax syntax, Type type, out object? value)
    {
        value = null;
        try
        {
            value = type == typeof(string) && TextConstant(syntax) is { } text ? text : EvaluatePreviewProperty(syntax, type);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The evaluator throws whatever the expression's shape provokes. Any failure keeps the value in code.
            return false;
        }
    }

    /// <summary>
    /// The value of a Visual Basic text expression made of string literals joined with &amp; or +, and of
    /// ChrW/Chr codes, which is how Visual Studio writes a multi-line Text. Null for any other shape.
    /// </summary>
    private static string? TextConstant(ExpressionSyntax syntax)
    {
        switch (syntax)
        {
            case ParenthesizedExpressionSyntax parentheses:
                return TextConstant(parentheses.Expression);
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                return literal.Token.ValueText;
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.ConcatenateExpression)
                || binary.IsKind(SyntaxKind.AddExpression):
                return TextConstant(binary.Left) is { } left && TextConstant(binary.Right) is { } right
                    ? left + right : null;
            case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } invocation
                when member.Name.Identifier.ValueText is "ChrW" or "Chr"
                    && IsVisualBasicStrings(member.Expression)
                    && invocation.ArgumentList?.Arguments.OfType<SimpleArgumentSyntax>().ToArray() is [{ Expression: var code }]
                    && WinFormsDesignerValues.Evaluate(code, typeof(int)) is int character:
                return ((char)character).ToString();
            default:
                return null;
        }
    }

    private static bool IsVisualBasicStrings(ExpressionSyntax owner)
    {
        var name = owner.ToString();
        if (name.StartsWith("Global.", StringComparison.OrdinalIgnoreCase)) name = name["Global.".Length..];
        return name.Equals("Microsoft.VisualBasic", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Microsoft.VisualBasic.Strings", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The code that Localizable = False writes for each neutral entry under an ApplyResources prefix. A literal
    /// comes from the entry's value and the entry is then removed. A value the codec cannot write is referenced
    /// through GetObject and its entry stays, so nothing is lost. Images and icons always stay in the resx and
    /// are read back through GetObject, the way a form that was never localizable reads them.
    /// </summary>
    private (IReadOnlyList<WinFormsLocalizedAssignment> Assignments, IReadOnlyList<string> RemovedNames,
        IReadOnlyList<string> ReadOnlyNames) RestoredAssignments(
        WinFormsResourceDocument resources)
    {
        var assignments = new List<WinFormsLocalizedAssignment>();
        var removed = new List<string>();
        var readOnly = new List<string>();
        foreach (var entry in resources.Entries)
        {
            var separator = entry.Name.IndexOf('.', StringComparison.Ordinal);
            if (separator <= 0) continue;
            var owner = entry.Name[..separator];
            var property = entry.Name[(separator + 1)..];
            var controlName = owner.Equals(WinFormsDesignerDocument.FormResourcePrefix, StringComparison.Ordinal)
                ? "Me" : owner;
            if (!Document.ResourceOperations.Any(operation => operation.PropertyName is null
                && operation.ControlName.Equals(controlName, StringComparison.OrdinalIgnoreCase)
                && operation.ResourceKey == owner)) continue;
            // A value already referenced by its own statement stays where it is.
            if (Document.ResourceOperations.Any(operation => operation.PropertyName is not null
                && operation.ResourceKey == entry.Name)) continue;

            // Read from the resx, not the scaled surface. The property need not be [Localizable]: a value the
            // ApplyResources call applied is restored whatever Basalt.Forms marks, and a control the designer cannot
            // create (a project control) is restored from its entry's own type name.
            var control = FindControl(controlName);
            var descriptor = control is null ? null : TypeDescriptor.GetProperties(control)[property];
            // A read-only property (ListBox.Items) cannot be assigned, so no statement is written for it. Its entry stays
            // in the resx, where the designer's preview skips it too, and a copy carries the entry as a resource.
            if (descriptor?.IsReadOnly == true)
            {
                readOnly.Add(entry.Name);
                continue;
            }
            var declared = DeclaredTypeName(entry);
            var type = descriptor?.PropertyType ?? KnownResourceType(declared);
            var name = descriptor?.Name ?? property;

            if (IsPicture(type, declared))
            {
                assignments.Add(new(controlName, name, GetObjectExpression(entry.Name, type?.FullName ?? declared)));
                continue;
            }

            if (type is not null && WinFormsDesignerResources.TryRead(entry, type, out var value))
            {
                assignments.Add(new(controlName, name, LiteralExpression(value)));
                removed.Add(entry.Name);
                continue;
            }

            assignments.Add(new(controlName, name, GetObjectExpression(entry.Name, type?.FullName ?? declared)));
        }
        return (assignments, removed, readOnly);
    }

    /// <summary>The type a resx entry declares, without its assembly. An entry with no type is a string.</summary>
    private static string DeclaredTypeName(WinFormsResourceEntry entry) =>
        entry.TypeName is null ? typeof(string).FullName! : entry.TypeName.Split(',')[0].Trim();

    /// <summary>The types the resx codec can read back without loading an assembly; null for any other.</summary>
    private static Type? KnownResourceType(string declared) => declared switch
    {
        "System.String" => typeof(string),
        "System.Int32" => typeof(int),
        "System.Boolean" => typeof(bool),
        "System.Drawing.Color" => typeof(System.Drawing.Color),
        "System.Drawing.Point" => typeof(System.Drawing.Point),
        "System.Drawing.Size" => typeof(System.Drawing.Size),
        _ => null,
    };

    private static bool IsPicture(Type? type, string declared) =>
        type is not null && (typeof(System.Drawing.Image).IsAssignableFrom(type) || typeof(System.Drawing.Icon).IsAssignableFrom(type))
        || declared.Equals(typeof(System.Drawing.Image).FullName, StringComparison.Ordinal)
        || declared.Equals(typeof(System.Drawing.Icon).FullName, StringComparison.Ordinal);

    private string GetObjectExpression(string key, string typeName)
    {
        var manager = Document.ResourceManagerName
            ?? throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerUnsupportedExpression));
        return $"CType({manager}.GetObject(\"{key}\"), {typeName})";
    }

    /// <summary>
    /// The Visual Basic expression for a value read from the resx. A text with line breaks cannot be one string
    /// literal, so each control character is joined in with ChrW, the way Visual Studio writes multi-line Text.
    /// </summary>
    private static string LiteralExpression(object? value)
    {
        if (value is not string text || !text.Any(char.IsControl)) return WinFormsDesignerValues.Serialize(value);
        var pieces = new List<string>();
        var segment = new StringBuilder();
        void FlushSegment()
        {
            if (segment.Length == 0) return;
            pieces.Add("\"" + segment.ToString().Replace("\"", "\"\"", StringComparison.Ordinal) + "\"");
            segment.Clear();
        }
        foreach (var character in text)
        {
            if (char.IsControl(character))
            {
                FlushSegment();
                pieces.Add($"Global.Microsoft.VisualBasic.ChrW({(int)character})");
            }
            else
            {
                segment.Append(character);
            }
        }
        FlushSegment();
        return string.Join(" & ", pieces);
    }

    private static bool IsLocalizableProperty(PropertyDescriptor descriptor) =>
        descriptor.Attributes[typeof(LocalizableAttribute)] is LocalizableAttribute { IsLocalizable: true };

    /// <summary>The resource key Visual Studio uses for a control's property; the form's own properties use "$this".</summary>
    private static string ResourceNameFor(string controlName, string property) =>
        $"{(controlName.Equals("Me", StringComparison.OrdinalIgnoreCase) ? WinFormsDesignerDocument.FormResourcePrefix : controlName)}.{property}";

    /// <summary>A value to move; TypeName is null for a string, which the resx stores without a type.</summary>
    private sealed record LocalizableValue(string ControlName, string PropertyName, string Text, string? TypeName);
}
