using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Basalt.Core.Model;

namespace Basalt.Shell.Controls;

/// <summary>Adapts a language-service completion entry to the editor list.</summary>
internal sealed class EditorCompletionData : ICompletionData
{
    private readonly CompletionItem _item;

    private readonly Action<EditorCompletionData, int, string>? _committed;

    /// <param name="committed">
    /// Told where the entry was written and what, so the editor can replace it
    /// with what the language service says committing it really writes.
    /// </param>
    public EditorCompletionData(CompletionItem item, Action<EditorCompletionData, int, string>? committed = null)
    {
        _item = item;
        _committed = committed;
    }

    /// <summary>The entry as the language service gave it.</summary>
    public CompletionItem Item => _item;

    public IImage? Image => CompletionIcons.For(_item.Kind);
    public string Text => _item.InsertionText;
    public object Content => _item.DisplayText;
    public object Description => _item.Description ?? _item.Kind.ToString();
    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        var start = completionSegment.Offset;

        textArea.Document.Replace(completionSegment, Text);

        _committed?.Invoke(this, start, Text);
    }
}
