using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Workspace.Search;

namespace Basalt.Shell.Controls;

/// <summary>A hit as the results list shows it.</summary>
public sealed record SearchResultRow(string FilePath, SearchHit? Hit, bool IsFileHeader)
{
    public string Display => IsFileHeader
        ? Path.GetFileName(FilePath)
        : $"{Hit!.Line,5}: {Hit.LineText.Trim()}";

    public string? Detail => IsFileHeader ? Path.GetDirectoryName(FilePath) : null;
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
    private readonly CheckBox _matchCase;
    private readonly CheckBox _wholeWord;
    private readonly CheckBox _useRegex;
    private readonly TextBox _extensions;
    private readonly ListBox _results;
    private readonly TextBlock _summary;

    private CancellationTokenSource? _running;
    private readonly List<SearchResultRow> _rows = [];

    public SearchPanel()
    {
        _term = new TextBox { PlaceholderText = "Find" };
        _replacement = new TextBox { PlaceholderText = "Replace with" };

        _matchCase = new CheckBox { Content = "Aa", FontSize = 11 };
        _wholeWord = new CheckBox { Content = "Word", FontSize = 11 };
        _useRegex = new CheckBox { Content = ".*", FontSize = 11 };

        _extensions = new TextBox
        {
            PlaceholderText = "File types, e.g. .vb .vbhtml",
            FontSize = 11
        };

        _summary = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.7,
            Margin = new Thickness(2, 4, 2, 4)
        };

        _results = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<SearchResultRow>((row, _) => BuildRow(row))
        };
        _results.DoubleTapped += (_, _) => OpenSelected();

        Content = BuildLayout();

        _term.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;

            e.Handled = true;
            await RunSearchAsync();
        };
    }

    /// <summary>Directory the search covers, normally the solution folder.</summary>
    public string? SearchRoot { get; set; }

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
        var options = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children = { _matchCase, _wholeWord, _useRegex }
        };

        var findButton = new Button { Content = "Find", Classes = { "primary" } };
        findButton.Click += async (_, _) => await RunSearchAsync();

        var replaceButton = new Button { Content = "Replace all" };
        replaceButton.Click += async (_, _) => await RunReplaceAsync();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { findButton, replaceButton }
        };

        var top = new StackPanel
        {
            Margin = new Thickness(12, 8, 12, 6),
            Spacing = 6,
            Children = { _term, _replacement, options, _extensions, buttons, _summary }
        };

        var layout = new DockPanel();
        // Fully qualified: the docking library also declares a "Dock"
        // namespace, which shadows Avalonia's enum of the same name.
        DockPanel.SetDock(top, Avalonia.Controls.Dock.Top);
        layout.Children.Add(top);
        layout.Children.Add(_results);

        return layout;
    }

    private static Control BuildRow(SearchResultRow? row)
    {
        if (row is null) return new TextBlock();

        var text = new TextBlock
        {
            Text = row.Display,
            FontFamily = row.IsFileHeader
                ? FontFamily.Default
                : new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
            FontWeight = row.IsFileHeader ? FontWeight.SemiBold : FontWeight.Normal,
            FontSize = row.IsFileHeader ? 13 : 12
        };

        if (row.Detail is null) return text;

        var detail = new TextBlock
        {
            Text = row.Detail,
            FontSize = 10,
            Opacity = 0.6,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        return new StackPanel { Children = { text, detail } };
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

        return text
            .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .ToList();
    }

    private async Task RunSearchAsync()
    {
        var term = _term.Text;
        var root = SearchRoot;

        if (string.IsNullOrEmpty(term) || root is null || !Directory.Exists(root))
        {
            _summary.Text = root is null ? "Open a solution first." : "";
            return;
        }

        // A search still running is abandoned: its results are for a term the
        // user has already replaced.
        _running?.Cancel();
        _running = new CancellationTokenSource();
        var token = _running.Token;

        _rows.Clear();
        _results.ItemsSource = null;
        _summary.Text = "Searching…";

        var options = CurrentOptions();
        var files = 0;
        var hits = 0;

        try
        {
            await foreach (var file in _engine
                               .SearchDirectoryAsync(root, term, options, token)
                               .ConfigureAwait(true))
            {
                files++;
                hits += file.Hits.Count;

                _rows.Add(new SearchResultRow(file.FilePath, null, IsFileHeader: true));
                foreach (var hit in file.Hits)
                    _rows.Add(new SearchResultRow(file.FilePath, hit, IsFileHeader: false));

                // Rebinding as results arrive lets the first files appear while
                // the rest of the solution is still being read.
                _results.ItemsSource = _rows.ToList();
                _summary.Text = $"{hits} in {files} files…";
            }

            _summary.Text = hits == 0
                ? "No results."
                : $"{hits} results in {files} files.";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search.
        }
    }

    private async Task RunReplaceAsync()
    {
        var term = _term.Text;
        var root = SearchRoot;

        if (string.IsNullOrEmpty(term) || root is null || !Directory.Exists(root)) return;

        var changed = await _engine
            .ReplaceInDirectoryAsync(root, term, _replacement.Text ?? "", CurrentOptions())
            .ConfigureAwait(true);

        _summary.Text = $"Replaced in {changed} files.";

        // The old results point at text that is no longer there.
        _rows.Clear();
        _results.ItemsSource = null;
    }

    private void OpenSelected()
    {
        if (_results.SelectedItem is not SearchResultRow { IsFileHeader: false, Hit: { } hit })
            return;

        HitActivated?.Invoke(this, hit);
    }
}
