using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Core.Localization;
using Basalt.Workspace.Search;

namespace Basalt.Shell.Controls;

/// <summary>
/// The find and replace bar above one editor, as Visual Studio shows it.
///
/// Ctrl+F opens it with the word at the caret in the box; F3 and Shift+F3 step
/// through the matches and wrap at either end; Ctrl+H adds the replace row.
/// Esc closes it and gives the keyboard back to the editor.
/// </summary>
/// <remarks>
/// AvaloniaEdit ships its own search panel, but it has no replace row and its
/// labels are fixed in English, so this bar is written here on the same
/// <see cref="SearchEngine"/> that Find in Files uses. One search engine means
/// one meaning of "match case" and "whole word" across the IDE.
/// </remarks>
public sealed class EditorFindBar : Border
{
    private static readonly ConditionalWeakTable<TextEditor, EditorFindBar> Bars = new();

    private readonly TextEditor _editor;
    private readonly SearchEngine _engine = new();
    private readonly TextBox _term;
    private readonly TextBox _replacement;
    private readonly ToggleButton _matchCase;
    private readonly ToggleButton _wholeWord;
    private readonly ToggleButton _useRegex;
    private readonly TextBlock _status;
    private readonly StackPanel _replaceRow;
    private readonly Button _replaceOne;
    private readonly Button _replaceAll;

