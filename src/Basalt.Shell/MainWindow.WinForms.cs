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

    /// <summary>
    /// Writes a file the project does not have yet, such as the .resx a form
    /// gains the first time it needs one, and opens it the same way any other
    /// companion file is opened.
    /// </summary>
    private async Task<EditorDocumentViewModel> CreateWinFormsCodeAsync(string path, string content)
    {
        if (FindWinFormsCode(path) is { } existing) return existing;
        await File.WriteAllTextAsync(path, content);
        var document = new EditorDocumentViewModel(path, content);
        ViewModel.OpenDocuments.Add(document);
        return document;
    }
}
