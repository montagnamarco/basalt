using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace Basalt.Shell.Controls;

/// <summary>
/// The line commands of the code editor, kept out of the editor's own file so
/// that it does not grow with each one.
/// </summary>
internal static class CodeEditorLineCommands
{
    /// <summary>
    /// Edit &gt; Duplicate Line, as Visual Studio has it.
    ///
    /// With a selection, the selected text is copied straight after itself and
    /// the caret goes to the end of the copy. With nothing selected, the line
    /// the caret is on is copied below itself, and the caret keeps its column
    /// on the copy.
    /// </summary>
    /// <remarks>
    /// The copy is one undo step, so a single Ctrl+Z takes it back. AvaloniaEdit
    /// binds Ctrl+D to deleting the line, which is why the command must be
    /// claimed by the shortcut dispatcher before the editor sees the key.
    /// </remarks>
    public static void DuplicateLine(TextEditor editor)
    {
        var undo = editor.Document.UndoStack;

        undo.StartUndoGroup();
        try
        {
            if (editor.SelectionLength > 0) DuplicateSelection(editor);
            else DuplicateCaretLine(editor);
        }
        finally
        {
            undo.EndUndoGroup();
        }
    }

    private static void DuplicateSelection(TextEditor editor)
    {
        var document = editor.Document;
        var start = editor.SelectionStart;
        var length = editor.SelectionLength;
        var end = start + length;

        document.Insert(end, document.GetText(start, length));

        var caret = end + length;
        editor.Select(caret, 0);
        editor.CaretOffset = caret;
    }

    private static void DuplicateCaretLine(TextEditor editor)
    {
        var document = editor.Document;
        var line = document.GetLineByOffset(editor.CaretOffset);
        var column = editor.CaretOffset - line.Offset;
        var lineEnd = line.Offset + line.Length;
        var newline = LineBreakOf(document, line);

        document.Insert(lineEnd, newline + document.GetText(line.Offset, line.Length));

        var caret = lineEnd + newline.Length + column;
        editor.Select(caret, 0);
        editor.CaretOffset = caret;
    }

    /// <summary>
    /// The break the file uses, so the copy's break matches the rest. The last
    /// line has none of its own, so the one before it is asked.
    /// </summary>
    private static string LineBreakOf(TextDocument document, DocumentLine line)
    {
        if (line.DelimiterLength > 0)
            return document.GetText(line.Offset + line.Length, line.DelimiterLength);

        if (line.PreviousLine is { DelimiterLength: > 0 } previous)
            return document.GetText(previous.Offset + previous.Length, previous.DelimiterLength);

        return "\n";
    }
}
