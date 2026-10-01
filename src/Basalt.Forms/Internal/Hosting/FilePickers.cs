using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>One filter entry of a file picker: a description and the patterns it matches, e.g. "Text files" and "*.txt".</summary>
internal sealed record FilePickerFilter(string Name, IReadOnlyList<string> Patterns);

/// <summary>What FileDialog asks the platform for, independent of Avalonia's own option types so tests can fake it.</summary>
internal sealed record FilePickerRequest(
    string Title,
    string? InitialDirectory,
    string? SuggestedFileName,
    bool AllowMultiple,
    IReadOnlyList<FilePickerFilter> Filters,
    int FilterIndex,
    bool ShowOverwritePrompt);

/// <summary>What FolderBrowserDialog asks the platform for.</summary>
internal sealed record FolderPickerRequest(string Title, string? InitialDirectory, bool AllowMultiple);

/// <summary>
/// What a FileDialog or FolderBrowserDialog needs from the platform: open,
/// save and folder pickers that return local paths, or an empty result when
/// the user cancels. Behind an interface so dialog tests substitute a fake
/// and never show a real picker.
/// </summary>
internal interface IFilePickerService
{
    string[] PickOpenFiles(HostWindow? owner, FilePickerRequest request);

    string? PickSaveFile(HostWindow? owner, FilePickerRequest request);

    string[] PickFolders(HostWindow? owner, FolderPickerRequest request);
}

/// <summary>
/// The real pickers, through Avalonia's TopLevel.StorageProvider. Avalonia's
/// pickers are asynchronous; WinForms code calls ShowDialog and expects the
/// chosen path back once it returns, so the call waits while the dispatcher
/// keeps running, the same way PlatformClipboard waits for the clipboard.
/// </summary>
internal sealed class FilePickers : IFilePickerService
{
    /// <summary>The service dialogs use; tests substitute a fake here and must restore this field afterwards.</summary>
    public static IFilePickerService Current { get; set; } = new FilePickers();

    public string[] PickOpenFiles(HostWindow? owner, FilePickerRequest request)
    {
        if (owner?.StorageProvider is not { } provider) return [];

        var fileTypes = ToFileTypes(request.Filters);

        var options = new FilePickerOpenOptions
        {
            Title = request.Title,
            AllowMultiple = request.AllowMultiple,
            SuggestedFileName = request.SuggestedFileName,
            FileTypeFilter = fileTypes,
            SuggestedFileType = SelectedType(fileTypes, request.FilterIndex),
            SuggestedStartLocation = ResolveStartLocation(provider, request.InitialDirectory),
        };

        var files = SynchronousWait.For(provider.OpenFilePickerAsync(options));

        return [.. files.Select(StorageProviderExtensions.TryGetLocalPath).OfType<string>()];
    }

    public string? PickSaveFile(HostWindow? owner, FilePickerRequest request)
    {
        if (owner?.StorageProvider is not { } provider) return null;

        var fileTypes = ToFileTypes(request.Filters);

        var options = new FilePickerSaveOptions
        {
            Title = request.Title,
            SuggestedFileName = request.SuggestedFileName,
            FileTypeChoices = fileTypes,
            SuggestedFileType = SelectedType(fileTypes, request.FilterIndex),
            SuggestedStartLocation = ResolveStartLocation(provider, request.InitialDirectory),
            ShowOverwritePrompt = request.ShowOverwritePrompt,
        };

        var file = SynchronousWait.For(provider.SaveFilePickerAsync(options));

        return file is null ? null : StorageProviderExtensions.TryGetLocalPath(file);
    }

    public string[] PickFolders(HostWindow? owner, FolderPickerRequest request)
    {
        if (owner?.StorageProvider is not { } provider) return [];

        var options = new FolderPickerOpenOptions
        {
            Title = request.Title,
            AllowMultiple = request.AllowMultiple,
            SuggestedStartLocation = ResolveStartLocation(provider, request.InitialDirectory),
        };

        var folders = SynchronousWait.For(provider.OpenFolderPickerAsync(options));

        return [.. folders.Select(StorageProviderExtensions.TryGetLocalPath).OfType<string>()];
    }

    private static IReadOnlyList<FilePickerFileType>? ToFileTypes(IReadOnlyList<FilePickerFilter> filters)
    {
        if (filters.Count == 0) return null;

        return [.. filters.Select(filter => new FilePickerFileType(filter.Name) { Patterns = filter.Patterns })];
    }

    private static FilePickerFileType? SelectedType(IReadOnlyList<FilePickerFileType>? fileTypes, int filterIndex) =>
        fileTypes is not null && filterIndex >= 1 && filterIndex <= fileTypes.Count ? fileTypes[filterIndex - 1] : null;

    private static IStorageFolder? ResolveStartLocation(IStorageProvider provider, string? initialDirectory) =>
        string.IsNullOrEmpty(initialDirectory) ? null : SynchronousWait.For(provider.TryGetFolderFromPathAsync(initialDirectory));
}
