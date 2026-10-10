using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>
/// Keeps a Localizable form's resource files in step with its controls. A control that is added or pasted moves its
/// localizable values into the neutral .resx and gets its ApplyResources call; a deleted control leaves its "Name.*"
/// entries in the neutral .resx and in every culture .resx; a renamed control renames them. Each of these is one undo
/// step together with the designer edit it belongs to. A form that is not Localizable takes the plain edit path.
/// </summary>
internal sealed partial class WinFormsDesignerSession
{
    /// <summary>
    /// The culture names of the form's culture .resx files on disk ("it", "fr-FR"). Null when the host cannot list them,
    /// in which case a deleted or renamed control's culture entries are not reached.
    /// </summary>
    internal Func<IReadOnlyList<string>>? ListCultureResources { get; set; }

    /// <summary>A culture .resx that one edit rewrites: the text it held, and the text it holds afterwards.</summary>
    private sealed record CultureResourceEdit(string Culture, WinFormsResourceDocument Before, WinFormsResourceDocument After);

    /// <summary>
    /// The resource files one designer edit writes. <see cref="Cultures"/> lists only the culture files whose text
    /// changes, so an edit never rewrites a culture file it does not touch. Reading them to find out is a separate step.
    /// </summary>
    private sealed record ControlResourceEdit(WinFormsResourceDocument? Neutral, WinFormsResourceDocument? NeutralAfter,
        IReadOnlyList<CultureResourceEdit> Cultures)
    {
        public bool NeutralChanged => Neutral?.Source != NeutralAfter?.Source;

        public bool Changed => NeutralChanged || Cultures.Count > 0;
    }

