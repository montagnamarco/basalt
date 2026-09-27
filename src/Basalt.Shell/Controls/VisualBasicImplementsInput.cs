using AvaloniaEdit;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell.Controls;

/// <summary>
/// Writes the members an Implements or Inherits line obliges its type to
/// have, once Enter has finished that line: "Implements IDisposable" and
/// Enter write Dispose, as Visual Studio does for Visual Basic.
/// </summary>
/// <remarks>
/// A step of its own after Enter, and its own undo step, as in Visual Studio,
/// where Ctrl+Z takes the members back and leaves the new line. The answer
/// is applied only if nothing has moved since it was asked for: a member
/// written into a file the user has gone on typing in would land in the
/// wrong place.
/// </remarks>
internal static class VisualBasicImplementsInput
{
    private sealed record Edit(int Start, int Length, string Text);

    /// <summary>
    /// Run when the answer has arrived and before it is checked, so a test
    /// can type in between, as a user may while Roslyn is working.
    /// </summary>
    internal static Action? AnswerArrivedForTests { get; set; }

    public static async Task HandleAsync(
        TextEditor editor, MainWindowViewModel shell, string filePath, int finishedLine, Action<Action> edit)
    {
        var document = editor.Document;
        var version = document.Version;
        var caret = editor.CaretOffset;
        var source = document.Text;

        var changes = await shell.ImplementMembersAsync(filePath, source, finishedLine).ConfigureAwait(true);
        if (changes.Count == 0) return;

        AnswerArrivedForTests?.Invoke();

        if (!ReferenceEquals(editor.Document, document) || document.Version != version ||
            editor.CaretOffset != caret || editor.SelectionLength != 0)
            return;

        var finishedStart = document.GetLineByNumber(finishedLine + 1).Offset;

        var edits = changes
            .Select(change => new Edit(change.Span.Start, change.Span.Length, change.NewText ?? ""))
            .OrderBy(change => change.Start)
            .ToList();

        edit(() =>
        {
            // One undo step for the members and any Imports a fix added.
            // From the end backwards, so each edit's offsets still hold.
            using (document.RunUpdate())
            {
                for (var index = edits.Count - 1; index >= 0; index--)
                    document.Replace(edits[index].Start, edits[index].Length, edits[index].Text);
            }

            // Roslyn rewrites the indented empty line Enter opened. The caret
            // stays on the line after the one Enter finished, below
            // Implements, rather than being carried past the members.
            if (Map(caret, edits) is { } mapped)
            {
                editor.CaretOffset = mapped;
            }
            else
            {
                var finished = document.GetLineByOffset(Map(finishedStart, edits) ?? finishedStart);
                editor.CaretOffset = finished.NextLine?.Offset ?? finished.EndOffset;
            }
        });
    }

    /// <summary>
    /// Where a position lands once the edits, given in order against the
    /// text before them, are made; null when an edit rewrites it.
    /// </summary>
    private static int? Map(int position, IReadOnlyList<Edit> edits)
    {
        var shift = 0;

        foreach (var change in edits)
        {
            if (change.Start >= position) break;

            if (change.Start + change.Length >= position) return null;

            shift += change.Text.Length - change.Length;
        }

        return position + shift;
    }
}
