using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using System.ComponentModel;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Editing;
using Basalt.Core.Model;
using Basalt.Core.Settings;
using Basalt.Shell.Syntax;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Completion;

namespace Basalt.Shell.Controls;

/// <summary>
/// Text editor with syntax highlighting and completion provided by Roslyn,
/// working for both C# and VB.NET.
/// </summary>
public sealed class CodeEditor : UserControl
{
    private readonly TextEditor _editor;
    private readonly EditorDocumentViewModel _document;
    private readonly MainWindowViewModel _shell;
    private CompletionWindow? _completionWindow;

    /// <summary>What is being suggested, drawn without entering the document.</summary>
    private readonly GhostTextGenerator _ghostText = new();

    private CancellationTokenSource? _suggestionDebounce;

    /// <summary>
    /// Who suggests what to write next, when anybody does.
    /// </summary>
    /// <remarks>
    /// Settable so a test can supply one that answers immediately: the real
    /// providers need an account and a network, and an editor whose suggestion
    /// path is only exercised against those is one nobody can test.
    /// </remarks>
    public Basalt.Extensibility.IInlineSuggestionProvider? SuggestionProvider { get; set; }

    /// <summary>
    /// Whether the completion list opens on its own while typing.
    ///
    /// On by default, as in Visual Studio: waiting for a dot means the list
    /// never appears for a bare name, which is most of what anyone types.
    /// </summary>
    private bool _suggestAutomatically = true;
    private CancellationTokenSource? _diagnosticsDebounce;

    /// <summary>Set while the editor rewrites its own text, to avoid re-entrancy.</summary>
    private bool _suppressTextChanged;

    /// <summary>Draws the wavy underlines under errors and warnings.</summary>
    private readonly DiagnosticSquiggles _squiggles;

    /// <summary>Grammar-based colouring, for the languages Roslyn does not cover.</summary>
    private TextMateHighlighting? _textMate;

    public CodeEditor(EditorDocumentViewModel document, MainWindowViewModel shell)
    {
        _document = document;
        _shell = shell;

        _editor = new TextEditor
        {
            Document = new TextDocument(document.Text),
            FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
            FontSize = 13,
            ShowLineNumbers = true,
            Options = { ConvertTabsToSpaces = true, IndentationSize = 4 }
        };

        ApplySyntaxHighlighting();

        InstallFolding();

        // The document can change under the editor: a refactoring rewrites the
        // file and tells the document, and until this existed the editor went
        // on showing the old text. The change was on disk, invisible until the
        // file was closed and reopened.
        _document.PropertyChanged += OnDocumentPropertyChanged;

        _squiggles = new DiagnosticSquiggles(_editor.Document);
        _editor.TextArea.TextView.BackgroundRenderers.Add(_squiggles);

        // Where a suggestion is drawn. Registered even with no provider
        // signed in: it draws nothing until something is suggested, and
        // adding it later would mean rebuilding the view.
        _editor.TextArea.TextView.ElementGenerators.Add(_ghostText);

        // Left of the line numbers, so a click to set a breakpoint cannot be
        // mistaken for a click to select a line.
        BreakpointMargin = new BreakpointMargin();
        _editor.TextArea.LeftMargins.Insert(0, BreakpointMargin);

        // Between the breakpoints and the line numbers, as in Visual Studio.
        GitChangeMargin = new GitChangeMargin();
        _editor.TextArea.LeftMargins.Insert(1, GitChangeMargin);

        EditorMenu = new EditorContextMenu(_editor);

        _editor.TextChanged += OnTextChanged;
        _editor.TextArea.TextEntered += OnTextEntered;

        InstallHover();

        // Leaving a line finishes it, however the user leaves: arrow keys, a
        // click elsewhere, or moving focus out of the editor entirely.
        _editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        _editor.TextArea.LostFocus += OnEditorLostFocus;
        // Tunnelling: AvaloniaEdit's own Enter handling runs on the bubbling
        // pass, so a bubbling handler here would fire after the newline was
        // already inserted and could no longer replace it.
        _editor.TextArea.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // Brackets are handled before the character is inserted, so the pair
        // can be written as a single edit the user undoes in one step.
        _editor.TextArea.AddHandler(TextInputEvent, OnTextInputForBrackets, RoutingStrategies.Tunnel);

        Content = _editor;

        // Roslyn's first formatting call costs a few hundred milliseconds while
        // it builds its caches. Paying that here keeps the first Enter as quick
        // as every later one.
        if (_document.Language is SourceLanguage.VisualBasic)
            _ = WarmUpFormattingAsync();
    }

    /// <summary>Primes the formatter so the first keystroke is not the slow one.</summary>
    private async Task WarmUpFormattingAsync()
    {
        try
        {
            await _shell
                .ApplyTypingConventionsAsync(_editor.Text, _document.Language, 0)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Warm-up is best effort: a failure here must not affect editing.
        }
    }

    /// <summary>
    /// Applies a syntax definition, chosen from the file extension.
    ///
    /// The language alone is not enough: a project holds views, stylesheets and
    /// configuration besides its source files, and those would otherwise open
    /// as plain text.
    /// </summary>
    /// <summary>
    /// Which theme "follow the system" came out as.
    ///
    /// Read from the variant Avalonia settled on rather than guessed: it is
    /// the only thing that knows what the desktop asked for.
    /// </summary>
    private AppTheme SystemTheme() =>
        ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
            ? AppTheme.Dark
            : AppTheme.Light;

    /// <summary>
    /// Colours the document.
    ///
    /// TextMate first, where a grammar exists: for Razor especially it is not
    /// a small improvement, since HTML highlighting reads the embedded code
    /// blocks as plain markup. AvaloniaEdit's own definitions cover the rest.
    /// </summary>
    private void ApplySyntaxHighlighting()
    {
        _textMate = TextMateHighlighting.InstallFor(_editor, _document.FilePath, _theme);

        if (_textMate is not null) return;

        var name = Path.GetExtension(_document.FilePath).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".vb" => "VB",

            // Razor views are markup with embedded code; HTML highlighting
            // renders the bulk of them correctly.
            ".cshtml" or ".vbhtml" or ".razor" or ".html" or ".htm" => "HTML",

            ".axaml" or ".xaml" or ".csproj" or ".vbproj"
                or ".xml" or ".props" or ".targets" or ".config" => "XML",

            ".json" => "Json",
            ".css" or ".scss" => "CSS",
            ".js" => "JavaScript",
            ".md" => "MarkDown",
            _ => null
        };

