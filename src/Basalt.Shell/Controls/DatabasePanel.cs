using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using Basalt.Core.Localization;
using Basalt.Data;
using Basalt.Shell.Syntax;

namespace Basalt.Shell.Controls;

/// <summary>
/// Looking at a database without leaving the IDE.
/// </summary>
/// <remarks>
/// An application that stores anything has a database behind it, and checking
/// what is actually in a table meant a second tool and a second window. The
/// panel is deliberately small — connect, look at the tables, run a query,
/// read the rows — because that is what interrupts the work, and anything
/// more is a job for a database tool proper.
///
/// The query box is a real editor with the SQL grammar the registry already
/// ships, so it is coloured the same way every other language in the IDE is
/// rather than by something written for this one panel.
/// </remarks>
public sealed class DatabasePanel : UserControl, IDisposable
{
    private readonly DatabaseClient _client = new();

    private readonly TreeView _tables = new()
    {
        Background = Brushes.Transparent,
        BorderThickness = default,
    };

    private readonly TextEditor _query;
    private readonly DataGrid _results;
    private readonly TextBlock _status;
    private readonly Button _run;
    private readonly Button _stop;

    private CancellationTokenSource? _running;

    /// <summary>Raised when the panel wants a database file chosen.</summary>
    public event EventHandler? OpenRequested;

    public DatabasePanel()
    {
        _query = new TextEditor
        {
            FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
            FontSize = 12,
            ShowLineNumbers = false,
            MinHeight = 70,
            Text = "SELECT * FROM ",
        };

        // The grammar the registry already has, reached the same way every
        // other language is: a name that ends in .sql.
        TextMateHighlighting.InstallFor(_query, "query.sql");

        _results = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            FontSize = 12,
        };

