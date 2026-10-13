using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
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
    private readonly Button _refresh;
    private readonly Button _disconnect;
    private readonly EmptyState _noTables;

    private CancellationTokenSource? _running;

    /// <summary>The run the panel last started, finished or not: a disconnect waits for it.</summary>
    private Task? _runningQuery;

    /// <summary>Raised when the panel wants a database file chosen.</summary>
    public event EventHandler? OpenRequested;

    public DatabasePanel()
    {
        _query = new TextEditor
        {
            FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
            FontSize = PanelMetrics.FontSize,
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
            FontSize = PanelMetrics.FontSize,
        };

        _status = new TextBlock
        {
            FontSize = PanelMetrics.SecondaryFontSize,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            TextWrapping = TextWrapping.Wrap,
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        _run = new Button { Content = Localizer.Get(StringKeys.DatabaseRun), FontSize = PanelMetrics.FontSize };
        _run.Click += (_, _) => _ = StartRunAsync();

        // Only while something is running: a stop button with nothing to stop
        // is one the user has to work out the meaning of.
        _stop = new Button
        {
            Content = Localizer.Get(StringKeys.DatabaseStop),
            FontSize = PanelMetrics.FontSize,
            IsVisible = false,
        };

        _stop.Click += (_, _) => _running?.Cancel();

        // Only while something is open: Refresh and Disconnect act on a
        // database, and with none they would do nothing the user could see.
        _refresh = new Button { Content = Localizer.Get(StringKeys.DatabaseRefresh), FontSize = PanelMetrics.FontSize };
        _refresh.Click += (_, _) => _ = RefreshAsync();

        _disconnect = new Button { Content = Localizer.Get(StringKeys.DatabaseDisconnect), FontSize = PanelMetrics.FontSize };
        _disconnect.Click += (_, _) => _ = DisconnectAsync();

        var open = new Button { Content = Localizer.Get(StringKeys.DatabaseOpen), FontSize = PanelMetrics.FontSize };
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            Children = { open, _refresh, _disconnect, _run, _stop },
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

        // The tables column says so when no database is open, instead of being
        // an empty white box beside the query.
        _noTables = new EmptyState("", IconKind.Information);
        var left = new Grid { Children = { _tables, _noTables } };

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);

        split.Children.Add(left);
        split.Children.Add(new GridSplitter { Width = 4, [Grid.ColumnProperty] = 1 });
        split.Children.Add(right);

        // Double-clicking a table reads its rows: the question a person asks of
        // a table is usually "what is in it", and typing SELECT * FROM first is
        // the step this skips.
        _tables.DoubleTapped += (_, e) =>
        {
            // The row that was tapped, found from the element that took the
            // tap: the selection is not a reliable stand-in for it.
            var tapped = (e.Source as Visual)?.GetSelfAndVisualAncestors()
                .OfType<TreeViewItem>().FirstOrDefault()?.DataContext as TableNode;

            if ((tapped ?? _tables.SelectedItem as TableNode) is { } table) _ = ShowRowsOfAsync(table.Name);
        };

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
            _refresh.IsEnabled = true;
            _disconnect.IsEnabled = true;
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
        _noTables.IsVisible = tables.Count == 0;
        _noTables.Message = Localizer.Get(StringKeys.DatabaseNoTables);

        _tables.ItemTemplate = new Avalonia.Controls.Templates.FuncTreeDataTemplate<object>(
            (item, _) => new TextBlock
            {
                Text = item?.ToString() ?? "",
                FontSize = PanelMetrics.FontSize,
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
    ///
    /// A run is tracked by identity. A run that was replaced may still finish
    /// after the new one has started; it must neither show its answer over the
    /// new one nor hide the Stop of the run that is still going.
    /// </remarks>
    private async Task RunAsync()
    {
        if (!_client.IsOpen) return;

        _running?.Cancel();

        var run = new CancellationTokenSource();
        _running = run;

        _run.IsEnabled = false;
        _stop.IsVisible = true;

        try
        {
            var result = await _client.ExecuteAsync(_query.Text, run.Token).ConfigureAwait(true);

            if (IsCurrent(run)) Show(result);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(run)) _status.Text = Localizer.Get(StringKeys.DatabaseStopped);
        }
        catch (Exception ex)
        {
            // While typing, a query is invalid far more often than not: the
            // message is the answer, not a failure of the panel. A replaced
            // run's failure is nobody's answer, so it is dropped rather than
            // thrown out of a fire-and-forget task.
            if (!IsCurrent(run)) return;

            _results.ItemsSource = null;
            _results.Columns.Clear();
            _status.Text = ex.Message;
        }
        finally
        {
            if (IsCurrent(run))
            {
                _run.IsEnabled = true;
                _stop.IsVisible = false;
                _running = null;
            }
        }
    }

    /// <summary>Whether this run is the one the panel is waiting on, rather than one it has replaced.</summary>
    private bool IsCurrent(CancellationTokenSource run) => ReferenceEquals(_running, run);

    /// <summary>Rereads the tables, so one created outside the panel since it was opened is listed.</summary>
    private async Task RefreshAsync()
    {
        if (!_client.IsOpen) return;

        try
        {
            await RefreshTablesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            ShowNothingOpen();
        }
    }

    /// <summary>Closes the connection, and empties the tree and the results with it.</summary>
    private async Task DisconnectAsync()
    {
        // A query still running is stopped first, and the connection is closed
        // only once that query has let go of it: closing under a statement that
        // is still reading would pull the connection away from it. Its answer,
        // or its failure, is no longer wanted, so the run is no longer current.
        var query = _runningQuery;

        _running?.Cancel();
        _running = null;

        if (query is not null) await query.ConfigureAwait(true);

        try
        {
            await _client.CloseAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Said rather than thrown: this runs from a button, and the panel
            // has to stay usable whatever the connection did on the way out.
            _status.Text = ex.Message;
            ShowNothingOpen();
            return;
        }

        ShowNothingOpen();
        _status.Text = Localizer.Get(StringKeys.DatabaseDisconnected);
    }

    /// <summary>Starts a run and keeps it, so that a disconnect can wait for it.</summary>
    private Task StartRunAsync()
    {
        _runningQuery = RunAsync();

        return _runningQuery;
    }

    /// <summary>
    /// Shows every row of a table, as a SELECT of it would, and leaves that
    /// query in the box so it can be edited and run again.
    /// </summary>
    private Task ShowRowsOfAsync(string table)
    {
        // The name is quoted as an identifier, so a table with a space or a
        // keyword for a name is read as that table and nothing else.
        _query.Text = $"SELECT * FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

        return StartRunAsync();
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
                        FontSize = PanelMetrics.FontSize,
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
        // The status line says what to do; here the column keeps its icon only,
        // so the same sentence is not shown twice.
        _noTables.IsVisible = true;
        _noTables.Message = "";
        _results.ItemsSource = null;
        _results.Columns.Clear();
        _run.IsEnabled = false;
        _refresh.IsEnabled = false;
        _disconnect.IsEnabled = false;
        _stop.IsVisible = false;

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
