using Basalt.Workspace.Refactoring;
using Basalt.Core.Localization;

namespace Basalt.Shell.ViewModels;

public sealed partial class MainWindowViewModel
{
    public async Task<RefactoringPreview> PreviewWinFormsRenameAsync(string path, string source, int position, string name)
    {
        var snapshots = OpenDocuments.GroupBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var dirty = group.Where(document => document.IsModified).Select(document => document.Text).Distinct().ToArray();
                if (dirty.Length > 1) throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
                return (Path: group.Key, Text: dirty.FirstOrDefault() ?? group.First().Text);
            }).Where(snapshot => !string.Equals(snapshot.Path, path, StringComparison.OrdinalIgnoreCase)).ToArray();
        return await Task.Run(async () =>
        {
            foreach (var snapshot in snapshots)
                await _languageService.UpdateDocumentAsync(snapshot.Path, snapshot.Text).ConfigureAwait(false);
            await _languageService.UpdateDocumentAsync(path, source).ConfigureAwait(false);
            return await _languageService.PreviewRenameAsync(path, position, name).ConfigureAwait(false);
        });
    }
}