        if (name is null) return;

        _editor.SyntaxHighlighting = AvaloniaEdit.Highlighting
            .HighlightingManager.Instance.GetDefinition(name);
    }

    /// <summary>
    /// Takes the document's text when something other than typing changed it.
    /// </summary>
    private FoldingManager? _folding;

    /// <summary>The text on screen, for the tests.</summary>
    internal string TextForTests => _editor.Text;

    /// <summary>The document behind the editor, for the tests.</summary>
    internal AvaloniaEdit.Document.TextDocument DocumentForTests => _editor.Document;

    /// <summary>Whether the completion list is showing, for the tests.</summary>
    internal bool CompletionOpenForTests => _completionWindow is not null;

    /// <summary>How many times completion was asked for, for the tests.</summary>
    internal int CompletionRequestsForTests { get; private set; }

    /// <summary>How many entries the last request produced, for the tests.</summary>
    internal int LastCompletionCountForTests { get; private set; } = -1;

    internal int LastCompletionOffered => LastCompletionCountForTests;



    /// <summary>Types text as a person would, for the tests.</summary>
    /// <summary>Selects a run of text, for the tests.</summary>
    internal void SelectForTests(int start, int length) =>
        _editor.Select(start, length);

    /// <summary>Deletes what is selected, for the tests.</summary>
    internal void DeleteSelectionForTests() => _editor.TextArea.Selection.ReplaceSelectionWithText("");

    internal void TypeForTests(string text)
    {
        foreach (var character in text)
        {
            // Only the event: the text area inserts the character itself when
            // it handles it. Inserting here as well typed everything twice —
            // "Console.." — and then completion had nothing to offer, which
            // looked like the completion being broken.
            _editor.TextArea.RaiseEvent(new Avalonia.Input.TextInputEventArgs
            {
                RoutedEvent = Avalonia.Input.InputElement.TextInputEvent,
                Text = character.ToString()
            });
        }
    }

    /// <summary>The fold marks, for the tests.</summary>
    internal FoldingManager? FoldingForTests => _folding;

    /// <summary>Where the caret is, for the tests.</summary>
    internal int CaretOffsetForTests
    {
        get => _editor.CaretOffset;
        set => _editor.CaretOffset = value;
    }
    private CancellationTokenSource? _foldingDebounce;

    /// <summary>
    /// Turns folding on for a Visual Basic file.
    ///
    /// Only for Visual Basic: the strategy reads VB keywords, and offering
    /// arrows that fold nothing on a .json would be worse than none.
    /// </summary>
    private void InstallFolding()
    {
        if (_document.Language != SourceLanguage.VisualBasic) return;

        _folding = FoldingManager.Install(_editor.TextArea);

        RefreshFolding();
    }

    /// <summary>Puts the fold marks back in step with the text.</summary>
    internal void RefreshFolding()
    {
        if (_folding is null) return;

        VbFoldingStrategy.Update(_folding, _editor.Document);
    }

    /// <summary>Closes every block.</summary>
    internal void FoldAll() => SetAllFolded(true);

    /// <summary>Opens every block.</summary>
    internal void UnfoldAll() => SetAllFolded(false);

    private void SetAllFolded(bool folded)
    {
        if (_folding is null) return;

        // Walked by offset: the manager offers no list of everything it holds.
        var at = 0;

        while (_folding.GetNextFolding(at) is { } section)
        {
            section.IsFolded = folded;
            at = section.StartOffset + 1;
        }
    }

    /// <summary>Folds or unfolds the block the caret is in.</summary>
    internal void ToggleFoldAtCaret()
    {
        if (_folding is null) return;

        // The innermost one: the caret is usually inside several.
        var section = _folding.GetFoldingsContaining(_editor.CaretOffset)
            .OrderByDescending(f => f.StartOffset)
            .FirstOrDefault();

        if (section is null) return;

        section.IsFolded = !section.IsFolded;
    }

    private void OnDocumentPropertyChanged(
        object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditorDocumentViewModel.Text)) return;

        // The editor is what changed the document a moment ago: taking the
        // text back would be a loop, and would move the caret on every
        // keystroke.
        if (string.Equals(_document.Text, _editor.Text, StringComparison.Ordinal)) return;

        var caret = _editor.CaretOffset;

        // The first line on screen, so the view does not jump to the top.
        var firstVisible = _editor.TextArea.TextView.ScrollOffset.Y / _editor.TextArea.TextView.DefaultLineHeight;

        _suppressTextChanged = true;

        try
        {
            // Through the document rather than TextEditor.Text, so the undo
            // stack keeps the change and it can be undone like any other.
            _editor.Document.Replace(0, _editor.Document.TextLength, _document.Text);
        }
        finally
        {
            _suppressTextChanged = false;
        }

        // Where the reader was, as near as the new text allows: a refactoring
        // that sent the caret to the top would lose their place every time.
        _editor.CaretOffset = Math.Clamp(caret, 0, _editor.Document.TextLength);

        var line = (int)Math.Clamp(firstVisible + 1, 1, _editor.Document.LineCount);

        _editor.ScrollToLine(line);

        ShowDiagnostics();
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (_suppressTextChanged) return;

        _document.Text = _editor.Text;

        // Diagnostics are recomputed once typing pauses: doing it on every
        // keystroke would make the editor stutter on large files.
        _diagnosticsDebounce?.Cancel();
        _diagnosticsDebounce = new CancellationTokenSource();
        _ = RefreshDiagnosticsAfterPauseAsync(_diagnosticsDebounce.Token);

        // The fold marks too: reading the whole file on every keystroke would
        // make a large one stutter.
        _foldingDebounce?.Cancel();
        _foldingDebounce = new CancellationTokenSource();
        _ = RefreshFoldingAfterPauseAsync(_foldingDebounce.Token);
    }

    private async Task RefreshDiagnosticsAfterPauseAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(true);
            await _shell.RefreshDiagnosticsAsync(_document).ConfigureAwait(true);
            ShowDiagnostics();
        }
        catch (OperationCanceledException)
        {
            // The user kept typing: the next computation takes precedence.
        }
    }

    /// <summary>Line the caret was on last, used to detect leaving it.</summary>
    private int _lastCaretLine = -1;

    /// <summary>
    /// Formats the line the caret has just left.
    ///
    /// Visual Basic tidies a line when you leave it, not only when you press
    /// Enter: arrow keys and mouse clicks finish a line just as much.
    /// </summary>
    private async void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        // Raised even while the editor is rewriting itself: the status bar
        // should follow the caret wherever it has been put.
        CaretMoved?.Invoke(this, EventArgs.Empty);

        if (_suppressTextChanged) return;

        var caret = Math.Clamp(_editor.CaretOffset, 0, _editor.Document.TextLength);
        var line = _editor.Document.GetLineByOffset(caret).LineNumber;

        var previous = _lastCaretLine;
        _lastCaretLine = line;

        // Still on the same line, or this is the first position we have seen.
        if (previous < 0 || previous == line) return;

        await FormatLineOnLeavingAsync(previous);
    }

    private async void OnEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_lastCaretLine < 0) return;

        await FormatLineOnLeavingAsync(_lastCaretLine);
    }

    /// <summary>Exposed for tests: headless input never reaches the text area.</summary>
    internal Task FormatLineOnLeavingForTestsAsync(int lineNumber) =>
        FormatLineOnLeavingAsync(lineNumber);

    /// <summary>
    /// Applies the language's conventions to a line the caret is no longer on.
    ///
    /// The caret is preserved exactly: the user has already moved on, and
    /// nudging them back would be worse than leaving the line untidy.
    /// </summary>
    private async Task FormatLineOnLeavingAsync(int lineNumber)
    {
        if (!AutoFormatWhileTyping) return;
        if (_document.Language is not SourceLanguage.VisualBasic) return;
        if (lineNumber < 1 || lineNumber > _editor.Document.LineCount) return;

        var line = _editor.Document.GetLineByNumber(lineNumber);
        var content = _editor.Document.GetText(line.Offset, line.Length);
        if (content.Trim().Length == 0) return;

        var snapshot = _editor.Text;
        var caretBefore = _editor.CaretOffset;

        var result = _document.Language == SourceLanguage.VisualBasic
            ? await _shell
                .ApplyTypingConventionsAsync(snapshot, _document.Language, line.EndOffset)
                .ConfigureAwait(true)
            : await _shell
                .FormatLineAsync(snapshot, _document.Language, line.EndOffset)
                .ConfigureAwait(true);

        // Identifier casing needs the project's symbols, so it comes from the
        // language service rather than the standalone formatter.
        var corrected = _document.Language == SourceLanguage.VisualBasic
            ? await _shell
                .CorrectIdentifierCasingAsync(_document.FilePath, result.Text, lineNumber - 1)
                .ConfigureAwait(true)
            : result.Text;

        var changed = result.Changed
                      || !string.Equals(corrected, snapshot, StringComparison.Ordinal);

        if (!changed) return;

        // The user kept typing while this ran: their text wins.
        if (!string.Equals(snapshot, _editor.Text, StringComparison.Ordinal)) return;

        var updated = corrected.Split('\n');
        var index = lineNumber - 1;
        if (index >= updated.Length) return;

        var replacement = updated[index];
        if (string.Equals(content, replacement, StringComparison.Ordinal)) return;

        _suppressTextChanged = true;
        try
        {
            _editor.Document.Replace(line.Offset, line.Length, replacement);

            // Re-indenting a line shifts everything after it, so the caret is
            // moved by the same amount when it sits past the edit.
            var delta = replacement.Length - content.Length;
            var caretAfter = caretBefore > line.EndOffset ? caretBefore + delta : caretBefore;
            _editor.CaretOffset = Math.Clamp(caretAfter, 0, _editor.Document.TextLength);
        }
        finally
        {
            _suppressTextChanged = false;
        }

        _document.Text = _editor.Text;
    }

    /// <summary>Puts the fold marks back in step once typing stops.</summary>
    private async Task RefreshFoldingAfterPauseAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(400, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        RefreshFolding();
    }

    /// <summary>Hands the current file's diagnostics to the underline renderer.</summary>
    internal void ShowDiagnostics()
    {
        var mine = _shell.Diagnostics
            .Where(d => string.Equals(d.FilePath, _document.FilePath, StringComparison.Ordinal))
            .ToList();

        _squiggles.SetDiagnostics(mine);
        _editor.TextArea.TextView.InvalidateLayer(_squiggles.Layer);
    }

    /// <summary>
    /// Closes a bracket or quote as it is opened, and steps over a closing one.
    ///
    /// Runs before the character is inserted so that the pair is one edit:
    /// pressing undo once should remove both, not leave the closing half
    /// behind.
    /// </summary>
    private void OnTextInputForBrackets(object? sender, TextInputEventArgs e)
    {
        if (!AutoCloseBrackets) return;
        if (e.Text is not { Length: 1 } input) return;

        // With a selection the character replaces it, which is a different
        // operation from completing a pair.
        if (!_editor.TextArea.Selection.IsEmpty) return;

        var action = BracketCompletion.ForTypedCharacter(
            _editor.Text, _editor.CaretOffset, input[0]);

        if (action.IsNone) return;

        if (action.SkipLength > 0)
        {
            _editor.CaretOffset += action.SkipLength;
            e.Handled = true;
            return;
        }

        _editor.Document.Insert(_editor.CaretOffset, input + action.Insert);

        // Document.Insert leaves the caret after everything written; the pair
        // is meant to be typed into, so the caret steps back inside it.
        _editor.CaretOffset += action.CaretOffset;

        e.Handled = true;
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;

        // Whatever was suggested was suggested for the line as it was a
        // keystroke ago. Left on screen it reads as an answer to what is there
        // now, which is worse than showing nothing: the request below asks
        // again for the line as it stands.
        ClearSuggestion();

        // Guarded, and no longer "async void": anything thrown while working
        // out what to offer used to reach the runtime, where it either took
        // the application down or — worse for finding it — was swallowed and
        // the list simply never appeared.
        Guarded.Run(() => OnTextEnteredAsync(e), _shell.WriteOutput, "editor");
    }

    private async Task OnTextEnteredAsync(TextInputEventArgs e)
    {

        // The dot is the natural trigger in both languages, and it is worth
        // answering at once: the member list is what the dot was typed for.
        if (e.Text == ".")
        {
            await ShowCompletionAsync();
            return;
        }

        // A letter opens the list as well, the way Visual Studio does: waiting
        // for a dot means the list never appears for a bare name, which is
        // most of what anyone types. Only on the first letters of a word — in
        // the middle of one the list is already open and filtering itself.
        if (_completionWindow is null
            && _suggestAutomatically
            && char.IsLetter(e.Text[0])
            && WordBeforeCaret().Length >= 1)
        {
            // After a pause, not on the keystroke: seven letters used to mean
            // seven round trips through Roslyn, five of them still in flight
            // when the next one started.
            RequestCompletionAfterPause();
            RequestSuggestionAfterPause();
            return;
        }

        // The parameters of the call being written.
        if (e.Text is "(" or ",")
        {
            await ShowSignatureHelpAsync();
            return;
        }

        // A closing brace lands unindented as it is typed; re-indenting it here
        // is what makes it snap back to its block level.
        if (_shell.TriggersFormatting(e.Text[0], _document.Language))
            await FormatCurrentLineAsync();

        // Visual Basic is deliberately not formatted per keystroke. Formatting
        // is asynchronous while typing is not, so a result computed from an
        // older document would land after further characters had arrived and
        // overwrite them. The line is formatted on Enter instead, once the user
        // has finished writing it.
    }

    /// <summary>
    /// Applies Visual Basic conventions to the line the caret is on: keyword
    /// casing, spacing around operators, and indentation.
    ///
    /// The whole line is corrected rather than the last word alone, because in
    /// "end if" the word "end" was finished earlier and would otherwise keep
    /// its lowercase spelling.
    /// </summary>
    internal async Task ApplyVisualBasicConventionsAsync()
    {
        if (!AutoFormatWhileTyping) return;
        if (_document.Language != SourceLanguage.VisualBasic) return;

        var caret = _editor.CaretOffset;

        // The document as it stands now. Formatting runs asynchronously, and
        // the user keeps typing meanwhile: applying a result computed from an
        // older document would overwrite the characters typed since.
        var snapshot = _editor.Text;

        var result = await _shell
            .ApplyTypingConventionsAsync(snapshot, _document.Language, caret)
            .ConfigureAwait(true);

        if (!string.Equals(snapshot, _editor.Text, StringComparison.Ordinal)) return;

        // Identifier casing needs the project's symbols, so it runs against the
        // language service rather than the standalone formatter.
        var lineIndex = _editor.Document.GetLineByOffset(
            Math.Clamp(caret, 0, result.Text.Length)).LineNumber - 1;

        var withIdentifiers = await _shell
            .CorrectIdentifierCasingAsync(_document.FilePath, result.Text, lineIndex)
            .ConfigureAwait(true);

        if (!string.Equals(snapshot, _editor.Text, StringComparison.Ordinal)) return;

        if (string.Equals(withIdentifiers, _editor.Text, StringComparison.Ordinal)) return;

        // Only the caret's line is rewritten, never the whole document.
        // Replacing Document.Text discards keystrokes that arrive while the
        // replacement is in flight, which showed up as dropped characters.
        ReplaceCurrentLine(withIdentifiers, result.Caret);
    }

    /// <summary>
    /// Applies an updated document by rewriting just the line the caret is on.
    ///
    /// The two versions differ only within that line, so a targeted replacement
    /// leaves the rest of the document — and any pending input — untouched.
    /// </summary>
    private void ReplaceCurrentLine(string updatedDocument, int caretAfter)
    {
        var caret = Math.Clamp(_editor.CaretOffset, 0, _editor.Document.TextLength);
        var line = _editor.Document.GetLineByOffset(caret);

        var updated = updatedDocument.Split('\n');
        var index = line.LineNumber - 1;
        if (index < 0 || index >= updated.Length) return;

        var replacement = updated[index];
        var current = _editor.Document.GetText(line.Offset, line.Length);

        if (string.Equals(current, replacement, StringComparison.Ordinal)) return;

        _suppressTextChanged = true;
        try
        {
            _editor.Document.Replace(line.Offset, line.Length, replacement);
            _editor.CaretOffset = Math.Clamp(caretAfter, 0, _editor.Document.TextLength);
        }
        finally
        {
            _suppressTextChanged = false;
        }

        _document.Text = _editor.Text;
    }

    /// <summary>
    /// Handles Enter in Visual Basic: tidy the line being left, insert the new
    /// line at the right indentation, and write the block's closing line when
    /// one has just been opened.
    ///
    /// Done as a single edit so one undo takes the whole thing back, and so the
    /// caret lands where the user expects rather than being nudged by three
    /// separate operations.
    /// </summary>
    /// <summary>Entry point for tests: headless input never reaches the text area.</summary>
    internal Task HandleEnterForTestsAsync() => HandleVisualBasicEnterAsync();

    private async Task HandleVisualBasicEnterAsync()
    {
        if (!AutoFormatWhileTyping)
        {
            InsertPlainNewLine();
            return;
        }

        // 1. Canonical casing, spacing and indentation for the line being left.
        await ApplyVisualBasicConventionsAsync();

        var caret = _editor.CaretOffset;
        var currentLine = _editor.Document.GetLineByOffset(caret);
        var lineIndex = currentLine.LineNumber - 1;
        var text = _editor.Text;

        // 2. Indentation for the line about to be created.
        var innerIndent = await _shell
            .GetIndentationAsync(text, SourceLanguage.VisualBasic, currentLine.EndOffset)
            .ConfigureAwait(true);

        // 3. The closing line, when this line opened a block.
        var closing = await _shell
            .GetBlockClosingAsync(text, SourceLanguage.VisualBasic, lineIndex)
            .ConfigureAwait(true);

        var builder = new System.Text.StringBuilder();
        builder.Append('\n').Append(new string(' ', innerIndent));

        var caretAfter = caret + builder.Length;

        if (closing is not null)
        {
            // The closing line sits one level out from the block's body.
            var closingIndent = Math.Max(0, innerIndent - _editor.Options.IndentationSize);
            builder.Append('\n').Append(new string(' ', closingIndent)).Append(closing);
        }

        _suppressTextChanged = true;
        try
        {
            _editor.Document.Insert(caret, builder.ToString());
            _editor.CaretOffset = Math.Clamp(caretAfter, 0, _editor.Document.TextLength);
        }
        finally
        {
            _suppressTextChanged = false;
        }

        _document.Text = _editor.Text;
    }

    /// <summary>Inserts a newline the way the editor would, with no extra work.</summary>
    private void InsertPlainNewLine()
    {
        var caret = _editor.CaretOffset;
        _editor.Document.Insert(caret, "\n");
        _editor.CaretOffset = caret + 1;
    }

    /// <summary>
    /// Indents a line the user has just started with Enter.
    ///
    /// AvaloniaEdit copies the previous line's indentation, which is wrong
    /// after a block opens or closes. The correct column comes from the
    /// language service, and is applied only while the line is still empty so
    /// nothing the user typed is disturbed.
    /// </summary>
    internal async Task IndentNewLineAsync()
    {
        if (!AutoFormatWhileTyping) return;
        if (_document.Language is not SourceLanguage.VisualBasic) return;

        var caret = _editor.CaretOffset;
        var line = _editor.Document.GetLineByOffset(caret);
        var existing = _editor.Document.GetText(line.Offset, line.Length);

        if (existing.Trim().Length > 0) return;

        var wanted = await _shell
            .GetIndentationAsync(_editor.Text, _document.Language, line.Offset)
            .ConfigureAwait(true);

        if (wanted == existing.Length) return;

        _suppressTextChanged = true;
        try
        {
            _editor.Document.Replace(line.Offset, line.Length, new string(' ', wanted));
            _editor.CaretOffset = line.Offset + wanted;
        }
        finally
        {
            _suppressTextChanged = false;
        }

        _document.Text = _editor.Text;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // A suggestion answers to Tab before anything else does. Only when one
        // is showing: Tab is indentation the rest of the time, and taking it
        // away would be a worse trade than any suggestion is worth.
        if (_ghostText.IsShowing && e.KeyModifiers == KeyModifiers.None)
        {
            if (e.Key == Key.Tab)
            {
                e.Handled = AcceptSuggestion();
                return;
            }

            if (e.Key == Key.Escape)
            {
                ClearSuggestion();
                e.Handled = true;
                return;
            }
        }

        // A word at a time, for a suggestion that starts well and goes wrong.
        if (_ghostText.IsShowing
            && e.Key == Key.Right
            && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            e.Handled = AcceptSuggestion(wordOnly: true);
            return;
        }

        // Ctrl+Space invokes completion at any position.
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await ShowCompletionAsync();
            return;
        }

        // Enter is handled here rather than left to the editor: the line being
        // left has to be tidied, a block may need closing, and the new line
        // needs the right indentation — all in one edit the user can undo once.
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (_document.Language == SourceLanguage.VisualBasic)
            {
                e.Handled = true;
                await HandleVisualBasicEnterAsync();
                return;
            }

            await FormatCurrentLineAsync();

            // The newline arrives only after this handler returns, so indenting
            // the new line is queued rather than done here.
            Dispatcher.UIThread.Post(
                () => _ = IndentNewLineAsync(), DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Re-indents the line holding the caret.
    ///
    /// Text and caret are written back together: changing a line's indentation
    /// shifts every position after it, and setting the text alone would leave
    /// the caret at the wrong column.
    /// </summary>
    /// <summary>
    /// Tidies the selected lines, leaving the rest of the file alone.
    /// </summary>
    internal async Task FormatSelectionAsync()
    {
        if (_editor.SelectionLength <= 0) return;

        var start = _editor.SelectionStart;
        var length = _editor.SelectionLength;
        var caret = _editor.CaretOffset;

        var result = await _shell
            .FormatRangeAsync(_editor.Text, _document.Language, start, length)
            .ConfigureAwait(true);

        if (!result.Changed) return;

        _suppressTextChanged = true;

        try
        {
            _editor.Document.Replace(0, _editor.Document.TextLength, result.Text);
        }
        finally
        {
            _suppressTextChanged = false;
        }

        _editor.CaretOffset = Math.Clamp(caret, 0, _editor.Document.TextLength);

        _document.Text = _editor.Text;
    }

    internal async Task FormatCurrentLineAsync()
    {
        if (!AutoFormatWhileTyping) return;
        if (_document.Language is not SourceLanguage.VisualBasic) return;

        var caret = _editor.CaretOffset;

        var result = await _shell
            .FormatLineAsync(_editor.Text, _document.Language, caret)
            .ConfigureAwait(true);

        if (!result.Changed) return;

        ReplaceCurrentLine(result.Text, result.Caret);
    }

    /// <summary>Whether lines are re-indented as the user types.</summary>
    /// <summary>
    /// The margin where breakpoints are set.
    ///
    /// Exposed so the window can connect it to the debugging session, which
    /// owns the breakpoints: they must survive this editor being closed.
    /// </summary>
    public BreakpointMargin BreakpointMargin { get; }

    /// <summary>The strip marking lines that differ from the last commit.</summary>
    public GitChangeMargin GitChangeMargin { get; }

    /// <summary>
    /// The menu shown on a right click.
    ///
    /// Exposed so the window can act on what is chosen: the editor knows the
    /// text, the window knows the solution, and only the window can navigate.
    /// </summary>
    public EditorContextMenu EditorMenu { get; }

    /// <summary>The file this editor is showing.</summary>
    public string FilePath => _document.FilePath;

    /// <summary>
    /// Puts the caret in the text.
    ///
    /// The editor is a wrapper: focusing it leaves the text area unfocused,
    /// and keystrokes go nowhere.
    /// </summary>
    public void FocusText() => _editor.TextArea.Focus();

    /// <summary>The document this editor is showing.</summary>
    internal EditorDocumentViewModel Document => _document;

    public bool AutoFormatWhileTyping { get; set; } = true;

    /// <summary>Raised when the caret moves, so the status bar can follow it.</summary>
    public event EventHandler? CaretMoved;

    /// <summary>Whether brackets and quotes close themselves as they are opened.</summary>
    public bool AutoCloseBrackets { get; set; } = true;

    /// <summary>
    /// Applies the user's settings to this editor.
    ///
    /// Called on every change rather than only when a file is opened, so a
    /// setting takes effect where the user can see it.
    /// </summary>
    /// <summary>
    /// The theme the syntax colours follow.
    ///
    /// Kept because highlighting is installed once per document and has to be
    /// told when the theme changes afterwards.
    /// </summary>
    private AppTheme _theme = AppTheme.Light;

    public void ApplySettings(IdeSettings settings)
    {
        var editor = settings.Editor;

        // The setting existed and was shown in the settings window; nothing
        // read it, so turning it off changed nothing.
        _suggestAutomatically = editor.CompleteAutomatically;

        // Syntax colours follow the interface theme: Solarized panels around
        // Dark+ colours read as two themes at once.
        var theme = settings.Appearance.Theme == AppTheme.System
            ? SystemTheme()
            : settings.Appearance.Theme;

        if (theme != _theme)
        {
            _theme = theme;
            _textMate?.SetTheme(theme);
        }

        _editor.FontFamily = new FontFamily(editor.FontFamily);
        _editor.FontSize = editor.FontSize;
        _editor.ShowLineNumbers = editor.ShowLineNumbers;
        _editor.WordWrap = editor.WordWrap;

        _editor.Options.ConvertTabsToSpaces = editor.ConvertTabsToSpaces;
        _editor.Options.IndentationSize = editor.IndentationSize;
        _editor.Options.ShowSpaces = editor.ShowWhitespace;
        _editor.Options.ShowTabs = editor.ShowWhitespace;

        AutoCloseBrackets = editor.AutoCloseBrackets;

        // A language may override the general setting for its own files.
        var language = LanguageIdFor(_document.FilePath);

        AutoFormatWhileTyping = language is null
            ? editor.FormatWhileTyping
            : settings.GetLanguageSetting(
                  language, "FormatWhileTyping", editor.FormatWhileTyping ? "true" : "false")
              == "true";
    }

    /// <summary>The language id a file belongs to, for its own settings.</summary>
    private static string? LanguageIdFor(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".vb" => "vb",
            ".cs" => "csharp",
            ".html" or ".htm" => "html",
            ".css" => "css",
            ".vbhtml" => "vbhtml",
            _ => null
        };

    /// <summary>
    /// The characters that take the highlighted entry and then type themselves.
    /// </summary>
    /// <remarks>
    /// What Visual Basic has always done, and what makes the list feel like
    /// help rather than an obstacle: "Console.Wr" followed by a dot gives
    /// Console.WriteLine. — the entry is taken and the dot arrives after it.
    ///
    /// Each of these ends a name: nobody types a dot in the middle of one, so
    /// a dot means the word is finished whatever the list is showing. A letter
    /// is not here, because a letter is how the word being typed keeps going.
    /// </remarks>
    internal static IReadOnlyDictionary<Key, string> CommitCharactersForTests => CommitCharacters;

    private static readonly Dictionary<Key, string> CommitCharacters = new()
    {
        [Key.Space] = " ",
        [Key.OemPeriod] = ".",
        [Key.OemOpenBrackets] = "(",
        [Key.OemComma] = ",",
    };

    /// <summary>
    /// Keys the completion list answers to beyond its own.
    /// </summary>
    /// <remarks>
    /// Tab and Enter are the list's own — AvaloniaEdit commits on both — so
    /// only the characters that also have to be typed are handled here. One
    /// typed when nothing is highlighted is only itself.
    /// </remarks>
    private void OnCompletionKeyDown(object? sender, KeyEventArgs e)
    {
        if (_completionWindow is null) return;

        if (e.KeyModifiers != KeyModifiers.None) return;

        if (!CommitCharacters.TryGetValue(e.Key, out var character)) return;

        if (_completionWindow.CompletionList.SelectedItem is null) return;

        // Committed first, then the character: the other order would insert it
        // into the word being replaced.
        _completionWindow.CompletionList.RequestInsertion(e);

        _editor.Document.Insert(_editor.CaretOffset, character);

        e.Handled = true;
    }

    /// <summary>
    /// Presses a key on the completion list, for the tests.
    ///
    /// Returns whether there was an entry to take: a headless run has no
    /// screen to put the popup on, so there often is not.
    /// </summary>
    internal bool AcceptCompletionWithForTests(Key key)
    {
        if (_completionWindow?.CompletionList.SelectedItem is null) return false;

        OnCompletionKeyDown(this, new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key
        });

        return true;
    }

    internal bool AcceptCompletionWithSpaceForTests() =>
        AcceptCompletionWithForTests(Key.Space);

    private OverloadInsightWindow? _signatureWindow;

    /// <summary>
    /// Shows what the call being written expects.
    ///
    /// Typing an open bracket used to show nothing at all, so the parameters
    /// had to be found somewhere else — which is exactly the moment the
    /// answer is needed.
    /// </summary>
    private async Task ShowSignatureHelpAsync()
    {
        _signatureWindow?.Close();
        _signatureWindow = null;

        var described = await _shell
            .DescribeCallAsync(_document.FilePath, _editor.CaretOffset, _editor.Text)
            .ConfigureAwait(true);

        if (described is null || described.Overloads.Count == 0) return;

        _signatureWindow = new OverloadInsightWindow(_editor.TextArea)
        {
            Provider = new DescriptionOverloads(described)
        };

        _signatureWindow.Closed += (_, _) => _signatureWindow = null;

        // Up and down step through the overloads, as they do in Visual
        // Studio; without this a call with three forms shows only the first.
        _signatureWindow.KeyDown += OnSignatureKeyDown;

        _signatureWindow.Show();
    }

    /// <summary>
    /// Keys the call help answers to.
    ///
    /// Up and down walk the overloads. Escape closes it, which is what any
    /// window offering itself unasked should do.
    /// </summary>
    private void OnSignatureKeyDown(object? sender, KeyEventArgs e)
    {
        if (_signatureWindow?.Provider is not { } provider) return;

        switch (e.Key)
        {
            case Key.Up:
                provider.SelectedIndex =
                    (provider.SelectedIndex - 1 + provider.Count) % provider.Count;
                e.Handled = true;
                break;

            case Key.Down:
                provider.SelectedIndex = (provider.SelectedIndex + 1) % provider.Count;
                e.Handled = true;
                break;

            case Key.Escape:
                _signatureWindow.Close();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Feeds the described overloads to the window AvaloniaEdit draws.
    ///
    /// It knows nothing of Visual Basic: it is handed descriptions, and the
    /// same class serves QuickBASIC and whatever dialect comes next.
    /// </summary>
    private sealed class DescriptionOverloads : IOverloadProvider
    {
        private readonly Basalt.Extensibility.SymbolDescriptionSet _set;
        private int _selected;

        public DescriptionOverloads(Basalt.Extensibility.SymbolDescriptionSet set)
        {
            _set = set;
            _selected = Math.Clamp(set.Active, 0, set.Overloads.Count - 1);
        }

        public int SelectedIndex
        {
            get => _selected;
            set
            {
                _selected = Math.Clamp(value, 0, _set.Overloads.Count - 1);

                Changed(nameof(SelectedIndex));
                Changed(nameof(CurrentHeader));
                Changed(nameof(CurrentContent));
                Changed(nameof(CurrentIndexText));
            }
        }

        public int Count => _set.Overloads.Count;

        /// <summary>"1 of 3", so more overloads are known to exist.</summary>
        public string CurrentIndexText => Count > 1 ? $"{_selected + 1} of {Count}" : "";

        public object CurrentHeader =>
            new SymbolDescriptionView(_set.Overloads[_selected]);

        // Everything is in the header: splitting it would put the parameter
        // list in a second box that scrolls separately.
        public object? CurrentContent => null;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Changed(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private readonly ToolTip _hover = new();
    private CancellationTokenSource? _hoverRequest;

    /// <summary>
    /// What a symbol is, shown by resting the pointer on it.
    ///
    /// There was none at all: the language service could answer and nothing
    /// asked. It is how you find out what something is without leaving the
    /// line you are reading.
    /// </summary>
    private void InstallHover()
    {
        var view = _editor.TextArea.TextView;

        // On the tunnelling event as well: something between the pointer and
        // the text view can handle the bubbling one first, and then the hover
        // never arrives.
        view.AddHandler(
            AvaloniaEdit.Rendering.TextView.PreviewPointerHoverEvent,
            (object? _, PointerEventArgs e) => Guarded.Run(
                () => ShowHoverAsync(e.GetPosition(view)), _ => { }, "editor"));

        view.PointerHover += (_, e) =>
        {
            Trace("pointer rested");

            Guarded.Run(() => ShowHoverAsync(e.GetPosition(view)), _ => { }, "editor");
        };

        // Moving on dismisses it: a tooltip left behind describes whatever
        // used to be under the pointer.
        view.PointerHoverStopped += (_, _) => HideHover();
        view.PointerExited += (_, _) => HideHover();
    }

    /// <summary>
    /// Describes what is under the caret, as resting the pointer there would.
    ///
    /// The pointer is not the only way to ask: a hover needs a hand on the
    /// mouse, and the answer is wanted most while typing.
    /// </summary>
    internal Task<bool> ShowHoverAtCaretAsync() => ShowHoverAtAsync(_editor.CaretOffset);

    /// <summary>Shows the tooltip for an offset.</summary>
    internal async Task<bool> ShowHoverAtAsync(int offset)
    {
        var description = await _shell
            .DescribeSymbolAsync(_document.FilePath, offset, _editor.Text)
            .ConfigureAwait(true);

        if (description is null) return false;

        _hover.Content = new SymbolDescriptionView(description);

        ToolTip.SetTip(_editor, _hover);
        ToolTip.SetIsOpen(_editor, true);

        return ToolTip.GetIsOpen(_editor);
    }

    private void HideHover()
    {
        _hoverRequest?.Cancel();

        ToolTip.SetIsOpen(_editor, false);
    }

    /// <summary>Asks what the symbol under a point is, and says so.</summary>
    private async Task ShowHoverAsync(Avalonia.Point point)
    {
        var view = _editor.TextArea.TextView;

        var position = view.GetPositionFloor(point + view.ScrollOffset);

        if (position is not { } at)
        {
            Trace("the pointer is not over any text");
            return;
        }

        var offset = _editor.Document.GetOffset(at.Location);

        // A fresh request cancels the one before it: the pointer moves faster
        // than the compiler answers.
        _hoverRequest?.Cancel();
        _hoverRequest = new CancellationTokenSource();

        var token = _hoverRequest.Token;

        var description = await _shell
            .DescribeSymbolAsync(_document.FilePath, offset, _editor.Text)
            .ConfigureAwait(true);

        if (token.IsCancellationRequested)
        {
            Trace("the pointer moved before the answer came");
            return;
        }

        if (description is null)
        {
            Trace($"nothing to say about offset {offset}");
            return;
        }

        Trace($"describing '{description.PlainSignature}'");

        // The same control the call help uses: they are the same information
        // asked two ways, and drawing them separately made two things to keep
        // in step.
        _hover.Content = new SymbolDescriptionView(description);

        ToolTip.SetTip(_editor, _hover);
        ToolTip.SetIsOpen(_editor, true);
    }


    /// <summary>
    /// Asks for a suggestion once typing pauses.
    /// </summary>
    /// <remarks>
    /// Longer than the completion pause, and deliberately: a suggestion costs
    /// a round trip to a service rather than a query against a compilation
    /// already in memory, and one per keystroke would be both slow and
    /// expensive. Long enough that it arrives when someone stops to think,
    /// which is when a suggestion is wanted.
    /// </remarks>
    private const int SuggestionPauseMilliseconds = 300;

    private void RequestSuggestionAfterPause()
    {
        _suggestionDebounce?.Cancel();

        // Cleared straight away: a suggestion made for what was on the line a
        // moment ago is worse than none, because it reads as an answer to what
        // is there now.
        ClearSuggestion();

        if (SuggestionProvider is not { IsAvailable: true } provider) return;

        _suggestionDebounce = new CancellationTokenSource();

        var token = _suggestionDebounce.Token;
        var at = _editor.CaretOffset;

        Guarded.Run(async () =>
        {
            await Task.Delay(SuggestionPauseMilliseconds, token).ConfigureAwait(true);

            if (token.IsCancellationRequested) return;

            var suggestion = await provider
                .SuggestAsync(_document.FilePath, _editor.Text, at, token)
                .ConfigureAwait(true);

            if (token.IsCancellationRequested || suggestion is null) return;

            // The caret has to still be where the suggestion was asked for.
            // Shown anyway, it appears in the middle of a word somebody has
            // gone on typing.
            if (_editor.CaretOffset != at) return;

            ShowSuggestion(suggestion);
        },
        _ => { }, "editor");
    }

    /// <summary>Shows a suggestion in the editor.</summary>
    private void ShowSuggestion(Basalt.Extensibility.InlineSuggestion suggestion)
    {
        if (suggestion.FirstLine.Length == 0) return;

        _suggestion = suggestion;
        _ghostText.Show(suggestion.FirstLine, suggestion.Position);
        _editor.TextArea.TextView.Redraw();
    }

    /// <summary>Takes a suggestion away.</summary>
    private void ClearSuggestion()
    {
        if (!_ghostText.IsShowing) return;

        _suggestion = null;
        _ghostText.Clear();
        _editor.TextArea.TextView.Redraw();
    }

    private Basalt.Extensibility.InlineSuggestion? _suggestion;

    /// <summary>Whether something is being suggested.</summary>
    public bool IsSuggesting => _ghostText.IsShowing;

    /// <summary>
    /// Writes the suggestion into the document.
    /// </summary>
    /// <param name="wordOnly">
    /// Take only the first word. A suggestion is often right at the start and
    /// wrong further along, and this is how somebody keeps the useful part
    /// without deleting the rest afterwards.
    /// </param>
    public bool AcceptSuggestion(bool wordOnly = false)
    {
        if (_suggestion is not { } suggestion) return false;

        var text = wordOnly ? suggestion.FirstWord : suggestion.FirstLine;

        if (text.Length == 0) return false;

        var at = Math.Clamp(suggestion.Position, 0, _editor.Document.TextLength);

        ClearSuggestion();

        _editor.Document.Insert(at, text);
        _editor.CaretOffset = at + text.Length;

        return true;
    }

    private CancellationTokenSource? _completionDebounce;

    /// <summary>
    /// Asks for completion once typing pauses.
    ///
    /// Typing seven letters used to make seven requests, and five of them
    /// were still in flight when the next began: they answer in whatever
    /// order they finish, and the last to arrive wins. After "Console." that
    /// meant the 3854 names in scope replacing Console's own members.
    /// </summary>
    /// <summary>
    /// How long typing has to pause before completion is asked for.
    /// </summary>
    /// <remarks>
    /// Short enough not to be felt. It was 180ms, which is long enough that a
    /// list asked for on the first letter of a word arrives after the second
    /// has been typed — the pause everyone notices and nobody can name.
    ///
    /// Not zero: the cancellation below stops a stale answer from winning, but
    /// it cannot stop the request being made, and a request per keystroke is
    /// work Roslyn does and throws away. Forty milliseconds is under the
    /// threshold where a delay reads as one and still collapses a burst of
    /// typing into a single ask.
    /// </remarks>
    private const int CompletionPauseMilliseconds = 40;

    private void RequestCompletionAfterPause()
    {
        _completionDebounce?.Cancel();
        _completionDebounce = new CancellationTokenSource();

        var token = _completionDebounce.Token;

        Guarded.Run(async () =>
        {
            await Task.Delay(CompletionPauseMilliseconds, token).ConfigureAwait(true);

            if (token.IsCancellationRequested) return;

            await ShowCompletionAsync(token).ConfigureAwait(true);
        },
        _ => { }, "editor");
    }

    /// <summary>
    /// Whether to write to the output pane what completion is doing.
    ///
    /// Set BASALT_TRACE_COMPLETION to see it. Off by default: this is for
    /// finding out why a list did not appear on a machine that is not the
    /// one the tests run on.
    /// </summary>
    private static readonly bool TraceCompletion =
        Environment.GetEnvironmentVariable("BASALT_TRACE_COMPLETION") is { Length: > 0 };

    private void Trace(string message)
    {
        if (TraceCompletion) _shell.WriteOutput($"[completion] {message}");
    }

    private async Task ShowCompletionAsync(CancellationToken ct = default)
    {
        CompletionRequestsForTests++;

        // Where the question is being asked from, and what was written when
        // it was asked.
        var askedAt = _editor.CaretOffset;
        var askedFor = _editor.Document.TextLength;

        // The editor's text is newer than the workspace's copy while typing.
        var completions = await _shell
            .GetCompletionsAsync(_document.FilePath, askedAt, _editor.Text)
            .ConfigureAwait(true);

        Trace($"asked at {askedAt}, got {completions.Count}");

        if (ct.IsCancellationRequested)
        {
            Trace("cancelled while waiting");
            return;
        }

        // Whether more was typed while the answer was being computed, not
        // whether the caret sits exactly where it did: TextEntered runs while
        // the editor is still placing the caret for the character just typed,
        // so comparing positions threw away good answers on a real machine
        // and kept them only when the reply came back fast enough to win the
        // race — which headless always did, and a real screen never did.
        if (_editor.Document.TextLength != askedFor)
        {
            Trace($"more was typed: {askedFor} -> {_editor.Document.TextLength}");
            return;
        }

        if (completions.Count == 0)
        {
            Trace("nothing to offer");
            return;
        }

        // What has been typed so far narrows and orders the list: with a
        // couple of letters typed, the best match should be selected rather
        // than whatever happens to sort first.
        var typed = WordBeforeCaret();

        var ranked = CompletionMatcher.Filter(completions, typed);

        LastCompletionCountForTests = ranked.Count;

        if (ranked.Count == 0)
        {
            Trace($"nothing matched '{typed}'");
            return;
        }

        _completionWindow = new CompletionWindow(_editor.TextArea)
        {
            // The window replaces the word being typed, not just what follows
            // the caret; without this the letters already typed are doubled.
            StartOffset = _editor.CaretOffset - typed.Length
        };

        // A cap keeps a list of thousands from being built for a window that
        // shows a dozen rows; the ordering means the cut falls on the least
        // relevant entries.
        foreach (var match in ranked.Take(200))
            _completionWindow.CompletionList.CompletionData.Add(new RoslynCompletionData(match.Item));

        _completionWindow.CompletionList.SelectedItem =
            _completionWindow.CompletionList.CompletionData.FirstOrDefault();

        _completionWindow.Closed += (_, _) => _completionWindow = null;

        // Space accepts the highlighted entry and types the space, the way
        // Visual Studio does: reaching for Tab or Enter breaks the flow of
        // writing a line.
        _completionWindow.CompletionList.KeyDown += OnCompletionKeyDown;

        _completionWindow.Show();

        Trace($"showing {ranked.Count} for '{typed}'");
    }

    /// <summary>
    /// The identifier being typed, which the completion list filters on.
    ///
    /// Empty right after a dot, where every member is a candidate.
    /// </summary>
    private string WordBeforeCaret()
    {
        var text = _editor.Text;
        var caret = Math.Clamp(_editor.CaretOffset, 0, text.Length);

        var start = caret;

        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
            start--;

        return text[start..caret];
    }

    /// <summary>Adapts a Roslyn completion entry to the AvaloniaEdit list.</summary>
    private sealed class RoslynCompletionData : ICompletionData
    {
        private readonly CompletionItem _item;

        public RoslynCompletionData(CompletionItem item) => _item = item;

        public IImage? Image => CompletionIcons.For(_item.Kind);
        public string Text => _item.InsertionText;
        public object Content => _item.DisplayText;
        public object Description => _item.Description ?? _item.Kind.ToString();
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
            textArea.Document.Replace(completionSegment, Text);
    }
}
