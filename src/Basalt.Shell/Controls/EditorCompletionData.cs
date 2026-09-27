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

    public EditorCompletionData(CompletionItem item) => _item = item;

    /// <summary>The entry as the language service gave it.</summary>
    public CompletionItem Item => _item;

    public IImage? Image => CompletionIcons.For(_item.Kind);
    public string Text => _item.InsertionText;
    public object Content => _item.DisplayText;
    public object Description => _item.Description ?? _item.Kind.ToString();
    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
        textArea.Document.Replace(completionSegment, Text);
}
