using Basalt.Core.Localization;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell;

public partial class MainWindow
{
    /// <summary>
    /// The WinForms designer the user is looking at, if any: Cut, Copy and Paste then mean its
    /// controls. The window runs those shortcuts before the designer sees the key, so without this
    /// they went to a text editor that was not there and the designer never copied anything.
    /// </summary>
    private Basalt.Shell.Controls.WinFormsDesignerView? WinFormsDesignerInFront
    {
        get
        {
            var document = ViewModel.ActiveDocument;
            var host = document is not null
                && _documents.TryGetValue(document, out var dockable)
                && _contents.TryGetValue(dockable, out var content)
                    ? content as Basalt.Shell.Controls.DesignerHost : null;

            return host is { ShowingDesigner: true, DesignerView: Basalt.Shell.Controls.WinFormsDesignerView forms }
                && forms.IsEffectivelyVisible ? forms : null;
        }
    }

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
    /// companion file is opened. A file that already exists on disk is never
    /// overwritten: the designer creates a resx only when it could not read one,
    /// and an empty template written over it would destroy its values.
    /// </summary>
    private async Task<EditorDocumentViewModel> CreateWinFormsCodeAsync(string path, string content)
    {
        if (FindWinFormsCode(path) is { } existing) return existing;
        if (File.Exists(path))
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerResourcesNotReplaced));
        await File.WriteAllTextAsync(path, content);
        var document = new EditorDocumentViewModel(path, content);
        ViewModel.OpenDocuments.Add(document);
        return document;
    }
}
