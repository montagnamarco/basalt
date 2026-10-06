using System.Collections.ObjectModel;
using Basalt.Core.Localization;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Basalt.Workspace.Refactoring;

namespace Basalt.Shell.Controls;

/// <summary>Reviews a fixed set of buffer and disk snapshots before applying an editor action.</summary>
internal sealed class EditorSmartTagTransaction
{
    private sealed record Snapshot(FileChangePreview Change, EditorDocumentViewModel? Document,
        string? DiskText, bool WasModified);

    private readonly ObservableCollection<EditorDocumentViewModel> _documents;
    private readonly IReadOnlyList<Snapshot> _snapshots;
    private readonly PathMatcher _matcher;

    private EditorSmartTagTransaction(ObservableCollection<EditorDocumentViewModel> documents,
        IReadOnlyList<Snapshot> snapshots, PathMatcher matcher)
    {
        _documents = documents;
        _snapshots = snapshots;
        _matcher = matcher;
    }

    internal static async Task<EditorSmartTagTransaction> CaptureAsync(RefactoringPreview preview,
        ObservableCollection<EditorDocumentViewModel> documents, PathMatcher? matcher = null)
    {
        matcher ??= new PathMatcher();
        var buffers = preview.Changes.Select(change => (Change: change, Document: Find(documents, change.FilePath, matcher))).ToArray();
        var snapshots = new List<Snapshot>();
        foreach (var (change, document) in buffers)
        {
            if (document is not null && document.Text != change.OriginalText)
            {
                throw Changed();
            }

            var modified = document?.IsModified == true;
            var disk = await ReadDiskAsync(change.FilePath);
            if (document is null && (disk ?? "") != change.OriginalText)
            {
                throw Changed();
            }

            snapshots.Add(new Snapshot(change, document, disk, modified));
        }

        var transaction = new EditorSmartTagTransaction(documents, snapshots, matcher);
        transaction.ValidateBuffers();
        return transaction;
    }

    internal async Task ApplyAsync(Func<bool>? canApply = null)
    {
        foreach (var snapshot in _snapshots)
        {
            if (await ReadDiskAsync(snapshot.Change.FilePath) != snapshot.DiskText)
            {
                throw Changed();
            }
        }

        if (canApply?.Invoke() == false)
        {
            throw Changed();
        }
        ValidateBuffers();
        var applied = new List<(Snapshot Snapshot, EditorDocumentViewModel Document, bool Created)>();
        try
        {
            foreach (var snapshot in _snapshots)
            {
                var document = snapshot.Document ?? new EditorDocumentViewModel(snapshot.Change.FilePath, snapshot.Change.OriginalText);
                applied.Add((snapshot, document, snapshot.Document is null));
                if (snapshot.Document is null)
                {
                    _documents.Add(document);
                }

                document.Text = snapshot.Change.NewText;
                ValidateBuffers(applied);
            }
        }
        catch (Exception failure)
        {
            var rollbackFailures = new List<Exception>();
            foreach (var (snapshot, document, created) in applied.AsEnumerable().Reverse())
            {
                // An observer can make another edit while notified. Never roll
                // that newer edit back just because this transaction failed.
                if (!ReferenceEquals(Find(_documents, snapshot.Change.FilePath, _matcher), document)
                    || document.Text != snapshot.Change.NewText && (!created || document.Text != snapshot.Change.OriginalText))
                {
                    continue;
                }

                try
                {
                    if (created)
                    {
                        _documents.Remove(document);
                    }
                    else
                    {
                        try
                        {
                            document.Text = snapshot.Change.OriginalText;
                        }
                        finally
                        {
                            if (document.Text == snapshot.Change.OriginalText)
                            {
                                document.IsModified = snapshot.WasModified;
                            }
                        }
                    }
                }
                catch (Exception rollbackFailure)
                {
                    rollbackFailures.Add(rollbackFailure);
                }
            }

            if (rollbackFailures.Count > 0)
            {
                throw new AggregateException(new[] { failure }.Concat(rollbackFailures));
            }

            throw;
        }
    }

    private void ValidateBuffers(IReadOnlyList<(Snapshot Snapshot, EditorDocumentViewModel Document, bool Created)>? applied = null)
    {
        foreach (var snapshot in _snapshots)
        {
            var written = applied?.FirstOrDefault(entry => ReferenceEquals(entry.Snapshot, snapshot));
            var expectedDocument = written?.Document ?? snapshot.Document;
            var expectedText = written?.Document is not null ? snapshot.Change.NewText : snapshot.Change.OriginalText;
            var current = Find(_documents, snapshot.Change.FilePath, _matcher);
            if (!ReferenceEquals(current, expectedDocument) || current is not null && current.Text != expectedText)
            {
                throw Changed();
            }
        }
    }

    internal static EditorDocumentViewModel? Find(IEnumerable<EditorDocumentViewModel> documents, string path,
        PathMatcher matcher) =>
        documents.FirstOrDefault(document => matcher.Same(document.FilePath, path));

    private static Task<string?> ReadDiskAsync(string path) => Task.Run(async () =>
        File.Exists(path) ? await File.ReadAllTextAsync(path) : null);

    private static InvalidOperationException Changed() => new(Localizer.Get(StringKeys.EditorSmartTagsChanged));
}
