using AvaloniaEdit;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

/// <summary>Applies generated documentation only to the editor snapshot that requested it.</summary>
internal static class VisualBasicDocumentationInput
{
    public static async Task<bool> TryGenerateAsync(TextEditor editor)
    {
        if (!editor.TextArea.Selection.IsEmpty) return false;

        var document = editor.Document;
        var version = document.Version;
        var caret = editor.CaretOffset;
        var source = document.Text;

        // Finish routing the text-input event before applying another edit,
        // even when a short document can be parsed immediately.
        await Task.Yield();

        var insertion = await VisualBasicDocumentationCommentService.GenerateAsync(source, caret)
            .ConfigureAwait(true);

        // Parsing runs in the background. A subsequent keystroke, navigation
        // or document replacement makes its insertion position obsolete.
        if (insertion is null || !ReferenceEquals(document, editor.Document) ||
            document.Version != version || editor.CaretOffset != caret ||
            !editor.TextArea.Selection.IsEmpty) return false;

        document.Insert(caret, insertion.Text);
        editor.CaretOffset = caret + insertion.CaretOffset;
        return true;
    }
}
