using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Workspace.Search;

namespace Basalt.Shell.Controls;

/// <summary>
/// A hit as the results list shows it: a file header carrying how many
/// matches the file has, or one matching line.
/// </summary>
public sealed record SearchResultRow(
    string FilePath, SearchHit? Hit, bool IsFileHeader, int MatchCount = 0, string? Folder = null)
{
    public string Display => IsFileHeader
        ? Path.GetFileName(FilePath)
        : $"{Hit!.Line,5}: {Hit.LineText.Trim()}";

    /// <summary>Where the file is: relative to the searched folder when known.</summary>
    public string? Detail => IsFileHeader ? Folder ?? Path.GetDirectoryName(FilePath) : null;
}

/// <summary>
/// Searching across the open solution, with the results grouped by file.
///
/// Searching runs off the UI thread and reports files as they are found, so a
/// large solution shows its first results immediately rather than after the
/// whole sweep.
/// </summary>
public sealed class SearchPanel : UserControl
{
    private readonly SearchEngine _engine = new();

    private readonly TextBox _term;
    private readonly TextBox _replacement;
    private readonly ToggleButton _matchCase;
    private readonly ToggleButton _wholeWord;
    private readonly ToggleButton _useRegex;
    private readonly Button _findButton;
    private readonly Button _replaceButton;
    private readonly Button _cancelButton;
    private readonly ComboBox _scope;
    private readonly ContentControl _body = new();
    private readonly EmptyState _hint;
    private readonly EmptyState _noResults;
    private readonly TextBox _extensions;
    private readonly ListBox _results;
    private readonly TextBlock _summary;

    private CancellationTokenSource? _running;
    private readonly List<SearchResultRow> _rows = [];

    // The editor's font, as the Output panel uses it: matched lines are code,
    // and read against the editor they came from.
    private FontFamily _font = new(new Basalt.Core.Settings.EditorSettings().FontFamily);

