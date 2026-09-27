using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace Basalt.Shell.Controls;

/// <summary>Identifies the latest language-service request for an unchanged editor position.</summary>
internal sealed class EditorRequestGate
{
    private Snapshot? _latest;

    public void Invalidate() => _latest = null;

    public Snapshot Begin(TextEditor editor)
    {
        var document = editor.Document;
        _latest = new Snapshot(document, document.Version, editor.CaretOffset, document.Text);
        return _latest;
    }

    public bool IsCurrent(TextEditor editor, Snapshot request) =>
        ReferenceEquals(request, _latest) &&
        ReferenceEquals(request.Document, editor.Document) &&
        request.Version == editor.Document.Version &&
        request.Position == editor.CaretOffset;

    internal sealed record Snapshot(
        TextDocument Document, ITextSourceVersion Version, int Position, string Text);
}
