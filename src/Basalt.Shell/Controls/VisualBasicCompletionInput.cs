using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;

namespace Basalt.Shell.Controls;

/// <summary>Resolves completion keys before the editor handles indentation and newlines.</summary>
internal static class VisualBasicCompletionInput
{
    public static void HandleText(CompletionWindow? window, TextInputEventArgs args, bool suggestionMode)
    {
        if (suggestionMode) return;
        if (window?.CompletionList.SelectedItem is null || args.Text is not { Length: 1 } text) return;
        if (text[0] is not (' ' or '.' or '(' or ')' or ',' or '=' or ':')) return;

        // Commit using the character produced by the keyboard layout. Normal
        // text input then inserts it, including the editor's bracket pairing.
        window.CompletionList.RequestInsertion(args);
    }

    public static bool HandleKey(TextEditor editor, CompletionWindow? window, KeyEventArgs args)
    {
        if (window is null || args.KeyModifiers != KeyModifiers.None) return false;

        if (args.Key == Key.Escape)
        {
            window.Close();
            args.Handled = true;
            return true;
        }

        if (args.Key is not (Key.Enter or Key.Tab)) return false;

        var selected = window.CompletionList.SelectedItem;
        if (selected is null)
        {
            window.Close();
            return false;
        }

        var start = Math.Clamp(window.StartOffset, 0, editor.Document.TextLength);
        var end = Math.Clamp(editor.CaretOffset, start, editor.Document.TextLength);
        var written = editor.Document.GetText(start, end - start);
        var continueWithNewLine = args.Key == Key.Enter &&
            string.Equals(written, selected.Text, StringComparison.OrdinalIgnoreCase);

        window.CompletionList.RequestInsertion(args);

        // Once the whole word was already present, Enter also finishes the
        // line. The editor's normal VB handler supplies formatting and indent.
        args.Handled = !continueWithNewLine;
        return args.Handled;
    }
}
