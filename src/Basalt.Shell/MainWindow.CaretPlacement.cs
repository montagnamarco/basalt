using Avalonia.Threading;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell;

public partial class MainWindow
{
    /// <summary>
    /// Moves the caret once the document just opened has a realised editor.
    /// </summary>
    /// <remarks>
    /// A document's editor is created by the dock reacting to the document
    /// change, which needs a layout pass of its own. Posting <see cref="GoToLine"/>
    /// once at <see cref="DispatcherPriority.Background"/> assumed that pass
    /// would already have happened by its turn; for the very first document a
    /// window opens there has been no earlier layout pass to piggyback on, so
    /// the posted callback ran while <see cref="CurrentEditor"/> was still
    /// null and the caret move was silently dropped — the file opened, but on
    /// line 1 rather than where the caller asked. Reposting itself at the same
    /// priority gives that pass a few more turns to run before giving up.
    /// </remarks>
    private void GoToLineWhenReady(
        EditorDocumentViewModel target, int line, int column = 1, int attemptsLeft = 20)
    {
        // Moved on to another tab meanwhile: the caret there is not this one's to move.
        if (!ReferenceEquals(ViewModel.ActiveDocument, target)) return;

        // The target's own editor, not CurrentEditor(): until the target's is realised that falls
        // back to the only editor open, which would be another file's.
        if (EditorFor(target) is not null)
        {
            GoToLine(line, column);
            return;
        }

        if (attemptsLeft <= 0) return;

        Dispatcher.UIThread.Post(
            () => GoToLineWhenReady(target, line, column, attemptsLeft - 1),
            DispatcherPriority.Background);
    }
}
