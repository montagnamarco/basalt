using System.Runtime.CompilerServices;
using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Basalt.Core.Localization;
using Basalt.Extensibility;
using Basalt.Workspace.Ai;

namespace Basalt.Shell.Controls;

/// <summary>The limits of inline suggestions; the defaults are what the IDE runs with.</summary>
internal sealed class InlineSuggestionOptions
{
    /// <summary>
    /// How long typing has to pause. Longer than completion's: a suggestion
    /// is a paid round trip, and is wanted when someone stops to think.
    /// </summary>
    public TimeSpan Pause { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// The least time between two requests, however much is typed: a ceiling
    /// on cost for someone who types in bursts.
    /// </summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>After an error, how long nothing is asked: a revoked key must not be tried on every pause.</summary>
    public TimeSpan ErrorBackoff { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Files longer than this are not sent: they are generated or data, not code being written.</summary>
    public int MaxFileChars { get; set; } = 200_000;
}

/// <summary>
/// Asks for a suggestion when typing pauses, and shows or applies the answer.
/// </summary>
/// <remarks>
/// One per editor, and it owns the whole life of a suggestion: the pause, the
/// guards that keep requests rare, the stale check, the ghost on screen and
/// what Tab, Ctrl+Right and Esc do to it. The editor only builds it, tells it
/// when something was typed, and asks it first about a key.
///
/// It watches the document and the caret itself, so no part of the editor has
/// to remember to cancel it: anything that edits or moves cancels the request
/// in flight and takes the suggestion away, because an answer to the text as
/// it was is worse than none.
/// </remarks>
internal sealed class InlineSuggestionController : IDisposable
{
    /// <summary>
    /// What every editor sharing one provider has to agree on: after the
    /// provider fails, none of them asks again for a while, and the failure
    /// is told once, not once per open file.
    /// </summary>
    private sealed class ProviderState
    {
        public long QuietUntil;
        public string? LastError;
    }

    private static readonly ConditionalWeakTable<IInlineSuggestionProvider, ProviderState> States = new();

    private readonly TextEditor _editor;
    private readonly GhostTextGenerator _ghost;
    private readonly Func<IInlineSuggestionProvider?> _provider;
    private readonly Func<string> _filePath;
    private readonly Func<bool> _isBlocked;
    private readonly Func<bool> _isActive;
    private readonly Func<bool> _isSuppressed;
    private readonly Func<Avalonia.Media.IBrush?> _brush;
    private readonly Action<string> _reportError;
    private readonly Action<Action> _applyEdit;

    private CancellationTokenSource? _pending;
    private Task _inFlight = Task.CompletedTask;
    private InlineSuggestion? _current;
    private long _edits;
    private int _caretAfterEdit = -1;
    private bool _accepting;
    private long _nextRequestAt;

    public InlineSuggestionController(
        TextEditor editor,
        GhostTextGenerator ghost,
        Func<IInlineSuggestionProvider?> provider,
        Func<string> filePath,
        Func<bool> isBlocked,
        Func<bool> isActive,
        Func<bool> isSuppressed,
        Func<Avalonia.Media.IBrush?> brush,
        Action<string> reportError,
        Action<Action>? applyEdit = null)
    {
        _editor = editor;
        _ghost = ghost;
        _provider = provider;
        _filePath = filePath;
        _isBlocked = isBlocked;
        _isActive = isActive;
        _isSuppressed = isSuppressed;
        _brush = brush;
        _reportError = reportError;
        _applyEdit = applyEdit ?? (edit => edit());

        _editor.Document.Changed += OnDocumentChanged;
        _editor.TextArea.Caret.PositionChanged += OnCaretChanged;
    }

    public InlineSuggestionOptions Options { get; } = new();

    /// <summary>How many requests were made, for the tests.</summary>
    public int RequestsMade { get; private set; }

    /// <summary>Whether a suggestion is on screen.</summary>
    public bool IsShowing => _current is not null;

    /// <summary>Whether a request is waiting its turn or in flight, for the tests.</summary>
    public bool IsPending => _pending is not null;

    public string? CurrentText => _current?.Text;

    private static long Now => Environment.TickCount64;

    // Watching the editor

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        _edits++;

        // Where the caret ends up when this edit is typing: just after what
        // was written. The caret reports itself more than once per keystroke,
        // so "it moved after an edit" cannot tell typing from navigation; only
        // where it moved to can.
        _caretAfterEdit = e.Offset + e.InsertionLength;

        Cancel();

        // The editor rewriting itself, or this controller accepting: neither
        // is someone typing, and neither earns a request.
        if (_accepting || _isSuppressed()) return;

        Schedule();
    }

    private void OnCaretChanged(object? sender, EventArgs e)
    {
        // Typing moves the caret to just after what was written: that is part
        // of typing, and the request just scheduled stands. A move anywhere
        // else is someone navigating, and no longer wants what was asked for.
        if (_accepting || _editor.CaretOffset == _caretAfterEdit) return;

        Cancel();
    }

    /// <summary>
    /// The editor's own typing finished: a character, a pair of brackets or
    /// quotes written or stepped over, a line started with Enter.
    /// </summary>
    /// <remarks>
    /// The editor moves the caret itself after some of these (inside the new
    /// pair, past the closer it overtyped) and does some of them with its
    /// change handling suppressed, so the document and the caret alone cannot
    /// tell they were typing. This says so, and asks again for the line as it
    /// now stands.
    /// </remarks>
    public void Typed()
    {
        if (_accepting) return;

        Cancel();
        Schedule();
    }

    /// <summary>
    /// Cancels the request being waited for or in flight and takes the
    /// suggestion away.
    /// </summary>
    public void Cancel()
    {
        var pending = _pending;
        _pending = null;

        // Disposed by the run once it has seen the cancellation, not here: a
        // token source disposed under its own awaiter throws.
        pending?.Cancel();

        Clear();
    }

    private void Clear()
    {
        if (_current is null && !_ghost.IsShowing) return;

        _current = null;
        _ghost.Clear();
        _editor.TextArea.TextView.Redraw();
    }

    // Asking

    private void Schedule()
    {
        var cancellation = new CancellationTokenSource();
        _pending = cancellation;

        Guarded.Run(() => RunAsync(cancellation), _reportError, "inline");
    }

    private ProviderState StateOf(IInlineSuggestionProvider provider) => States.GetOrCreateValue(provider);

    private async Task RunAsync(CancellationTokenSource cancellation)
    {
        var ct = cancellation.Token;

        try
        {
            await Task.Delay(Options.Pause, ct).ConfigureAwait(true);

            // One request in flight: the one a keystroke cancelled may still
            // be unwinding, and its connection is not free until it has.
            try { await _inFlight.ConfigureAwait(true); }
            catch (Exception) { /* its own run reported it */ }

            // Never more often than the interval allows; waited for rather
            // than dropped, so the last pause of a burst is still answered.
            var wait = _nextRequestAt - Now;

            if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait), ct).ConfigureAwait(true);

            ct.ThrowIfCancellationRequested();

            if (!TrySnapshot(out var provider, out var text, out var caret, out var path)) return;

            var state = StateOf(provider);

            if (Now < state.QuietUntil) return;

            var version = _edits;

            _nextRequestAt = Now + (long)Options.MinInterval.TotalMilliseconds;
            RequestsMade++;

            var request = provider.SuggestAsync(path, text, caret, ct);
            _inFlight = request;

            InlineSuggestion? suggestion;

            try
            {
                suggestion = await request.ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Once, and then quiet: an error on every pause would be a
                // status bar that never stops flashing. A different error
                // is news; the same one is not. Shared by every editor of
                // the provider, which is the one that failed.
                state.QuietUntil = Now + (long)Options.ErrorBackoff.TotalMilliseconds;

                if (ex.Message != state.LastError)
                {
                    state.LastError = ex.Message;
                    _reportError(ex.Message);
                }

                return;
            }

            state.LastError = null;

            // The answer is for the text and caret it was asked about. Shown
            // anywhere else it reads as an answer to something else.
            if (ct.IsCancellationRequested
                || version != _edits
                || suggestion is null
                || suggestion.Position != caret
                || _editor.CaretOffset != caret
                || _isBlocked()
                || !_isActive()) return;

            Show(suggestion);
        }
        catch (OperationCanceledException)
        {
            // Typing went on: the next pause asks again.
        }
        finally
        {
            if (ReferenceEquals(_pending, cancellation)) _pending = null;

            cancellation.Dispose();
        }
    }

    /// <summary>
    /// Whether a request is wanted now, and with what. Every guard that keeps
    /// requests rare or private is here, on the interface thread, before
    /// anything is sent. (A file reached through a link is checked again by
    /// the provider, off this thread, where the link can be followed.)
    /// </summary>
    private bool TrySnapshot(
        out IInlineSuggestionProvider provider, out string text, out int caret, out string path)
    {
        provider = null!;
        text = "";
        caret = 0;
        path = _filePath();

        if (_provider() is not { IsAvailable: true } available) return false;

        provider = available;

        if (!_isActive() || _isBlocked()) return false;
        if (_editor.SelectionLength > 0) return false;
        if (_editor.Document.TextLength > Options.MaxFileChars) return false;
        if (IsSecretFile(path)) return false;

        caret = _editor.CaretOffset;

        if (!OnlyClosersAfter(caret)) return false;

        text = _editor.Text;

        return text.Trim().Length > 0;
    }

    /// <summary>
    /// A file that may hold credentials by its name: its contents never leave
    /// the machine, by the same rules that keep them from the chat's tools.
    /// </summary>
    internal static bool IsSecretFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            return AiWorkspaceSandbox.IsSecret(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A path that cannot be read cannot be shown to be safe.
            return true;
        }
    }

    /// <summary>
    /// Whether the rest of the line is empty, blank or only closing tokens:
    /// the caret is at the end of what is being written. In the middle of an
    /// expression a suggestion would have to rewrite the line, and none is
    /// asked for.
    /// </summary>
    private bool OnlyClosersAfter(int caret)
    {
        var line = _editor.Document.GetLineByOffset(caret);

        for (var i = caret; i < line.EndOffset; i++)
        {
            var c = _editor.Document.GetCharAt(i);

            if (!(char.IsWhiteSpace(c) || ")]}\">".Contains(c))) return false;
        }

        return true;
    }

    // Showing

    private void Show(InlineSuggestion suggestion)
    {
        // One newline in memory; the document's own is used when it is written.
        var text = suggestion.Text.Replace("\r\n", "\n");
        var lines = text.Split('\n');

        _current = new InlineSuggestion(text, suggestion.Position);
        _ghost.Foreground = _brush();

        // The first line, where the caret is, and a note of how much more
        // there is. The rest is never drawn: it would have to go over the code
        // below, and what is already written is not the suggestion's to hide.
        var more = lines.Length - 1;

        var shown = lines[0] + (more switch
        {
            0 => "",
            1 => Localizer.Get(StringKeys.InlineMoreLine),
            _ => Localizer.Get(StringKeys.InlineMoreLines, more)
        });

        if (shown.Length > 0) _ghost.Show(shown, suggestion.Position);

        _editor.TextArea.TextView.Redraw();
    }

    // Keys

    /// <summary>
    /// Tab accepts, Ctrl+Right takes a word, Esc dismisses; only while a
    /// suggestion is showing, so Tab stays indentation the rest of the time.
    /// </summary>
    /// <remarks>
    /// Esc also stops a request that has not answered yet, and leaves the key
    /// to whoever else wants it: with nothing on screen it is not ours.
    /// </remarks>
    public bool TryHandleKey(KeyEventArgs e)
    {
        if (_current is null)
        {
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && _pending is not null)
                Cancel();

            return false;
        }

        // A selection has its own use of Tab (indenting it) and the
        // suggestion was made for a caret, not for a selection.
        if (_isBlocked() || _editor.SelectionLength > 0)
        {
            Cancel();
            return false;
        }

        if (e.KeyModifiers == KeyModifiers.None)
        {
            if (e.Key == Key.Tab) return e.Handled = Accept();

            if (e.Key == Key.Escape)
            {
                Cancel();
                return e.Handled = true;
            }
        }

        // Alt as well as Ctrl: Ctrl+Right is taken by the desktop switcher on macOS.
        if (e.Key == Key.Right && (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Alt))
            return e.Handled = Accept(wordOnly: true);

        return false;
    }

    /// <summary>
    /// Writes the suggestion, or its next word, into the document.
    /// </summary>
    /// <remarks>
    /// One undo step however many lines: one Ctrl+Z removes what one Tab put
    /// there. It goes through the editor's typing edit, so leaving the line
    /// the caret was on does not format it as a separate step afterwards, and
    /// it is written with the document's own line ending, never a mix.
    /// After a word the rest is shown again where the caret now is.
    /// </remarks>
    public bool Accept(bool wordOnly = false)
    {
        if (_current is not { } suggestion) return false;

        if (_editor.SelectionLength > 0) return false;

        var text = wordOnly ? NextWord(suggestion.Text) : suggestion.Text;

        if (text.Length == 0) return false;

        var at = Math.Clamp(suggestion.Position, 0, _editor.Document.TextLength);
        var rest = suggestion.Text[text.Length..];
        var written = text.Replace("\n", NewLineOf(_editor.Document));

        Cancel();

        _accepting = true;

        try
        {
            _applyEdit(() =>
            {
                var undo = _editor.Document.UndoStack;

                undo.StartUndoGroup();

                try
                {
                    _editor.Document.Insert(at, written);
                    _editor.CaretOffset = at + written.Length;
                }
                finally
                {
                    undo.EndUndoGroup();
                }
            });
        }
        finally
        {
            _accepting = false;
        }

        if (wordOnly && rest.Trim().Length > 0) Show(new InlineSuggestion(rest, at + written.Length));

        return true;
    }

    /// <summary>
    /// The document's line ending, found the way the Enter key finds it: the
    /// first line that ends with one. A document with none is a single line,
    /// and gets "\n".
    /// </summary>
    internal static string NewLineOf(TextDocument document)
    {
        foreach (var line in document.Lines)
        {
            if (line.DelimiterLength > 0) return document.GetText(line.EndOffset, line.DelimiterLength);
        }

        return "\n";
    }

    /// <summary>
    /// The first word, with the space before it: a suggestion that starts with
    /// a space would otherwise accept as nothing and look broken.
    /// </summary>
    internal static string NextWord(string text)
    {
        var at = 0;

        while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
        while (at < text.Length && !char.IsWhiteSpace(text[at])) at++;

        return text[..at];
    }

    public void Dispose()
    {
        Cancel();

        _editor.Document.Changed -= OnDocumentChanged;
        _editor.TextArea.Caret.PositionChanged -= OnCaretChanged;
    }
}
