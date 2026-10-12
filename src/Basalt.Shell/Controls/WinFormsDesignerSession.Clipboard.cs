using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    internal WinFormsControlClipboard CopyControls(IEnumerable<Forms.Control> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        var names = controls.Select(NameOf).ToArray();
        var (values, readOnlyEntries) = ResourceValuesOf(names);
        return Document.CopyControls(names, values, _resources, readOnlyEntries);
    }

    /// <summary>
    /// The values the copied controls' ApplyResources calls read from the neutral .resx, resolved the way the
    /// Localizable = False writer resolves them. The clipboard carries them as literal assignments, so a paste into a
    /// form that is not localizable gets the controls' own values, and a paste into a Localizable form moves them the
    /// way any pasted value moves. A form whose copied controls have no ApplyResources call needs no resource file.
    /// The second list names the copied controls' entries for read-only properties (ListBox.Items): no literal is made
    /// for them, and the copy carries their entries as resources, the way it carries a picture.
    /// </summary>
    private (IReadOnlyCollection<WinFormsLocalizedAssignment> Assignments, IReadOnlyCollection<string> ReadOnlyEntries)
        ResourceValuesOf(IReadOnlyCollection<string> names)
    {
        if (!Document.ResourceOperations.Any(operation => operation.PropertyName is null
            && names.Contains(operation.ControlName, StringComparer.OrdinalIgnoreCase)))
            return ([], []);
        var resources = _resources
            ?? throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerResourcesUnreadable));
        var (assignments, _, readOnly) = RestoredAssignments(resources);
        return (assignments.Where(assignment => names.Contains(assignment.ControlName, StringComparer.OrdinalIgnoreCase)).ToArray(),
            readOnly.Where(entry => names.Contains(entry[..entry.IndexOf('.', StringComparison.Ordinal)], StringComparer.OrdinalIgnoreCase))
                .ToArray());
    }

    internal async Task CheckPastableAsync(WinFormsControlClipboard payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var original = Document;
        // The rewrite and the parse are Roslyn work: they run on a background thread, as the paste does.
        var pasted = await Task.Run(() => original.PasteControls(payload, "Me"));
        ObjectDisposedException.ThrowIf(_disposed, this);
        var parsed = await Task.Run(() => new WinFormsDesignerDocument(pasted.Source));
        ObjectDisposedException.ThrowIf(_disposed, this);
        // The preview is built on the interface thread, as the paste builds it; a throwing preview refuses the cut here.
        using var preview = new WinFormsDesignerSession(parsed, null, _projectPath, _designContext);
        _ = preview.PreviewProblems;
    }

    internal async Task<IReadOnlyList<string>> PasteControlsAsync(
        WinFormsControlClipboard clipboard, Forms.Control parent)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(parent);
        var parentName = NameOf(parent);
        var original = Document;
        var pasted = await Task.Run(() => original.PasteControls(clipboard, parentName));
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(original, Document)) throw new InvalidOperationException("The document changed during the edit.");
        if (pasted.Resources.Count == 0)
            await ApplyControlEditAsync(_ => pasted.Source);
        else
            await ApplyPastedResourcesAsync(original, pasted);
        return pasted.TopLevelNames;
    }

    /// <summary>
    /// Pastes controls that read resx entries: a picture is a GetObject read of its own entry. The entries are written to
    /// this form's own .resx under the keys the pasted controls now read, in the same undo step as the designer edit. A
    /// Localizable form also moves the pasted control's own values to its neutral .resx, as every other paste does, so the
    /// copy is as localizable as the form it lands in.
    /// </summary>
    private async Task ApplyPastedResourcesAsync(WinFormsDesignerDocument original, WinFormsControlPasteResult pasted)
    {
        RefuseImageInLanguage();
        var neutral = _resources ?? await CreateResourcesAsync();
        EnsureUnchanged(original, neutral);

        var designer = new WinFormsDesignerDocument(pasted.Source);
        var added = ControlNames(designer).Except(ControlNames(original), StringComparer.OrdinalIgnoreCase).ToArray();
        var neutralAfter = neutral;
        if (IsLocalizable && added.Length > 0)
        {
            IReadOnlyList<LocalizableValue> moved = MovableValues(designer,
                designer.Controls.Where(model => added.Contains(model.Name, StringComparer.OrdinalIgnoreCase)),
                model => WinFormsDesignTypes.Find(model.TypeName.Replace("Global.", "", StringComparison.OrdinalIgnoreCase), _designContext));
            foreach (var value in moved)
            {
                neutralAfter = new WinFormsResourceDocument(neutralAfter.SetValue(
                    ResourceNameFor(value.ControlName, value.PropertyName), value.Text, value.TypeName));
            }
            designer = new WinFormsDesignerDocument(designer.LocalizeAddedControls(
                moved.Select(value => (value.ControlName, value.PropertyName)).ToArray(), added));
        }
        foreach (var entry in pasted.Resources)
        {
            neutralAfter = new WinFormsResourceDocument(neutralAfter.SetValue(
                entry.Name, entry.Value, entry.TypeName, entry.MimeType, entry.Comment));
        }

        await CommitControlEditAsync(original, designer.Source,
            new ControlResourceEdit(neutral, neutralAfter, []), null, null, null);
    }
}
