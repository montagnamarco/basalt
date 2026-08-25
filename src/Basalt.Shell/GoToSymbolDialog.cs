using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Extensibility;

namespace Basalt.Shell;

/// <summary>A symbol offered in the jump list.</summary>
public sealed record SymbolChoice(string Name, SymbolKind Kind, string FilePath, int Line)
{
    public string Detail => $"{Path.GetFileName(FilePath)}:{Line}";
}

/// <summary>
/// Jumping to a symbol by typing part of its name.
///
/// Built in code rather than XAML: it is one box and one list, and building it
/// here keeps the filtering and the layout in the same place.
/// </summary>
public sealed class GoToSymbolDialog : Window
{
    private readonly TextBox _query;
    private readonly ListBox _list;
    private readonly TextBlock _summary;

    private IReadOnlyList<SymbolChoice> _all = [];
    private readonly Func<string, Task<IReadOnlyList<SymbolChoice>>>? _search;

    private CancellationTokenSource? _running;

    /// <summary>
    /// Jumps within a fixed set of symbols, filtered as the user types.
    /// </summary>
    public GoToSymbolDialog(IReadOnlyList<SymbolChoice> symbols, string title)
        : this(title)
    {
        _all = symbols;
        ApplyFilter("");
    }

    /// <summary>
    /// Jumps across a solution, asking for symbols as the query changes.
    /// </summary>
    public GoToSymbolDialog(
        Func<string, Task<IReadOnlyList<SymbolChoice>>> search, string title)
        : this(title)
    {
        _search = search;
    }

    private GoToSymbolDialog(string title)
    {
        Title = title;
        Width = 620;
        Height = 420;

        // A floor, so resizing cannot hide what matters.
        MinWidth = 420; MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _query = new TextBox { PlaceholderText = "Type part of a name" };
        _query.TextChanged += async (_, _) => await OnQueryChangedAsync();
        _query.KeyDown += OnQueryKeyDown;

        _summary = new TextBlock { FontSize = 11, Opacity = 0.7 };

        _list = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<SymbolChoice>((choice, _) => BuildRow(choice))
        };
        _list.DoubleTapped += (_, _) => Accept();

        var top = new StackPanel
        {
            Margin = new Thickness(16, 16, 16, 8),
            Spacing = 6,
            Children = { _query, _summary }
        };

        var layout = new DockPanel();
        DockPanel.SetDock(top, Avalonia.Controls.Dock.Top);
        layout.Children.Add(top);
        layout.Children.Add(_list);

        Content = layout;

        Opened += (_, _) => _query.Focus();
    }

    /// <summary>What the list currently offers, exposed for tests.</summary>
    internal IReadOnlyList<SymbolChoice> Choices =>
        _list.ItemsSource?.Cast<SymbolChoice>().ToList() ?? [];

    /// <summary>Filters as if the user had typed, for tests.</summary>
    internal void FilterForTests(string query) => ApplyFilter(query);

    /// <summary>The search box, so a test can raise a real key event on it.</summary>
    internal TextBox QueryBoxForTests => _query;

    /// <summary>Which entry is highlighted, for tests.</summary>
    internal int SelectedIndexForTests => _list.SelectedIndex;

    private static Control BuildRow(SymbolChoice? choice)
    {
        if (choice is null) return new TextBlock();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        row.Children.Add(new Image
        {
            Source = Controls.CompletionIcons.For(choice.Kind),
            Width = 14,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center
        });

        row.Children.Add(new TextBlock
        {
            Text = choice.Name,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });

        row.Children.Add(new TextBlock
        {
            Text = choice.Detail,
            FontSize = 11,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        });

        return row;
    }

    private async Task OnQueryChangedAsync()
    {
        var query = _query.Text ?? "";

        if (_search is null)
        {
            ApplyFilter(query);
            return;
        }

        // Solution-wide search runs per keystroke, so an older one is abandoned
        // rather than allowed to overwrite newer results.
        _running?.Cancel();
        _running = new CancellationTokenSource();

        if (query.Length < 2)
        {
            _all = [];
            ApplyFilter(query);
            return;
        }

        try
        {
            _all = await _search(query).ConfigureAwait(true);
            ApplyFilter(query);
        }
        catch (OperationCanceledException)
        {
            // Superseded.
        }
    }

    /// <summary>
    /// Narrows the list to names containing the query.
    ///
    /// Names starting with the query sort first: typing "Cust" should offer
    /// "Customer" before "AccountCustomer".
    /// </summary>
    private void ApplyFilter(string query)
    {
        var matches = string.IsNullOrWhiteSpace(query)
            ? _all
            : _all
                .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .ThenBy(s => s.Name.Length)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        var shown = matches.Take(200).ToList();

        _list.ItemsSource = shown;
        if (shown.Count > 0) _list.SelectedIndex = 0;

        _summary.Text = matches.Count > shown.Count
            ? $"{shown.Count} of {matches.Count} shown"
            : $"{shown.Count} symbols";
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        // Arrow keys move through the list while the caret stays in the box.
        switch (e.Key)
        {
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;

            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                Accept();
                e.Handled = true;
                break;

            case Key.Escape:
                Close(null);
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        var count = Choices.Count;
        if (count == 0) return;

        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + delta, 0, count - 1);
        _list.ScrollIntoView(_list.SelectedIndex);
    }

    private void Accept() => Close(_list.SelectedItem as SymbolChoice);
}