    /// <summary>
    /// Applies a designer edit that adds or deletes controls. Added controls move their values into the neutral .resx
    /// and deleted controls leave their entries in every .resx, but only while the form is Localizable: the values
    /// those resources hold exist only then.
    /// </summary>
    private async Task ApplyControlEditAsync(Func<WinFormsDesignerDocument, string> edit,
        Action? commitCompanion = null, Action? undoCompanion = null, Action? redoCompanion = null)
    {
        var original = Document;
        var edited = await Task.Run(() => new WinFormsDesignerDocument(edit(original)));
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(original, Document)) throw new InvalidOperationException("The document changed during the edit.");
        if (!IsLocalizable || edited.Source == original.Source)
        {
            await ApplyAsync(_ => edited.Source, commitCompanion, undoCompanion, redoCompanion);
            return;
        }
        if (_culture is not null)
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerNotInLanguage));

        var added = ControlNames(edited).Except(ControlNames(original), StringComparer.OrdinalIgnoreCase).ToArray();
        var removed = ControlNames(original).Except(ControlNames(edited), StringComparer.OrdinalIgnoreCase).ToArray();
        // The values come from the new control's code, read here on the interface thread, where the preview
        // evaluator may look at the controls it already shows.
        IReadOnlyList<LocalizableValue> moved = added.Length == 0 ? [] : MovableValues(edited,
            edited.Controls.Where(model => added.Contains(model.Name, StringComparer.OrdinalIgnoreCase)),
            model => WinFormsDesignTypes.Find(model.TypeName.Replace("Global.", "", StringComparison.OrdinalIgnoreCase), _designContext));

        var neutral = _resources;
        if (neutral is null && added.Length > 0) neutral = await CreateResourcesAsync();
        IReadOnlyList<(string Culture, WinFormsResourceDocument Document)> cultures = removed.Length > 0
            ? await LoadCultureDocumentsAsync() : [];
        EnsureUnchanged(original, neutral);

        var planned = await Task.Run(() =>
        {
            // neutral stays the text the edit starts from; neutralAfter is the text it leaves.
            var neutralAfter = neutral;
            foreach (var value in moved)
            {
                neutralAfter = new WinFormsResourceDocument(neutralAfter!.SetValue(
                    ResourceNameFor(value.ControlName, value.PropertyName), value.Text, value.TypeName));
            }
            var designer = added.Length == 0 ? edited
                : new WinFormsDesignerDocument(edited.LocalizeAddedControls(
                    moved.Select(value => (value.ControlName, value.PropertyName)).ToArray(), added));
            IReadOnlyList<CultureResourceEdit> cultureEdits = [];
            if (removed.Length > 0)
            {
                neutralAfter = neutralAfter is null ? null : new WinFormsResourceDocument(neutralAfter.RemoveEntriesUnder(removed));
                cultureEdits = ChangedCultures(cultures, document => document.RemoveEntriesUnder(removed));
            }
            return (Designer: designer, Edit: new ControlResourceEdit(neutral, neutralAfter, cultureEdits));
        });
        await CommitControlEditAsync(original, planned.Designer.Source, planned.Edit,
            commitCompanion, undoCompanion, redoCompanion);
    }

    /// <summary>
    /// Renames a field: the designer edit that the editor's rename produced, plus the keys the renamed control owns
    /// in the neutral .resx, every culture .resx and the ApplyResources call. The old name is the one control name
    /// the rename removes from the designer.
    /// </summary>
    private async Task ApplyControlRenameAsync(string renamedSource, string newName, Action commit, Action undo, Action redo)
    {
        var original = Document;
        var (renamed, oldName) = await Task.Run(() => ParseRenamed(original, renamedSource, newName));
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (oldName is null || !IsLocalizable)
        {
            await ApplyAsync(_ => renamed.Source, commit, undo, redo);
            return;
        }
        if (_culture is not null)
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerNotInLanguage));

        var neutral = _resources;
        var cultures = await LoadCultureDocumentsAsync();
        EnsureUnchanged(original, neutral);
        var planned = await Task.Run(() =>
        {
            var designer = new WinFormsDesignerDocument(renamed.RenameResourceKeys(oldName, newName));
            var neutralAfter = neutral is null ? null
                : new WinFormsResourceDocument(neutral.RenameEntries(oldName, newName));
            var edit = new ControlResourceEdit(neutral, neutralAfter,
                ChangedCultures(cultures, document => document.RenameEntries(oldName, newName)));
            return (Designer: designer, Edit: edit);
        });
        await CommitControlEditAsync(original, planned.Designer.Source, planned.Edit, commit, undo, redo);
    }

    /// <summary>
    /// The renamed designer and the control the rename renamed: the one control name that the rename removes. Null when
    /// the rename removes none or several, so the caller keeps the plain rename.
    /// </summary>
    private static (WinFormsDesignerDocument Document, string? OldName) ParseRenamed(WinFormsDesignerDocument original,
        string renamedSource, string newName)
    {
        var renamed = new WinFormsDesignerDocument(RenamedDesignerSource(renamedSource, newName));
        var gone = ControlNames(original).Except(ControlNames(renamed), StringComparer.OrdinalIgnoreCase).ToArray();
        return (renamed, gone is [var oldName] ? oldName : null);
    }

    /// <summary>
    /// Makes one undoable step of the designer edit and the resource files it writes. The files are opened first and
    /// written by the commit, the same way every other resource edit writes them, and undo and redo write them back.
    /// </summary>
    private async Task CommitControlEditAsync(WinFormsDesignerDocument original, string editedSource, ControlResourceEdit edit,
        Action? commitCompanion, Action? undoCompanion, Action? redoCompanion)
    {
        await PrepareControlResources(edit, forward: true);
        EnsureUnchanged(original, edit.Neutral);
        await ApplyAsync(_ => editedSource,
            () => WriteControlEdit(edit, forward: true, commitCompanion),
            () => WriteControlEdit(edit, forward: false, undoCompanion),
            () => WriteControlEdit(edit, forward: true, redoCompanion),
            commitUnchangedSource: edit.Changed,
            undoPrepare: edit.Changed ? () => PrepareControlResources(edit, forward: false) : null,
            redoPrepare: edit.Changed ? () => PrepareControlResources(edit, forward: true) : null);
    }

    /// <summary>Opens the neutral and culture files one direction of the edit writes.</summary>
    private async Task PrepareControlResources(ControlResourceEdit edit, bool forward)
    {
        var (neutralFrom, neutralTo) = forward ? (edit.Neutral, edit.NeutralAfter) : (edit.NeutralAfter, edit.Neutral);
        if (edit.NeutralChanged && neutralFrom is not null && neutralTo is not null)
            await PrepareNeutralResourceAsync(neutralFrom);
        foreach (var culture in edit.Cultures)
        {
            var (from, to) = forward ? (culture.Before, culture.After) : (culture.After, culture.Before);
            await PrepareEdit(true, culture.Culture, from.Source, to.Source);
        }
    }

    /// <summary>
    /// Makes one direction of the edit: every resource file is verified, then the companion edit runs (it checks its own
    /// buffers and throws before it changes any of them), and only then are the files written. A failure before the
    /// writes leaves every resource file as it was, so the step and the files still agree.
    /// </summary>
    private void WriteControlEdit(ControlResourceEdit edit, bool forward, Action? companion)
    {
        VerifyControlResources(edit, forward);
        companion?.Invoke();
        WriteControlResources(edit, forward);
    }

    /// <summary>
    /// Checks, without writing, that every file one direction of the edit writes still holds the text the edit expects:
    /// the neutral one held by this session, and each culture file the host holds.
    /// </summary>
    private void VerifyControlResources(ControlResourceEdit edit, bool forward)
    {
        var (neutralFrom, neutralTo) = forward ? (edit.Neutral, edit.NeutralAfter) : (edit.NeutralAfter, edit.Neutral);
        if (edit.NeutralChanged && neutralFrom is not null && neutralTo is not null && _resources?.Source != neutralFrom.Source)
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        foreach (var culture in edit.Cultures)
        {
            var (from, to) = forward ? (culture.Before, culture.After) : (culture.After, culture.Before);
            VerifyCultureResourceSource?.Invoke(culture.Culture, from.Source, to.Source);
        }
    }

    /// <summary>Writes one direction of the edit into the neutral and culture files the host holds.</summary>
    private void WriteControlResources(ControlResourceEdit edit, bool forward)
    {
        var (neutralFrom, neutralTo) = forward ? (edit.Neutral, edit.NeutralAfter) : (edit.NeutralAfter, edit.Neutral);
        if (edit.NeutralChanged && neutralFrom is not null && neutralTo is not null)
        {
            if (_resources?.Source != neutralFrom.Source)
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            CommitResourceSource?.Invoke(neutralFrom.Source, neutralTo.Source);
            _resources = neutralTo;
        }
        foreach (var culture in edit.Cultures)
        {
            var (from, to) = forward ? (culture.Before, culture.After) : (culture.After, culture.Before);
            CommitCultureResourceSource?.Invoke(culture.Culture, from.Source, to.Source);
        }
    }

    /// <summary>
    /// Every culture .resx on disk, read through the host's buffers. A culture whose file does not exist is skipped:
    /// it has no entries to remove or rename.
    /// </summary>
    private async Task<List<(string Culture, WinFormsResourceDocument Document)>> LoadCultureDocumentsAsync()
    {
        var documents = new List<(string Culture, WinFormsResourceDocument Document)>();
        if (ListCultureResources is null || LoadCultureResources is null) return documents;
        foreach (var culture in ListCultureResources())
        {
            var document = await LoadCultureResources(culture);
            if (document is not null) documents.Add((culture, document));
        }
        return documents;
    }

    /// <summary>The culture files whose text <paramref name="rewrite"/> changes, with the text each holds before and after.</summary>
    private static IReadOnlyList<CultureResourceEdit> ChangedCultures(
        IEnumerable<(string Culture, WinFormsResourceDocument Document)> cultures, Func<WinFormsResourceDocument, string> rewrite) =>
        cultures.Select(culture => new CultureResourceEdit(culture.Culture, culture.Document,
                new WinFormsResourceDocument(rewrite(culture.Document))))
            .Where(edit => edit.After.Source != edit.Before.Source)
            .ToArray();

    private static HashSet<string> ControlNames(WinFormsDesignerDocument document) =>
        document.Controls.Select(control => control.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
