using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Snippets;
using Basalt.Workspace.Snippets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using TextDocument = AvaloniaEdit.Document.TextDocument;

namespace Basalt.Shell.Controls;

/// <summary>Expands a standalone shortcut on Tab, Tab using native snippet fields.</summary>
internal sealed class VisualBasicSnippetInput(TextEditor editor, Action<Action> edit)
{
    private (ITextSourceVersion Version, int Caret)? _armed;
    private int _request;
    private InsertionContext? _context;
    public bool IsActive { get; private set; }

    public void CancelPending()
    {
        _armed = null;
        _request++;
    }

    public bool HandleFieldKey(KeyEventArgs key)
    {
        if (!IsActive || _context is not { } context || key.KeyModifiers != KeyModifiers.None) return false;
        var last = context.ActiveElements.LastOrDefault(element => element.IsEditable && element.Segment is not null);
        var finishingTab = key.Key == Key.Tab && last?.Segment is { } segment &&
            editor.CaretOffset >= segment.Offset && editor.CaretOffset <= segment.EndOffset;
        if (!finishingTab && key.Key is not (Key.Enter or Key.Escape)) return false;

        // AvalonEdit normally wraps Tab back to the first field. At the last
        // field, finish at $end$ instead; Escape keeps the current position.
        context.Deactivate(new SnippetEventArgs(key.Key == Key.Escape
            ? DeactivateReason.EscapePressed : DeactivateReason.ReturnPressed));
        editor.Select(editor.CaretOffset, 0);
        return true;
    }

    public Task? HandleKey(KeyEventArgs key)
    {
        var request = ++_request;
        if (IsActive || key.Key != Key.Tab || key.KeyModifiers != KeyModifiers.None || editor.SelectionLength != 0)
        {
            _armed = null;
            return null;
        }

        var document = editor.Document;
        var caret = editor.CaretOffset;
        var line = document.GetLineByOffset(caret);
        var before = document.GetText(line.Offset, caret - line.Offset);
        var shortcut = before.TrimStart(' ', '\t');
        var snippet = VbSnippets.ByShortcut(shortcut);
        if (snippet is null || !string.IsNullOrWhiteSpace(document.GetText(caret, line.EndOffset - caret)))
        {
            _armed = null;
            return null;
        }

        var version = document.Version;
        if (snippet.Shortcut != "?" &&
            (_armed is not { } armed || armed.Version != version || armed.Caret != caret))
        {
            _armed = (version, caret);
            return Task.CompletedTask;
        }
        _armed = null;
        return ExpandAsync(document, version, caret, caret - shortcut.Length, snippet, request);
    }

    private async Task ExpandAsync(TextDocument document, ITextSourceVersion version,
        int caret, int start, CodeSnippet snippet, int request)
    {
        var text = document.Text;
        var valid = await Task.Run(async () =>
        {
            var root = await VisualBasicSyntaxTree.ParseText(text).GetRootAsync().ConfigureAwait(false);
            var token = root.FindToken(start, findInsideTrivia: true);
            // A matching word inside XML text, a string, or comment trivia is
            // not a VB shortcut. Ask the same parser used for editing services.
            return token.Span.Start == start && token.Span.End == caret &&
                (token.RawKind == (int)SyntaxKind.IdentifierToken || SyntaxFacts.IsKeywordKind(token.Kind()) ||
                 snippet.Shortcut == "?" && token.IsKind(SyntaxKind.QuestionToken));
        });
        if (!valid || request != _request || editor.Document != document || document.Version != version ||
            editor.CaretOffset != caret || editor.SelectionLength != 0) return;

        edit(() =>
        {
            using (document.RunUpdate())
            {
                editor.Select(start, caret - start);
                IsActive = true;
                try
                {
                    _context = Build(snippet).Insert(editor.TextArea);
                    _context.Deactivated += (_, _) =>
                    {
                        IsActive = false;
                        _context = null;
                    };
                    IsActive = _context.ActiveElements.Any(element => element.IsEditable);
                    if (!IsActive) _context = null;
                }
                catch
                {
                    IsActive = false;
                    throw;
                }
            }
        });
    }

    private static Snippet Build(CodeSnippet source)
    {
        // Native insertion supplies the document's newline and indentation.
        var expanded = SnippetExpander.Expand(source);
        var snippet = new Snippet();
        var at = 0;
        var caretWritten = false;
        foreach (var stop in expanded.Stops)
        {
            AppendText(stop.Start);
            snippet.Elements.Add(new SnippetReplaceableTextElement { Text = stop.Placeholder });
            at = stop.Start + stop.Length;
        }
        AppendText(expanded.Text.Length);
        return snippet;

        void AppendText(int end)
        {
            if (!caretWritten && expanded.Caret >= at && expanded.Caret <= end)
            {
                snippet.Elements.Add(new SnippetTextElement { Text = expanded.Text[at..expanded.Caret] });
                snippet.Elements.Add(new SnippetCaretElement());
                at = expanded.Caret;
                caretWritten = true;
            }
            snippet.Elements.Add(new SnippetTextElement { Text = expanded.Text[at..end] });
            at = end;
        }
    }
}