    private EditorFindBar(TextEditor editor)
    {
        _editor = editor;

        _term = Input(Localizer.Get(StringKeys.SearchFind));
        _replacement = Input(Localizer.Get(StringKeys.SearchReplaceWith));

        _matchCase = PanelToolbar.TextToggle("Aa", Localizer.Get(StringKeys.SearchMatchCase));
        _wholeWord = PanelToolbar.TextToggle("ab", Localizer.Get(StringKeys.SearchWholeWord));
        _useRegex = PanelToolbar.TextToggle(".*", Localizer.Get(StringKeys.SearchUseRegex));

        _status = new TextBlock
        {
            FontSize = PanelMetrics.SecondaryFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Spacing.Normal, 0)
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        var previous = Icon("↑", StringKeys.SearchFindPrevious);
        var next = Icon("↓", StringKeys.SearchFindNext);
        var close = Icon("✕", StringKeys.SearchClose);
        previous.Click += (_, _) => Find(forward: false);
        next.Click += (_, _) => Find(forward: true);
        close.Click += (_, _) => Close();

        _replaceOne = new Button { Content = Localizer.Get(StringKeys.SearchReplaceOne) };
        _replaceAll = new Button { Content = Localizer.Get(StringKeys.SearchReplaceAll) };
        _replaceOne.Click += (_, _) => ReplaceOne();
        _replaceAll.Click += (_, _) => ReplaceAll();

        _term.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;

            Find(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        };
        _replacement.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;

            ReplaceOne();
            e.Handled = true;
        };
        // The property, not the TextChanged event: a text set in code is seen
        // through the property on every write.
        _term.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) RefreshStatus();
        };
        _matchCase.IsCheckedChanged += (_, _) => RefreshStatus();
        _wholeWord.IsCheckedChanged += (_, _) => RefreshStatus();
        _useRegex.IsCheckedChanged += (_, _) => RefreshStatus();

        var findRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            Children = { _term, previous, next, _matchCase, _wholeWord, _useRegex, _status, close }
        };

        _replaceRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            IsVisible = false,
            Children = { _replacement, _replaceOne, _replaceAll }
        };

        Child = new StackPanel
        {
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            Spacing = Spacing.Tight,
            Children = { findRow, _replaceRow }
        };
        BorderThickness = new Thickness(0, 0, 0, 1);
        this.Themed(BorderBrushProperty, "PanelBorderBrush");
        IsVisible = false;

        // Tunnelling, so the bar sees F3 and Esc before the text area takes them.
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        editor.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Opens the find bar of an editor, with or without the replace row. Returns
    /// false when there is no editor, or it has no bar, so the caller can fall back.
    /// </summary>
    public static bool OpenFor(TextEditor? editor, bool withReplace)
    {
        if (editor is null || For(editor) is not { } bar) return false;

        bar.Open(withReplace);
        return true;
    }

    /// <summary>
    /// Whether a window shortcut must stand aside for this Esc: Esc closes an
    /// open find bar before any command bound to Esc runs.
    /// </summary>
    public static bool ClaimsEscape(KeyEventArgs e) =>
        e.Key == Key.Escape && OwnsKey(e.Source as Visual);

    /// <summary>The bar of an editor, once that editor has been wrapped by <see cref="Wrap"/>.</summary>
    public static EditorFindBar? For(TextEditor editor) =>
        Bars.TryGetValue(editor, out var bar) ? bar : null;

    /// <summary>
    /// Whether a key pressed at this control belongs to an open find bar: the
    /// bar's own controls, or the text of the editor it is above. Window
    /// shortcuts stand aside for such a key, because Esc means "close the bar"
    /// there and a command bound to Esc would otherwise take it first.
    /// </summary>
    public static bool OwnsKey(Visual? source) =>
        source is not null && source.GetVisualAncestors().Prepend(source).Any(v =>
            v is EditorFindBar bar ? bar.IsOpen : v is TextEditor editor && For(editor) is { IsOpen: true });

    /// <summary>
    /// Puts the editor's find bar above its content. The bar starts hidden and
    /// takes no room until Ctrl+F opens it.
    /// </summary>
    public static Control Wrap(TextEditor editor, Control content)
    {
        var bar = new EditorFindBar(editor);
        Bars.AddOrUpdate(editor, bar);

        var layout = new DockPanel();
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);
        layout.Children.Add(bar);
        layout.Children.Add(content);
        return layout;
    }

    /// <summary>Whether the bar is on screen.</summary>
    public bool IsOpen => IsVisible;

    /// <summary>The term, as typed.</summary>
    public string Term => _term.Text ?? "";

    /// <summary>The replacement, as typed.</summary>
    public string Replacement => _replacement.Text ?? "";

    /// <summary>What the bar says about the matches, or why it cannot search.</summary>
    public string Status => _status.Text ?? "";

    /// <summary>
    /// Opens the bar for a search. The word at the caret, or the selection
    /// when it is on one line, becomes the term and is selected for typing over.
    /// </summary>
    /// <param name="withReplace">True to show the replace row too (Ctrl+H).</param>
    public void Open(bool withReplace)
    {
        var prefill = PrefillText();
        if (!string.IsNullOrEmpty(prefill)) _term.Text = prefill;

        _replaceRow.IsVisible = withReplace;
        IsVisible = true;
        _term.Focus();
        _term.SelectAll();
        RefreshStatus();
    }

    /// <summary>Hides the bar and gives the keyboard back to the text area itself.</summary>
    public void Close()
    {
        IsVisible = false;
        _editor.TextArea.Focus();
    }

    /// <summary>The controls the tests press, read and toggle.</summary>
    internal TextBox TermBox => _term;
    internal TextBox ReplacementBox => _replacement;
    internal ToggleButton MatchCaseToggle => _matchCase;
    internal ToggleButton WholeWordToggle => _wholeWord;
    internal ToggleButton RegexToggle => _useRegex;
    internal Button ReplaceOneButton => _replaceOne;
    internal Button ReplaceAllButton => _replaceAll;

    /// <summary>
    /// Applies a replacement plan to a document as one undoable edit. Undo
    /// reverts all of it at once, so a Replace All is never a stack of steps.
    /// </summary>
    /// <remarks>
    /// The plan is applied last first: each change sits at an offset found
    /// against the text as it was, so an earlier change must not move the
    /// later ones before they are made.
    /// </remarks>
    public static void ApplyAsOneEdit(TextEditor editor, IReadOnlyList<TextReplacement> plan)
    {
        if (plan.Count == 0) return;

        var document = editor.Document;
        document.UndoStack.StartUndoGroup();
        try
        {
            for (var i = plan.Count - 1; i >= 0; i--)
                document.Replace(plan[i].Offset, plan[i].Length, plan[i].Text);
        }
        finally
        {
            document.UndoStack.EndUndoGroup();
        }
    }

    /// <summary>Moves the selection to the next or previous match, wrapping at the ends.</summary>
    public void Find(bool forward)
    {
        var term = Term;
        if (term.Length == 0) return;

        var options = CurrentOptions();
        if (_engine.PatternError(term, options) is { } error)
        {
            ShowInvalid(error);
            return;
        }

        var hits = _engine.FindInText(_editor.Text, term, options);
        if (hits.Count == 0)
        {
            _status.Text = Localizer.Get(StringKeys.SearchNoMatches);
            return;
        }

        // With a selection, the search starts past it: the selected match is
        // the one just found, so it must not be found again.
        var selected = _editor.SelectionLength > 0;
        var target = forward
            ? hits.FirstOrDefault(h => h.Offset >= (selected ? _editor.SelectionStart + 1 : _editor.CaretOffset)) ?? hits[0]
            : hits.LastOrDefault(h => h.Offset < (selected ? _editor.SelectionStart : _editor.CaretOffset)) ?? hits[^1];

        _editor.Select(target.Offset, target.Length);
        _editor.ScrollToLine(_editor.Document.GetLineByOffset(target.Offset).LineNumber);
        _status.Text = Localizer.Get(StringKeys.SearchMatchPosition, hits.ToList().IndexOf(target) + 1, hits.Count);
    }

    /// <summary>
    /// Replaces the selected match when the selection is one, then moves on to
    /// the next. With no match selected it only finds the next one.
    /// </summary>
    private void ReplaceOne()
    {
        var options = CurrentOptions();
        if (_engine.PatternError(Term, options) is { } error)
        {
            ShowInvalid(error);
            return;
        }

        if (_editor.SelectionLength > 0)
        {
            var plan = _engine.PlanReplacements(_editor.Text, Term, Replacement, options);
            var current = plan.FirstOrDefault(p => p.Offset == _editor.SelectionStart && p.Length == _editor.SelectionLength);
            if (current is not null) ApplyAsOneEdit(_editor, [current]);
        }

        Find(forward: true);
    }

    private void ReplaceAll()
    {
        var options = CurrentOptions();
        if (_engine.PatternError(Term, options) is { } error)
        {
            ShowInvalid(error);
            return;
        }

        var plan = _engine.PlanReplacements(_editor.Text, Term, Replacement, options);
        if (plan.Count == 0)
        {
            _status.Text = Localizer.Get(StringKeys.SearchNoMatches);
            return;
        }

        ApplyAsOneEdit(_editor, plan);
        _status.Text = Localizer.Get(StringKeys.SearchReplacedMatches, plan.Count);
    }

    /// <summary>Keys the bar and the editor share: F3 steps and Esc closes.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F3 when Term.Length > 0:
                Find(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                break;

            case Key.Escape when IsOpen:
                Close();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Counts the matches as the term or an option changes, without moving the caret.</summary>
    private void RefreshStatus()
    {
        var term = Term;
        if (term.Length == 0)
        {
            _status.Text = "";
            return;
        }

        var options = CurrentOptions();
        if (_engine.PatternError(term, options) is { } error)
        {
            ShowInvalid(error);
            return;
        }

        var count = _engine.FindInText(_editor.Text, term, options).Count;
        _status.Text = count switch
        {
            0 => Localizer.Get(StringKeys.SearchNoMatches),
            1 => Localizer.Get(StringKeys.SearchFileMatchesOne),
            _ => Localizer.Get(StringKeys.SearchFileMatches, count)
        };
    }

    private void ShowInvalid(string error) =>
        _status.Text = Localizer.Get(StringKeys.SearchInvalidPattern, error);

    private SearchOptions CurrentOptions() => new()
    {
        MatchCase = _matchCase.IsChecked == true,
        WholeWord = _wholeWord.IsChecked == true,
        UseRegex = _useRegex.IsChecked == true
    };

    /// <summary>The selection when it is on one line, otherwise the word the caret is in.</summary>
    private string PrefillText()
    {
        if (_editor.SelectionLength > 0)
        {
            var selected = _editor.SelectedText;
            return selected.Contains('\n') || selected.Contains('\r') ? "" : selected;
        }

        var text = _editor.Text;
        var caret = Math.Clamp(_editor.CaretOffset, 0, text.Length);
        var start = caret;
        var end = caret;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        while (end < text.Length && IsWordChar(text[end])) end++;

        return text[start..end];
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static TextBox Input(string placeholder) => new()
    {
        PlaceholderText = placeholder,
        FontSize = PanelMetrics.FontSize,
        MinWidth = 200,
        MinHeight = Spacing.EditorHeight,
        Padding = new Thickness(Spacing.Tight, 2),
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private static Button Icon(string glyph, string tooltipKey)
    {
        var button = new Button
        {
            Content = glyph,
            MinWidth = Spacing.EditorHeight,
            MinHeight = Spacing.EditorHeight,
            FontSize = PanelMetrics.FontSize
        };
        ToolTip.SetTip(button, Localizer.Get(tooltipKey));
        return button;
    }
}
