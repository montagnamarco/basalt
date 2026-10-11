using Basalt.Shell.ViewModels;

namespace Basalt.Shell;

/// <summary>
/// The jumps the user makes through the code, and what Navigate Back and
/// Forward return to.
///
/// A jump is recorded twice: where it starts, and where it lands. Recording
/// only the start left Navigate Back with nothing before the first jump, so
/// it did nothing after Go to Definition.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Opens a file at a line and column as a jump Navigate Back can undo.
    /// </summary>
    private async Task JumpToAsync(string filePath, int line, int column)
    {
        if (!File.Exists(filePath)) return;

        RecordCurrentPosition();
        await ViewModel.OpenFileAsync(filePath);

        // The editor is created by the document change, so the caret moves
        // once it exists, and the landing is recorded with the move.
        if (ViewModel.ActiveDocument is { } target)
            GoToLineWhenReady(target, line, column, landed: RecordCurrentPosition);
    }

    /// <summary>Moves the caret within the document in front, as a jump Navigate Back can undo.</summary>
    private void JumpWithinDocument(int line, int column = 1)
    {
        RecordCurrentPosition();
        GoToLine(line, column);
        RecordCurrentPosition();
    }

    /// <summary>
    /// Puts the caret where it was when the place was recorded: at its offset,
    /// so the column comes back as well as the line.
    /// </summary>
    private static void PlaceCaretIn(AvaloniaEdit.TextEditor editor, NavigationPoint point)
    {
        editor.CaretOffset = Math.Clamp(point.Offset, 0, editor.Document.TextLength);

        // The file may have changed since the place was recorded, so the line
        // is clamped rather than trusted.
        editor.ScrollToLine(Math.Clamp(point.Line, 1, editor.Document.LineCount));
        editor.TextArea.Focus();
    }

    /// <summary>
    /// Opens a file of the solution by typing part of its name.
    /// </summary>
    private async Task GoToFileAsync()
    {
        if (SolutionFolder() is not { } folder) return;

        // Walking a large solution is file-system work, kept off the interface thread.
        var files = await Task.Run(() => GoToFileDialog.ListFiles(folder)).ConfigureAwait(true);

        var dialog = new GoToFileDialog(files);
        var chosen = await dialog.ShowDialog<FileChoice?>(this);

        if (chosen is not null) await JumpToAsync(chosen.FilePath, 1, 1);
    }

    /// <summary>
    /// The folder the solution lives in, or the folder of the document in
    /// front when no solution is open.
    /// </summary>
    private string? SolutionFolder()
    {
        if (ViewModel.SolutionPath is { } path)
            return Path.GetDirectoryName(Path.GetFullPath(path));

        return ViewModel.ActiveDocument is { } document
            ? Path.GetDirectoryName(Path.GetFullPath(document.FilePath))
            : null;
    }
}
