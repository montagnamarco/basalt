using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    /// <summary>Names removed by deleting the selected controls, including their nested children.</summary>
    public IReadOnlySet<string> DeletedControlNames(IReadOnlyList<Forms.Control> selectedControls)
    {
        ArgumentNullException.ThrowIfNull(selectedControls);
        var roots = SelectedDeletionRoots(selectedControls);
        var removed = roots.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool changed;
        do
        {
            changed = false;
            foreach (var control in Document.Controls)
            {
                if (removed.Contains(control.ParentName) && removed.Add(control.Name)) changed = true;
            }
        } while (changed);
        return removed;
    }

    /// <summary>Removes a selected group and its descendants through one document and undo transaction.</summary>
    public Task DeleteControlsAsync(IReadOnlyList<Forms.Control> selectedControls,
        Action? commitCompanion = null, Action? undoCompanion = null, Action? redoCompanion = null)
    {
        ArgumentNullException.ThrowIfNull(selectedControls);
        var roots = SelectedDeletionRoots(selectedControls);
        if (roots.Length == 0) return Task.CompletedTask;
        return ApplyControlEditAsync(document =>
        {
            foreach (var name in roots)
                document = new(document.RemoveControl(name));
            return document.Source;
        }, commitCompanion, undoCompanion, redoCompanion);
    }

    private string[] SelectedDeletionRoots(IReadOnlyList<Forms.Control> selectedControls)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var control in selectedControls)
        {
            ArgumentNullException.ThrowIfNull(control);
            if (ReferenceEquals(control, Form))
                throw new ArgumentException("The root form cannot be deleted.", nameof(selectedControls));
            // NameOf uses instance identity, so a stale control from a previous preview fails here.
            var name = NameOf(control);
            // A container's panel is named Owner.Panel and has no model of its own: it is refused, not looked up.
            if (name.Contains('.', StringComparison.Ordinal))
                throw new NotSupportedException(Basalt.Core.Localization.Localizer.Get(Basalt.Core.Localization.StringKeys.WinFormsDesignerPanelNotSelectable));
            selected.Add(name);
        }

        var models = Document.Controls.ToDictionary(control => control.Name, StringComparer.OrdinalIgnoreCase);
        return selected.Where(name =>
        {
            var parent = models[name].ParentName;
            while (!parent.Equals("Me", StringComparison.OrdinalIgnoreCase))
            {
                if (selected.Contains(parent)) return false;
                if (!models.TryGetValue(parent, out var parentModel)) break;
                parent = parentModel.ParentName;
            }
            return true;
        }).ToArray();
    }
}
