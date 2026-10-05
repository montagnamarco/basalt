using Basalt.Core.Localization;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell;

public partial class MainWindow
{
    private EditorDocumentViewModel? FindWinFormsCode(string path) => ViewModel.OpenDocuments.FirstOrDefault(document =>
        string.Equals(document.FilePath, path, StringComparison.Ordinal) && !document.OpenInDesigner);

    private async Task<EditorDocumentViewModel> OpenWinFormsCodeAsync(string path)
    {
        await ViewModel.OpenFileAsync(path);
        return FindWinFormsCode(path) ?? throw new InvalidOperationException(
            Localizer.Get(StringKeys.WinFormsDesignerFormCodeNotOpened));
    }

    private async Task<EditorDocumentViewModel> GetWinFormsCodeAsync(string path)
    {
        if (FindWinFormsCode(path) is { } existing) return existing;
        var text = await File.ReadAllTextAsync(path);
        var document = new EditorDocumentViewModel(path, text);
        ViewModel.OpenDocuments.Add(document);
        return document;
    }
}
