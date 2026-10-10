using Basalt.Core.Localization;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Basalt.Workspace.Refactoring;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private readonly Func<string, string, int, string, Task<RefactoringPreview>>? _rename;

    private async Task RenameFieldAsync(string name, string? newName)
    {
        var session = _session!;
        if (name == newName) return;
        if (_rename is null || _getCode is null || newName is null)
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeUnavailable));
        var original = session.Document;
        var position = await Task.Run(() =>
        {
            original.ValidateFieldRename(name, newName);
            return original.FieldPosition(name);
        });
        var preview = await _rename(_document.FilePath, original.Source, position, newName);
        if (!preview.CanApply) throw new InvalidOperationException(preview.Problem);
        var designerChange = preview.Changes.Single(change =>
            string.Equals(change.FilePath, _document.FilePath, StringComparison.OrdinalIgnoreCase));
        if (designerChange.OriginalText != original.Source || !ReferenceEquals(original, session.Document))
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        var edits = new List<(EditorDocumentViewModel Document, FileChangePreview Change)>();
        foreach (var change in preview.Changes.Where(change => !ReferenceEquals(change, designerChange)))
            edits.Add((await _getCode(change.FilePath), change));
        var fieldsBefore = original.Controls.Select(model => model.Name)
            .Concat(original.Components.Select(model => model.Name)).Prepend("Me").ToArray();
        var fieldsAfter = fieldsBefore.Select(field => field.Equals(name, StringComparison.OrdinalIgnoreCase) ? newName : field).ToArray();
        var companionEdit = edits.FirstOrDefault(edit => string.Equals(edit.Document.FilePath, CompanionPath, StringComparison.OrdinalIgnoreCase));
        var beforeBindings = _handlers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var afterBindings = companionEdit.Document is null
            ? beforeBindings.ToDictionary(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase) ? newName : pair.Key,
                pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            : await Task.Run(() => fieldsAfter.ToDictionary(field => field,
                field => WinFormsEventHandlers.Read(companionEdit.Change.NewText, original.ClassName, field), StringComparer.OrdinalIgnoreCase));

        void Restore(bool forward)
        {
            // The designer buffer must show the text the session holds. The session committed the rename, ApplyResources key
            // renames included, and every step restores from that text: a plain Roslyn rename text would never match after
            // an undo's rewrite of the keys. Checked before any buffer changes.
            if (_document.Text != session.Document.Source)
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            var currentEdits = edits.Select(edit => (Document: ResolveTransactionDocument(edit.Document), edit.Change)).ToArray();
            foreach (var (document, change) in currentEdits)
                if (document.Text != (forward ? change.OriginalText : change.NewText))
                    throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            foreach (var (document, change) in currentEdits)
                document.Text = forward ? change.NewText : change.OriginalText;
            _handlers.Clear();
            foreach (var pair in forward ? afterBindings : beforeBindings) _handlers[pair.Key] = pair.Value;
            _selectedName = forward ? newName : name;
        }
        if (!ReferenceEquals(original, session.Document) || _document.Text != original.Source)
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        await session.ApplyFieldRenameAsync(designerChange.NewText, newName,
            () => Restore(true), () => Restore(false), () => Restore(true));
    }
}
