using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace Basalt.Shell.Controls;

/// <summary>Applies typing corrections to their individual spans in one undo group.</summary>
internal static class EditorTypingChanges
{
    public static bool Apply(TextEditor editor, string snapshot, string updated, int? caretAfter = null)
    {
        if (!string.Equals(editor.Text, snapshot, StringComparison.Ordinal)) return false;
        var original = editor.Document;
        var replacement = new TextDocument(updated);
        if (original.LineCount != replacement.LineCount) return false;

        var edits = new List<(int Offset, int Length, string Text)>();
        foreach (var line in original.Lines)
        {
            var next = replacement.GetLineByNumber(line.LineNumber);
            // Typing conventions edit tokens and spacing, never line breaks.
            // Reject a different shape instead of applying only half a result.
            if (original.GetText(line.EndOffset, line.DelimiterLength) !=
                replacement.GetText(next.EndOffset, next.DelimiterLength)) return false;
            var before = original.GetText(line.Offset, line.Length);
            var after = replacement.GetText(next.Offset, next.Length);
            if (before == after) continue;

            var prefix = 0;
            while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
            var suffix = 0;
            while (suffix < before.Length - prefix && suffix < after.Length - prefix &&
                before[before.Length - suffix - 1] == after[after.Length - suffix - 1]) suffix++;
            edits.Add((line.Offset + prefix, before.Length - prefix - suffix,
                after.Substring(prefix, after.Length - prefix - suffix)));
        }
        if (edits.Count == 0) return false;

        var caret = editor.CaretOffset;
        using (original.RunUpdate())
        {
            // Descending offsets leave every earlier span valid and allow the
            // caret to follow edits even if it moved while Roslyn was working.
            foreach (var edit in edits.AsEnumerable().Reverse())
            {
                original.Replace(edit.Offset, edit.Length, edit.Text);
                if (caret >= edit.Offset + edit.Length) caret += edit.Text.Length - edit.Length;
                else if (caret > edit.Offset) caret = edit.Offset + edit.Text.Length;
            }
            editor.CaretOffset = Math.Clamp(caretAfter ?? caret, 0, original.TextLength);
        }
        return true;
    }
}
