using AvaloniaEdit;
using AvaloniaEdit.Document;
using Basalt.Core.Model;
using Basalt.Shell.ViewModels;
using Microsoft.CodeAnalysis.Text;

namespace Basalt.Shell.Controls;

/// <summary>Accepts Enter immediately and applies Roslyn's corrections only to that input.</summary>
internal static class VisualBasicEnterInput
{
    private sealed record Plan(string Text, int Position, string Insertion, int Caret);

    public static async Task HandleAsync(TextEditor editor, MainWindowViewModel shell,
        string filePath, Action<Action> edit)
    {
        var document = editor.Document;
        var undoGroup = new object();
        var source = "";
        var position = 0;
        var provisional = "";
        var newline = "\n";
        var indentationSize = editor.Options.IndentationSize;

        edit(() =>
        {
            document.UndoStack.StartUndoGroup(undoGroup);
            try
            {
                using (document.RunUpdate())
                {
                    editor.TextArea.Selection.ReplaceSelectionWithText("");
                    source = document.Text;
                    position = editor.CaretOffset;
                    var line = document.GetLineByOffset(position);
                    if (line.DelimiterLength > 0)
                        newline = document.GetText(line.EndOffset, line.DelimiterLength);
                    else if (source.Contains("\r\n", StringComparison.Ordinal)) newline = "\r\n";
                    var beforeCaret = document.GetText(line.Offset, position - line.Offset);
                    var indentation = new string(beforeCaret.TakeWhile(character => character is ' ' or '\t').ToArray());
                    provisional = newline + indentation;
                    document.Insert(position, provisional);
                    editor.CaretOffset = position + provisional.Length;
                }
            }
            finally { document.UndoStack.EndUndoGroup(); }
        });

        var version = document.Version;
        var caret = editor.CaretOffset;
        // Complete key routing before any cached language-service answer can
        // edit the document. Further input already has its own new line.
        await Task.Yield();
        var plan = await Task.Run(async () =>
        {
            var formatted = await shell.ApplyTypingConventionsAsync(source, SourceLanguage.VisualBasic, position)
                .ConfigureAwait(false);
            var originalLine = SourceText.From(source).Lines.GetLineFromPosition(position).LineNumber;
            var corrected = await shell.CorrectIdentifierCasingAsync(filePath, formatted.Text, originalLine)
                .ConfigureAwait(false);
            var correctedSource = SourceText.From(corrected);
            var correctedPosition = Math.Clamp(formatted.Caret, 0, corrected.Length);
            var line = correctedSource.Lines.GetLineFromPosition(correctedPosition);
            var indentation = await shell.GetIndentationAsync(corrected, SourceLanguage.VisualBasic, line.End)
                .ConfigureAwait(false);
            var closing = await shell.GetBlockClosingAsync(corrected, SourceLanguage.VisualBasic, line.LineNumber)
                .ConfigureAwait(false);
            var insertion = newline + new string(' ', indentation);
            var finalCaret = correctedPosition + insertion.Length;
            if (closing is not null)
                insertion += newline + new string(' ', Math.Max(0, indentation - indentationSize)) + closing;
            return new Plan(corrected, correctedPosition, insertion, finalCaret);
        });

        // Text, caret and undo history all belong to the initiating Enter.
        // A late answer must never follow the user into another statement.
        if (!ReferenceEquals(editor.Document, document) || document.Version != version ||
            editor.CaretOffset != caret || editor.SelectionLength != 0 ||
            !ReferenceEquals(document.UndoStack.LastGroupDescriptor, undoGroup)) return;
        if (new TextDocument(source).LineCount != new TextDocument(plan.Text).LineCount) return;

        edit(() =>
        {
            document.UndoStack.StartContinuedUndoGroup(undoGroup);
            try
            {
                using (document.RunUpdate())
                {
                    document.Remove(position, provisional.Length);
                    EditorTypingChanges.Apply(editor, source, plan.Text, plan.Position);
                    document.Insert(plan.Position, plan.Insertion);
                    editor.CaretOffset = plan.Caret;
                }
            }
            finally { document.UndoStack.EndUndoGroup(); }
        });
    }
}
