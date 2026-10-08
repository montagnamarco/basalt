namespace Basalt.Shell.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task SaveWinFormsCompanionAsync(EditorDocumentViewModel document)
    {
        var path = document.FilePath;
        string formPath;
        if (path.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase))
        {
            formPath = path[..^".Designer.vb".Length];
        }
        else if (path.EndsWith(".vb", StringComparison.OrdinalIgnoreCase))
        {
            formPath = path[..^".vb".Length];
        }
        else if (path.EndsWith(".resx", StringComparison.OrdinalIgnoreCase))
        {
            formPath = path[..^".resx".Length];
        }
        else
        {
            return;
        }

        // Resource files also belong to unrelated projects. Only a form's
        // existing code/designer pair identifies this coordinated save.
        if (!HasFormCompanion(formPath + ".vb") || !HasFormCompanion(formPath + ".Designer.vb"))
        {
            return;
        }

        var siblings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            formPath + ".vb",
            formPath + ".Designer.vb",
            formPath + ".resx",
        };
        // Designer declarations, Handles bindings and resources form one saved
        // form. Resolve each path again after awaits, since tabs may be replaced.
        var pendingPaths = OpenDocuments.Where(item => !ReferenceEquals(item, document)
            && item.IsModified && siblings.Contains(item.FilePath))
            .Select(item => item.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var companionPath in pendingPaths)
        {
            var companion = OpenDocuments.FirstOrDefault(item =>
                string.Equals(item.FilePath, companionPath, StringComparison.OrdinalIgnoreCase));
            if (companion is null || !companion.IsModified)
            {
                continue;
            }
            // Formatted like a save of its own: Save All skips a companion saved here, so this is
            // the only save it gets.
            if (FormatOnSave && !companion.OpenInDesigner)
                await FormatDocumentAsync(companion).ConfigureAwait(true);

            await companion.SaveAsync().ConfigureAwait(true);
            if (companion.FilePath.EndsWith(".vb", StringComparison.OrdinalIgnoreCase))
            {
                await _languageService.UpdateDocumentAsync(companion.FilePath, companion.Text).ConfigureAwait(true);
            }
        }
    }

    private bool HasFormCompanion(string path) => File.Exists(path)
        || OpenDocuments.Any(document => string.Equals(document.FilePath, path, StringComparison.OrdinalIgnoreCase));
}