    public SearchPanel()
    {
        _term = Input(Localizer.Get(StringKeys.SearchFind));
        _replacement = Input(Localizer.Get(StringKeys.SearchReplaceWith));
        _extensions = Input(Localizer.Get(StringKeys.SearchFileTypes));

        _matchCase = PanelToolbar.TextToggle("Aa", Localizer.Get(StringKeys.SearchMatchCase));
        _wholeWord = PanelToolbar.TextToggle("ab", Localizer.Get(StringKeys.SearchWholeWord));
        _useRegex = PanelToolbar.TextToggle(".*", Localizer.Get(StringKeys.SearchUseRegex));

        _findButton = new Button { Content = Localizer.Get(StringKeys.SearchFind), Classes = { "primary" } };
        _findButton.Click += async (_, _) => await RunSearchAsync();

        _replaceButton = new Button { Content = Localizer.Get(StringKeys.SearchReplaceAll) };
        _replaceButton.Click += async (_, _) => await RunReplaceAsync();

        // Cancel takes the place of Find while a search runs, so a large solution
        // can be stopped without waiting for it to finish.
        _cancelButton = new Button { Content = Localizer.Get(StringKeys.ButtonCancel), IsVisible = false };
        _cancelButton.Click += (_, _) => _running?.Cancel();

        _scope = new ComboBox
        {
            ItemsSource = new[] { Localizer.Get(StringKeys.SearchScopeSolution), Localizer.Get(StringKeys.SearchScopeProject) },
            SelectedIndex = 0,
            FontSize = PanelMetrics.FontSize,
            MinHeight = Spacing.EditorHeight,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        foreach (var button in new[] { _findButton, _replaceButton, _cancelButton })
        {
            button.MinHeight = Spacing.EditorHeight;
            button.MinWidth = 88;
            button.FontSize = PanelMetrics.FontSize;
            button.Padding = new Thickness(Spacing.Normal, 2);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
        }

        _summary = new TextBlock
        {
            FontSize = PanelMetrics.SecondaryFontSize,
            TextTrimming = TextTrimming.CharacterEllipsis
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        _results = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<SearchResultRow>((row, _) => BuildRow(row))
        };
        _results.DoubleTapped += (_, _) => OpenSelected();
        _results.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;

            OpenSelected();
            e.Handled = true;
        };

        _hint = new EmptyState(Localizer.Get(StringKeys.SearchEmpty), IconKind.Find);
        _noResults = new EmptyState(Localizer.Get(StringKeys.SearchNoResults), IconKind.Find);
        _body.Content = _hint;

        Content = BuildLayout();

        _term.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;

            e.Handled = true;
            await RunSearchAsync();
        };
    }

    private static TextBox Input(string placeholder) => new()
    {
        PlaceholderText = placeholder,
        FontSize = PanelMetrics.FontSize,
        MinHeight = Spacing.EditorHeight,
        Padding = new Thickness(Spacing.Tight, 2),
        VerticalContentAlignment = VerticalAlignment.Center
    };

    /// <summary>Directory the search covers, normally the solution folder.</summary>
    public string? SearchRoot { get; set; }

    /// <summary>
    /// The folder of the project the user is working in. The "Current project"
    /// scope searches this folder; without one the scope is the solution.
    /// </summary>
    public string? ProjectRoot { get; set; }

    /// <summary>
    /// The editor showing a file, when one is open. A replacement changes an
    /// open file in its editor, as one undoable edit, and leaves the disk alone
    /// until the user saves; a closed file is rewritten on disk.
    /// </summary>
    public Func<string, AvaloniaEdit.TextEditor?>? EditorFor { get; set; }

    /// <summary>The paths of the files open in the IDE, whether or not they have unsaved edits.</summary>
    public Func<IEnumerable<string>>? OpenFilePaths { get; set; }

    /// <summary>
    /// The text of each open file as its editor holds it, keyed by full path.
    /// Taken on the interface thread because editors can be read only there;
    /// the search then runs off it, so an unsaved edit is found and replaced.
    /// </summary>
    private Dictionary<string, string> UnsavedTexts()
    {
        // Windows and macOS file names ignore case, so an editor opened as
        // "program.vb" must still stand for "Program.vb" found on disk.
        var texts = new Dictionary<string, string>(
            OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        if (OpenFilePaths is null || EditorFor is null) return texts;

        foreach (var path in OpenFilePaths())
            if (EditorFor(path) is { } editor)
                texts[Path.GetFullPath(path)] = editor.Text;

        return texts;
    }

    /// <summary>The folder the search covers for the chosen scope.</summary>
    private string? ScopeRoot() =>
        _scope.SelectedIndex == 1 && ProjectRoot is { } project && Directory.Exists(project)
            ? project
            : SearchRoot;

    /// <summary>The font the matched lines are drawn in.</summary>
    internal FontFamily Font => _font;

    /// <summary>
    /// Follows the editor's font from Settings. The rows on screen are built
    /// again, because each one carries the font it was made with.
    /// </summary>
    public void ApplySettings(Basalt.Core.Settings.IdeSettings settings)
    {
        _font = new FontFamily(settings.Editor.FontFamily);

        if (_results.ItemsSource is not null) _results.ItemsSource = _rows.ToList();
    }

    /// <summary>Raised when the user picks a hit to open.</summary>
    public event EventHandler<SearchHit>? HitActivated;

    /// <summary>
    /// The results as they currently stand.
    ///
    /// Exposed so the panel can be checked without rendering it: a headless
    /// test has no layout pass, so the list's own items are unreachable.
    /// </summary>
    internal IReadOnlyList<SearchResultRow> Results => _rows.ToList();

    /// <summary>What the panel is telling the user about the last search.</summary>
    internal string Summary => _summary.Text ?? "";

    /// <summary>The controls the tests read the visible text of.</summary>
    internal Button FindButton => _findButton;
    internal Button ReplaceAllButton => _replaceButton;
    internal Button CancelButton => _cancelButton;
    internal ComboBox ScopeBox => _scope;
    internal TextBox TermBox => _term;
    internal TextBox ReplacementBox => _replacement;
    internal TextBox ExtensionsBox => _extensions;
    internal ToggleButton MatchCaseToggle => _matchCase;
    internal ToggleButton WholeWordToggle => _wholeWord;
    internal ToggleButton RegexToggle => _useRegex;
    internal ListBox ResultsList => _results;

    /// <summary>Which of the three bodies the panel shows, for the tests.</summary>
    internal bool IsShowingHint => ReferenceEquals(_body.Content, _hint);
    internal bool IsShowingNoResults => ReferenceEquals(_body.Content, _noResults);

    /// <summary>Reports a hit as if the user had picked it.</summary>
    internal void ActivateForTests(SearchResultRow row)
    {
        if (row is { IsFileHeader: false, Hit: { } hit }) HitActivated?.Invoke(this, hit);
    }

    /// <summary>Puts the caret in the search box, with the term selected.</summary>
    public void FocusSearchBox()
    {
        _term.Focus();
        _term.SelectAll();
    }

    /// <summary>Starts a search for the given term, used by Find in Files.</summary>
    public async Task SearchForAsync(string term)
    {
        _term.Text = term;
        await RunSearchAsync();
    }

    private Control BuildLayout()
    {
        // Two rows of an input and the button that acts on it, so the
        // buttons line up in one column whatever the panel's width.
        var inputs = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = Spacing.Tight,
            RowSpacing = Spacing.Tight
        };

        Place(inputs, _term, 0, 0);
        Place(inputs, _findButton, 0, 1);
        Place(inputs, _cancelButton, 0, 1);
        Place(inputs, _replacement, 1, 0);
        Place(inputs, _replaceButton, 1, 1);

        var options = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = Spacing.Tight };
        var toggles = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Hairline * 2,
            Children = { _matchCase, _wholeWord, _useRegex }
        };
        Grid.SetColumn(_extensions, 1);
        options.Children.Add(toggles);
        options.Children.Add(_extensions);
        Grid.SetColumn(_scope, 2);
        options.Children.Add(_scope);

        var top = new StackPanel
        {
            Margin = new Thickness(Spacing.Normal, Spacing.Normal, Spacing.Normal, Spacing.Tight),
            Spacing = Spacing.Tight,
            Children = { inputs, options, _summary }
        };

        var header = new Border { Child = top, BorderThickness = new Thickness(0, 0, 0, 1) };
        header.Themed(Border.BorderBrushProperty, "PanelBorderBrush");
        // Fully qualified: the docking library also declares a "Dock"
        // namespace, which shadows Avalonia's enum of the same name.
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);

        return new DockPanel { Children = { header, _body } };
    }

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private Control BuildRow(SearchResultRow? row)
    {
        if (row is null) return new TextBlock();

        return row.IsFileHeader ? BuildFileRow(row) : BuildHitRow(row);
    }

    private static Control BuildFileRow(SearchResultRow row)
    {
        var name = new TextBlock
        {
            Text = row.Display,
            FontWeight = FontWeight.SemiBold,
            FontSize = PanelMetrics.FontSize,
            VerticalAlignment = VerticalAlignment.Center
        };

        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Normal,
            Children = { name }
        };

        if (!string.IsNullOrEmpty(row.Detail))
        {
            line.Children.Add(new TextBlock
            {
                Text = row.Detail,
                FontSize = PanelMetrics.SecondaryFontSize,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush"));
        }

        var count = new TextBlock
        {
            Text = row.MatchCount == 1
                ? Localizer.Get(StringKeys.SearchFileMatchesOne)
                : Localizer.Get(StringKeys.SearchFileMatches, row.MatchCount),
            FontSize = PanelMetrics.SecondaryFontSize,
            VerticalAlignment = VerticalAlignment.Center
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        var layout = new DockPanel { Margin = new Thickness(0, 2) };
        DockPanel.SetDock(count, Avalonia.Controls.Dock.Right);
        layout.Children.Add(count);
        layout.Children.Add(line);

        return layout;
    }

    private Control BuildHitRow(SearchResultRow row)
    {
        var hit = row.Hit!;

        var number = new TextBlock
        {
            Text = hit.Line.ToString(),
            FontSize = PanelMetrics.SecondaryFontSize,
            Width = 44,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Spacing.Normal, 0)
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { number, HighlightedLine(hit) }
        };
    }

    /// <summary>
    /// The matching line, trimmed, with the matched text on a highlight.
    ///
    /// Without it the user reads the whole line for the term. The highlight
    /// is the theme's focus colour, translucent, so the text stays legible
    /// on every theme.
    /// </summary>
    private TextBlock HighlightedLine(SearchHit hit)
    {
        var text = hit.LineText.TrimEnd('\r', '\n');
        var indent = text.Length - text.TrimStart().Length;
        var visible = text.Trim();

        var start = Math.Clamp(hit.Column - 1 - indent, 0, visible.Length);
        var length = Math.Clamp(hit.Length, 0, visible.Length - start);

        var block = new TextBlock
        {
            FontFamily = _font,
            FontSize = PanelMetrics.FontSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        block.Inlines!.Add(new Run(visible[..start]));

        var match = new Run(visible.Substring(start, length)) { FontWeight = FontWeight.SemiBold };
        match.Bind(Inline.BackgroundProperty, block.GetResourceObservable(
            "FocusBorder", value => value is Color color ? new SolidColorBrush(color, 0.35) : (IBrush?)null));
        block.Inlines.Add(match);

        block.Inlines.Add(new Run(visible[(start + length)..]));

        return block;
    }

    private SearchOptions CurrentOptions() => new()
    {
        MatchCase = _matchCase.IsChecked == true,
        WholeWord = _wholeWord.IsChecked == true,
        UseRegex = _useRegex.IsChecked == true,
        IncludeExtensions = ParseExtensions(_extensions.Text)
    };

    private static IReadOnlyList<string> ParseExtensions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        // "*.vb" is how people write a file type, so the glob's star is dropped:
        // without that the suffix became ".*.vb" and matched no file at all.
        return text
            .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.TrimStart('*'))
            .Where(e => e.Length > 0)
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .ToList();
    }

    private async Task RunSearchAsync()
    {
        var term = _term.Text;
        var root = ScopeRoot();

        if (string.IsNullOrEmpty(term) || root is null || !Directory.Exists(root))
        {
            _summary.Text = root is null ? Localizer.Get(StringKeys.SearchOpenSolutionFirst) : "";
            return;
        }

        // A pattern that does not parse cannot find anything, and saying why is
        // more use than an empty list that looks like "no matches".
        if (_engine.PatternError(term, CurrentOptions()) is { } error)
        {
            _rows.Clear();
            _results.ItemsSource = null;
            _body.Content = _hint;
            _summary.Text = Localizer.Get(StringKeys.SearchInvalidPattern, error);
            return;
        }

        // A search still running is abandoned: its results are for a term the
        // user has already replaced.
        _running?.Cancel();
        var running = _running = new CancellationTokenSource();
        var token = running.Token;
        SetRunning(true);

        _rows.Clear();
        _results.ItemsSource = null;
        _summary.Text = Localizer.Get(StringKeys.SearchSearching);

        var options = CurrentOptions();
        var files = 0;
        var hits = 0;

        try
        {
            await foreach (var file in _engine
                               .SearchFilesAsync(root, term, options, UnsavedTexts(), token)
                               .ConfigureAwait(true))
            {
                files++;
                hits += file.Hits.Count;

                _rows.Add(new SearchResultRow(file.FilePath, null, IsFileHeader: true,
                    file.Hits.Count, RelativeFolder(file.FilePath, root)));
                foreach (var hit in file.Hits)
                    _rows.Add(new SearchResultRow(file.FilePath, hit, IsFileHeader: false));

                // Rebinding as results arrive lets the first files appear while
                // the rest of the solution is still being read.
                _results.ItemsSource = _rows.ToList();
                _body.Content = _results;
                _summary.Text = Localizer.Get(StringKeys.SearchProgress, hits, files);
            }

            ShowOutcome(hits, files);
        }
        catch (OperationCanceledException)
        {
            // Stopped by Cancel, or superseded by a newer search. A superseded
            // search must not describe itself over the one that replaced it.
            if (ReferenceEquals(_running, running)) ShowStopped(hits, files);
        }
        finally
        {
            if (ReferenceEquals(_running, running)) SetRunning(false);
        }
    }

    /// <summary>Says what a stopped search had found, so the summary never claims it is still going.</summary>
    private void ShowStopped(int hits, int files)
    {
        _summary.Text = Localizer.Get(StringKeys.SearchCancelled, hits, files);
        _body.Content = hits == 0 ? _noResults : _results;
    }

    /// <summary>Find while idle; Cancel while a search runs.</summary>
    private void SetRunning(bool running)
    {
        _findButton.IsVisible = !running;
        _cancelButton.IsVisible = running;
    }

    /// <summary>
    /// What the panel says about the results so far. Used both when a search
    /// runs to the end and when it is stopped part way.
    /// </summary>
    private void ShowOutcome(int hits, int files)
    {
        _summary.Text = hits == 0
            ? Localizer.Get(StringKeys.SearchNoResults)
            : Localizer.Get(StringKeys.SearchResults, hits, files);

        _body.Content = hits == 0 ? _noResults : _results;
    }

    /// <summary>The file's folder relative to the searched one; null at the root, where there is nothing to add.</summary>
    private static string? RelativeFolder(string filePath, string root)
    {
        var folder = Path.GetDirectoryName(filePath);
        if (folder is null) return null;

        var relative = Path.GetRelativePath(root, folder);

        return relative == "." ? "" : relative.StartsWith("..", StringComparison.Ordinal) ? folder : relative;
    }

    private async Task RunReplaceAsync()
    {
        var term = _term.Text;
        var root = ScopeRoot();

        if (string.IsNullOrEmpty(term) || root is null || !Directory.Exists(root)) return;

        var options = CurrentOptions();
        if (_engine.PatternError(term, options) is { } error)
        {
            _summary.Text = Localizer.Get(StringKeys.SearchInvalidPattern, error);
            return;
        }

        var replacement = _replacement.Text ?? "";
        var changed = 0;

        // Each file is changed where it is open, in its editor, and on disk only
        // when it is not: the editor holds the text the user is working on.
        await foreach (var file in _engine.SearchFilesAsync(root, term, options, UnsavedTexts()).ConfigureAwait(true))
        {
            if (EditorFor?.Invoke(file.FilePath) is { } editor)
            {
                var plan = _engine.PlanReplacements(editor.Text, term, replacement, options);
                if (plan.Count == 0) continue;

                EditorFindBar.ApplyAsOneEdit(editor, plan);
                changed++;
                continue;
            }

            if (await _engine.ReplaceInFileAsync(file.FilePath, term, replacement, options).ConfigureAwait(true))
                changed++;
        }

        _summary.Text = Localizer.Get(StringKeys.SearchReplaced, changed);

        // The old results point at text that is no longer there.
        _rows.Clear();
        _results.ItemsSource = null;
        _body.Content = _hint;
    }

    private void OpenSelected()
    {
        if (_results.SelectedItem is not SearchResultRow { IsFileHeader: false, Hit: { } hit })
            return;

        HitActivated?.Invoke(this, hit);
    }
}
