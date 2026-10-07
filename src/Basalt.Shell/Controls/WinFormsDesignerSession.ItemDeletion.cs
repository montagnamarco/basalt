using Basalt.Core.Localization;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

/// <summary>
/// Deletes a menu item, a ToolStrip/StatusStrip item, or an item nested inside a
/// ToolStripDropDownButton/SplitButton's own cascading drop-down, the way Visual Studio's
/// Delete does: the whole subtree's declarations (see
/// <see cref="WinFormsMenuDocument.RemoveItem"/>/<see cref="WinFormsToolStripDocument.RemoveItem"/>)
/// together with any resx entries those names owned ("ItemName.Property", the same convention
/// <see cref="WinFormsDesignerSession.ImageResources"/> writes under), in one undo step. The
/// companion code file's own Handles clauses are not this class's concern: the caller (see
/// WinFormsDesignerView.MenuInline/ToolStripInline/ToolStripDropDownInline) passes commit/undo/
/// redo hooks that edit them the same way WinFormsDesignerView.MultiDelete already does for a
/// deleted control.
/// </summary>
internal sealed partial class WinFormsDesignerSession
{
    internal Task<IReadOnlySet<string>> DeleteMenuItemAsync(string strip, string itemName,
        Action? commitCompanion = null, Action? undoCompanion = null, Action? redoCompanion = null)
    {
        var removal = Document.Menus.RemoveItem(strip, itemName);
        return CommitItemRemovalAsync(removal.Source, removal.RemovedNames, commitCompanion, undoCompanion, redoCompanion);
    }

    internal Task<IReadOnlySet<string>> DeleteDropDownItemAsync(string ownerName, string itemName,
        Action? commitCompanion = null, Action? undoCompanion = null, Action? redoCompanion = null)
    {
        var removal = Document.Menus.RemoveItem(ownerName, itemName);
        return CommitItemRemovalAsync(removal.Source, removal.RemovedNames, commitCompanion, undoCompanion, redoCompanion);
    }

    internal Task<IReadOnlySet<string>> DeleteToolStripItemAsync(string strip, string itemName,
        Action? commitCompanion = null, Action? undoCompanion = null, Action? redoCompanion = null)
    {
        // A ToolStripDropDownButton/SplitButton's own cascading DropDownItems tree is tracked by
        // WinFormsMenuDocument, rooted at itemName: this tree's only link back to itemName is the
        // "Me.<itemName>.DropDownItems.AddRange(...)" statement RemoveItem below is about to erase
        // as part of cleaning up itemName's own "Me.<itemName>.*" statements (it textually
        // contains "Me.<itemName>" too), so the nested tree must be read from the source before
        // RemoveItem runs, not after - reading it afterward would always find it empty, leaving
        // every nested field/statement behind as an orphan. An item that owns no such tree simply
        // reads back empty here either way, so no special-casing is needed for the common case.
        var nested = new WinFormsMenuDocument(Document.Source).Read(itemName);

        var removal = Document.ToolStripItems.RemoveItem(strip, itemName);
        var source = removal.Source;
        var removedNames = removal.RemovedNames;

        if (nested.Count > 0)
        {
            var nestedNames = nested.Select(child => child.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            source = new WinFormsMenuDocument(source).RemoveDeclarations(nestedNames);
            removedNames = removedNames.Concat(nestedNames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return CommitItemRemovalAsync(source, removedNames, commitCompanion, undoCompanion, redoCompanion);
    }

    private async Task<IReadOnlySet<string>> CommitItemRemovalAsync(string editedSource, IReadOnlySet<string> removedNames,
        Action? commitCompanion, Action? undoCompanion, Action? redoCompanion)
    {
        var original = Document;
        var beforeResources = _resources;
        var editedResources = beforeResources;

        if (beforeResources is not null)
        {
            var matches = beforeResources.Entries.Where(entry => removedNames.Any(name =>
                entry.Name.StartsWith(name + ".", StringComparison.Ordinal))).ToArray();
            if (matches.Length > 0)
            {
                if (PrepareResourceEdit is not null) await PrepareResourceEdit(beforeResources.Source);
                if (!ReferenceEquals(original, Document) || !ReferenceEquals(beforeResources, _resources))
                    throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
                editedResources = await Task.Run(() =>
                {
                    var resources = beforeResources;
                    foreach (var entry in matches) resources = new WinFormsResourceDocument(resources.Remove(entry.Name));
                    return resources;
                });
            }
        }

        var resourceChanged = beforeResources?.Source != editedResources?.Source;

        void RestoreResources(WinFormsResourceDocument? expected, WinFormsResourceDocument? replacement)
        {
            if (_resources?.Source != expected?.Source)
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            if (expected is not null && replacement is not null)
                CommitResourceSource?.Invoke(expected.Source, replacement.Source);
            _resources = replacement;
        }

        await ApplyAsync(_ => editedSource,
            () => { if (resourceChanged) RestoreResources(beforeResources, editedResources); commitCompanion?.Invoke(); },
            () => { if (resourceChanged) RestoreResources(editedResources, beforeResources); undoCompanion?.Invoke(); },
            () => { if (resourceChanged) RestoreResources(beforeResources, editedResources); redoCompanion?.Invoke(); },
            commitUnchangedSource: resourceChanged,
            undoPrepare: resourceChanged ? () => PrepareResourceEdit?.Invoke(editedResources!.Source) ?? Task.CompletedTask : null,
            redoPrepare: resourceChanged ? () => PrepareResourceEdit?.Invoke(beforeResources!.Source) ?? Task.CompletedTask : null);
        return removedNames;
    }
}
