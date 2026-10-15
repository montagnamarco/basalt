using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Basalt.Workspace.Completion;

namespace Basalt.Shell.Controls;

/// <summary>Resolves completion keys before the editor handles indentation and newlines.</summary>
/// <remarks>
/// A key that ends a word takes the highlighted entry only when that entry
/// matches what was typed, as Visual Studio does. "(", "." and "," take an
/// entry that begins with what was typed; a space takes only the name written,
/// the one the user chose, or the only one the typed text can mean (see
/// <see cref="CompletionMatcher.MayCommitOnPunctuation"/>). Nothing is taken in
/// suggestion mode unless the name is written in full. Otherwise the list
/// closes and the key is typed as it is: "Strq" followed by a space stays
/// "Strq ", and "st" followed by a space stays "st " rather than becoming
/// "Static".
/// </remarks>
internal static class VisualBasicCompletionInput
{
    /// <summary>The characters that end a name and may take the highlighted entry. The colon stays: the existing tests pin it.</summary>
    private const string CommitCharacters = " ),=&(.:";

    /// <param name="chosen">Whether the user moved the highlight to the entry with the arrow keys.</param>
    public static void HandleText(CompletionWindow? window, TextInputEventArgs args, bool suggestionMode, bool chosen)
    {
        if (suggestionMode) return;
        if (window is null || args.Text is not { Length: 1 } text) return;

        // A letter or a digit is a name going on, and the list narrows as it is typed.
        if (IsWordCharacter(text[0])) return;

        if (window.CompletionList.SelectedItem is EditorCompletionData selected &&
            CommitCharacters.Contains(text[0]) &&
            CompletionMatcher.MayCommitOnPunctuation(
                selected.Item, TypedSince(window), window.CompletionList.CompletionData.Count, chosen, text[0]))
        {
            // Commit using the character produced by the keyboard layout. Normal
            // text input then inserts it, including the editor's bracket pairing.
            window.CompletionList.RequestInsertion(args);
            return;
        }

        // Anything else ends the list without taking an entry, and the character
        // is typed as it is.
        window.Close();
    }

    /// <param name="chosen">Whether the user moved the highlight to the entry with the arrow keys.</param>
    public static bool HandleKey(TextEditor editor, CompletionWindow? window, KeyEventArgs args, bool chosen)
    {
        if (window is null || args.KeyModifiers != KeyModifiers.None) return false;

        if (args.Key == Key.Escape)
        {
            window.Close();
            args.Handled = true;
            return true;
        }

        if (args.Key is not (Key.Enter or Key.Tab)) return false;

        var selected = window.CompletionList.SelectedItem as EditorCompletionData;

        var start = Math.Clamp(window.StartOffset, 0, editor.Document.TextLength);
        var end = Math.Clamp(editor.CaretOffset, start, editor.Document.TextLength);
        var written = editor.Document.GetText(start, end - start);

        // With nothing typed, Enter and Tab take only an entry the language
        // service preselected or the user moved the highlight to. An entry the
        // list picked for itself is not a choice: the key closes the list and
        // acts as it does without one, so Enter after a Backspace that emptied
        // the word still writes a new line.
        var mayTake = selected is not null && CompletionMatcher.MayCommit(selected.Item, written, chosen);

        if (selected is null || !mayTake)
        {
            window.Close();
            return false;
        }

        var continueWithNewLine = args.Key == Key.Enter &&
            string.Equals(written, selected.Text, StringComparison.OrdinalIgnoreCase);

        window.CompletionList.RequestInsertion(args);

        // Once the whole word was already present, Enter also finishes the
        // line. The editor's normal VB handler supplies formatting and indent.
        args.Handled = !continueWithNewLine;
        return args.Handled;
    }

    /// <summary>The name written since the list opened, up to the caret.</summary>
    private static string TypedSince(CompletionWindow window)
    {
        var document = window.TextArea.Document;
        var caret = Math.Clamp(window.TextArea.Caret.Offset, 0, document.TextLength);
        var start = Math.Clamp(window.StartOffset, 0, caret);

        return document.GetText(start, caret - start);
    }

    private static bool IsWordCharacter(char character) =>
        char.IsLetterOrDigit(character) || character == '_';
}
