using CommunityToolkit.Mvvm.ComponentModel;
using Basalt.Core.Model;

namespace Basalt.Shell.ViewModels;

/// <summary>A document open in an editor tab.</summary>
public sealed partial class EditorDocumentViewModel : ObservableObject
{
    [ObservableProperty] private string _text;
    [ObservableProperty] private bool _isModified;
    [ObservableProperty] private int _caretOffset;

    public EditorDocumentViewModel(string filePath, string text, bool openInDesigner = false)
    {
        FilePath = filePath;
        _text = text;
        OriginalText = text;
        OpenInDesigner = openInDesigner;
    }

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public SourceLanguage Language => SourceLanguageExtensions.FromPath(FilePath);

    /// <summary>The document is open in the visual designer rather than the text editor.</summary>
    public bool OpenInDesigner { get; }

    private string OriginalText { get; set; }

    /// <summary>
    /// Records that the text on disk now matches this document.
    ///
    /// Used after a refactoring writes the file: without it the tab shows a
    /// change the user did not make and did not need to save.
    /// </summary>
    public void MarkSaved()
    {
        OriginalText = Text;
        IsModified = false;
    }

    /// <summary>Tab title, with a marker when there are unsaved changes.</summary>
    public string Title => IsModified ? $"{FileName} •" : FileName;

    /// <summary>Tab symbol: distinguishes designer, VB and C#.</summary>
    public string Icon => OpenInDesigner
        ? "\U0001F3A8"
        : Language switch
        {
            SourceLanguage.VisualBasic => "VB",
            _ => "\U0001F4C4"
        };

    partial void OnTextChanged(string value)
    {
        IsModified = !string.Equals(value, OriginalText, StringComparison.Ordinal);
        OnPropertyChanged(nameof(Title));
    }

    partial void OnIsModifiedChanged(bool value) => OnPropertyChanged(nameof(Title));

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(FilePath, Text, ct).ConfigureAwait(false);
        OriginalText = Text;
        IsModified = false;
    }
}
