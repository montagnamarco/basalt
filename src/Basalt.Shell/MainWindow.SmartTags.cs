using Avalonia.Controls;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Workspace.Refactoring;

namespace Basalt.Shell;

public sealed partial class MainWindow
{
    private Func<bool>? _smartTagRefactoringCurrent;

    private void WireSmartTags(CodeEditor editor)
    {
        editor.SmartTagCommandChosen += async (_, request) =>
        {
            if (_smartTagRefactoringCurrent is not null)
            {
                return;
            }

            bool Current() => ReferenceEquals(ActingDocument, request.Document)
                && ViewModel.OpenDocuments.Contains(request.Document)
                && editor.IsSmartTagRequestCurrent(request.Source, request.Caret, request.Generation);
            if (!Current())
            {
                return;
            }

            _smartTagRefactoringCurrent = Current;
            try
            {
                await RunEditorCommandAsync(request.Command, editor);
            }
            catch (Exception exception)
            {
                ViewModel.WriteOutput(Localizer.Get(StringKeys.EditorSmartTagsFailed, exception.Message));
            }
            finally
            {
                _smartTagRefactoringCurrent = null;
            }
        };
        editor.SmartTagActionChosen += async (_, request) =>
        {
            try
            {
                await ApplySmartTagAsync(editor, request);
            }
            catch (Exception exception)
            {
                ViewModel.WriteOutput(Localizer.Get(StringKeys.EditorSmartTagsFailed, exception.Message));
            }
        };
    }

    /// <summary>
    /// Computes a refactoring preview on a thread-pool thread.
    ///
    /// Rename, extract variable and extract method are started from menus,
    /// keys and smart-tag actions alike, always from an interface event
    /// handler. The preview methods only return a Task: whatever Roslyn work
    /// they do before their first await (symbol search, semantic model) runs
    /// synchronously on the caller, so every caller goes through here and the
    /// result is awaited back on the interface thread.
    /// </summary>
    private static Task<RefactoringPreview> PreviewOffUiThreadAsync(
        Func<Task<RefactoringPreview>> previewAsync) =>
        Task.Run(previewAsync);

    private async Task ApplySmartTagAsync(CodeEditor editor, EditorSmartTagAction request)
    {
        bool Current() => ReferenceEquals(ActingDocument, request.Document)
            && ViewModel.OpenDocuments.Contains(request.Document)
            && editor.IsSmartTagRequestCurrent(request.Source, request.Caret, request.Generation);

        if (!Current())
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.EditorSmartTagsChanged));
        }

        var preview = await Task.Run(() => ViewModel.PreviewQuickActionAsync(request.Document.FilePath, request.Action));
        if (!Current())
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.EditorSmartTagsChanged));
        }

        if (!preview.CanApply)
        {
            ViewModel.WriteOutput(preview.Problem ?? Localizer.Get(StringKeys.EditorSmartTagsNone));
            return;
        }

        await ApplySmartTagPreviewAsync(preview, Current);
    }

    private async Task ApplySmartTagPreviewAsync(RefactoringPreview preview, Func<bool> current)
    {
        if (!current())
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.EditorSmartTagsChanged));
        }

        var transaction = await EditorSmartTagTransaction.CaptureAsync(preview, ViewModel.OpenDocuments);
        if (!current())
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.EditorSmartTagsChanged));
        }

        var dialog = new RefactoringPreviewDialog(preview);
        await dialog.ShowDialog(this);
        if (!dialog.Accepted)
        {
            return;
        }

        await transaction.ApplyAsync(current);
        ShowSmartTagDocumentTabs(preview.Changes.Select(change => change.FilePath));
    }

    /// <summary>
    /// Gives every document a smart-tag action touched a tab of its own.
    ///
    /// EditorSmartTagTransaction keeps the whole change in memory: it adds a
    /// document for a file that was not open to <see cref="MainWindowViewModel.OpenDocuments"/>
    /// so the edit has somewhere to live, but nothing else turns an open
    /// document into a dockable tab except <see cref="ShowActiveDocument"/>.
    /// Without this, a fix that reaches into a closed file leaves its change
    /// sitting in the open-document list with no tab pointing at it, invisible
    /// until the IDE exits and the edit, never saved, is gone.
    /// </summary>
    private void ShowSmartTagDocumentTabs(IEnumerable<string> paths)
    {
        if (_factory.DocumentArea is not { } area)
        {
            // Said rather than skipped: the documents are already in the open
            // list, and a buffer with a change and no tab is invisible.
            ViewModel.WriteOutput(Localizer.Get(StringKeys.EditorSmartTagsNoDocumentArea));
            return;
        }

        foreach (var path in paths)
        {
            var document = ViewModel.OpenDocuments.FirstOrDefault(
                d => string.Equals(d.FilePath, path, StringComparison.Ordinal));
            if (document is null || _documents.ContainsKey(document))
            {
                continue;
            }

            var dockable = new IdeDocument(document);
            _documents[document] = dockable;
            _factory.AddDockable(area, dockable);
        }
    }
}