        _status = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.7,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            TextWrapping = TextWrapping.Wrap,
        };

        _run = new Button { Content = Localizer.Get(StringKeys.DatabaseRun), FontSize = 12 };
        _run.Click += (_, _) => _ = RunAsync();

        // Only while something is running: a stop button with nothing to stop
        // is one the user has to work out the meaning of.
        _stop = new Button
        {
            Content = Localizer.Get(StringKeys.DatabaseStop),
            FontSize = 12,
            IsVisible = false,
        };

        _stop.Click += (_, _) => _running?.Cancel();

        var open = new Button { Content = Localizer.Get(StringKeys.DatabaseOpen), FontSize = 12 };
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            Children = { open, _run, _stop },
        };

        // Tables on the left, query above results on the right: the shape
        // every database tool uses, so nobody has to learn this one.
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };

        Grid.SetRow(bar, 0);
        Grid.SetRow(_query, 1);
        Grid.SetRow(_results, 2);
        Grid.SetRow(_status, 3);

        right.Children.Add(bar);
        right.Children.Add(_query);
        right.Children.Add(_results);
        right.Children.Add(_status);

        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("200,4,*") };

        Grid.SetColumn(_tables, 0);
        Grid.SetColumn(right, 2);

        split.Children.Add(_tables);
        split.Children.Add(new GridSplitter { Width = 4, [Grid.ColumnProperty] = 1 });
        split.Children.Add(right);

        Content = split;

        ShowNothingOpen();
    }

    /// <summary>What the panel is connected to, if anything.</summary>
    public string? Source => _client.Source;

    /// <summary>What the status line says. For tests.</summary>
    internal string StatusText => _status.Text ?? "";

    /// <summary>The tables listed. For tests.</summary>
    internal IReadOnlyList<string> TableNames =>
        _tables.ItemsSource?.OfType<TableNode>().Select(t => t.Name).ToList() ?? [];

    /// <summary>The query as typed. For tests.</summary>
    internal string Query
    {
        get => _query.Text;
        set => _query.Text = value;
    }

    /// <summary>Opens a database and lists what is in it.</summary>
    public async Task OpenAsync(string path)
    {
        try
        {
            await _client.OpenAsync(path).ConfigureAwait(true);

            await RefreshTablesAsync().ConfigureAwait(true);

            _status.Text = Localizer.Get(StringKeys.DatabaseOpened, Path.GetFileName(path));
            _run.IsEnabled = true;
        }
        catch (Exception ex)
        {
            // A file that is not a database, or one that has moved: said
            // rather than thrown, since the panel has to stay usable.
            _status.Text = ex.Message;
            ShowNothingOpen();
        }
    }

    private async Task RefreshTablesAsync()
    {
        var tables = await _client.GetTablesAsync().ConfigureAwait(true);

        _tables.ItemsSource = tables.Select(t => new TableNode(t)).ToList();

        _tables.ItemTemplate = new Avalonia.Controls.Templates.FuncTreeDataTemplate<object>(
            (item, _) => new TextBlock
            {
                Text = item?.ToString() ?? "",
                FontSize = 12,
                Margin = new Thickness(0, Spacing.Hairline),
            },
            item => (item as TableNode)?.Columns ?? []);
    }

    /// <summary>
    /// Runs whatever is in the query box.
    /// </summary>
    /// <remarks>
    /// Cancellable, and the previous run is cancelled first: pressing Run
    /// twice should replace the answer, not queue a second one behind a query
    /// that may take minutes.
    /// </remarks>
    private async Task RunAsync()
    {
        if (!_client.IsOpen) return;

        _running?.Cancel();
        _running = new CancellationTokenSource();

        var token = _running.Token;

        _run.IsEnabled = false;
        _stop.IsVisible = true;

        try
        {
            var result = await _client.ExecuteAsync(_query.Text, token).ConfigureAwait(true);

            Show(result);
        }
        catch (OperationCanceledException)
        {
            _status.Text = Localizer.Get(StringKeys.DatabaseStopped);
        }
        catch (Exception ex)
        {
            // While typing, a query is invalid far more often than not: the
            // message is the answer, not a failure of the panel.
            _results.ItemsSource = null;
            _results.Columns.Clear();
            _status.Text = ex.Message;
        }
        finally
        {
            _run.IsEnabled = true;
            _stop.IsVisible = false;
            _running = null;
        }
    }

    /// <summary>Puts a result into the grid.</summary>
    private void Show(QueryResult result)
    {
        _results.Columns.Clear();

        if (result.IsCount)
        {
            // An UPDATE is an answer too; a blank grid after one reads as a
            // query that did nothing.
            _results.ItemsSource = null;

            _status.Text = Localizer.Get(
                StringKeys.DatabaseAffected, result.Affected, Milliseconds(result));

            return;
        }

        for (var i = 0; i < result.Columns.Count; i++)
        {
            var at = i;

            _results.Columns.Add(new DataGridTemplateColumn
            {
                Header = result.Columns[i].Name,
                CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<IReadOnlyList<string?>>(
                    (_, _) => new TextBlock
                    {
                        FontSize = 12,
                        Margin = new Thickness(Spacing.Tight, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        [!TextBlock.TextProperty] = new Avalonia.Data.Binding($"[{at}]"),
                    },
                    supportsRecycling: true),
            });
        }

        _results.ItemsSource = result.Rows;

        _status.Text = result.Rows.Count >= DatabaseClient.MaximumRows
            ? Localizer.Get(StringKeys.DatabaseCapped, DatabaseClient.MaximumRows, Milliseconds(result))
            : Localizer.Get(StringKeys.DatabaseRows, result.Rows.Count, Milliseconds(result));
    }

    private static long Milliseconds(QueryResult result) => (long)result.Took.TotalMilliseconds;

    private void ShowNothingOpen()
    {
        _tables.ItemsSource = null;
        _results.ItemsSource = null;
        _results.Columns.Clear();
        _run.IsEnabled = false;

        if (_status.Text is not { Length: > 0 })
            _status.Text = Localizer.Get(StringKeys.DatabaseNothingOpen);
    }

    /// <summary>A table, with its columns as children.</summary>
    private sealed class TableNode(TableInfo table)
    {
        public string Name => table.Name;

        public IReadOnlyList<string> Columns =>
            [.. table.Columns.Select(c => $"{c.Name}  {c.Type}")];

        public override string ToString() => table.Name;
    }

    public void Dispose()
    {
        _running?.Cancel();
        _running?.Dispose();

        _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
