using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Data.Sessions;
using Basalt.Shell.Controls;
using Basalt.Shell.Syntax;

namespace Basalt.Shell.Database;

/// <summary>
/// A query document: SQL in an editor, run against a connection, with each
/// result set in its own tab and the engine's messages in a last one.
/// </summary>
/// <remarks>
/// Run takes the selection when there is one, and the whole text otherwise. The
/// batch runs on the connection's worker and Stop cancels it: the engine stops
/// the statement on the server, not only the wait. A message with a line number
/// moves the caret to that line of the editor, counting from the first line the
/// batch was run from, so an error in a selection is found where it was typed.
/// </remarks>
public sealed class QueryView : Grid, IDatabaseDocumentContent, IDisposable
{
    /// <summary>The most rows kept per result set. The engine stops reading there and says so.</summary>
    public const int MaximumRows = 10_000;

    private readonly DatabaseWorkspace _workspace;
    private readonly int _number;
    private readonly TextEditor _editor;
    private readonly ComboBox _connection;
    private readonly Button _run;
    private readonly Button _cancel;
    private readonly Button _historyButton;
    private readonly TabControl _results;
    private StackPanel _messages;
    private readonly TextBlock _status;
    private readonly DispatcherTimer _clock;
    private readonly List<ResultSet> _resultSets = [];

    /// <summary>The SQL colouring, installed with the interface theme and re-themed when the settings change.</summary>
    private TextMateHighlighting? _highlighting;

    /// <summary>The theme the colouring was last set for, with System already resolved.</summary>
    private AppTheme _theme;

    /// <summary>The theme as the settings name it, System included, so System can be resolved again.</summary>
    private AppTheme _chosenTheme;

    private CancellationTokenSource? _running;
    private Stopwatch? _clockWatch;
    private string _savedText;
    private int _messageBase = 1;

    public QueryView(DatabaseWorkspace workspace, int number, string? connectionId, string sql, string? filePath, AppTheme theme)
    {
        _workspace = workspace;
        _number = number;
        FilePath = filePath;
        _savedText = sql;
        _chosenTheme = theme;
        _theme = ResolvedTheme(theme);

        _editor = new TextEditor
        {
            FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
            FontSize = PanelMetrics.FontSize,
            ShowLineNumbers = true,
            Text = sql,
        };

        // The SQL grammar the registry ships, coloured with the grammar theme that goes with the window's theme:
        // the light palette on a dark editor left keywords near-invisible.
        _highlighting = TextMateHighlighting.InstallFor(_editor, "query.sql", _theme);

        _connection = new ComboBox
        {
            Name = "QueryConnection",
            MinWidth = 180,
            FontSize = PanelMetrics.FontSize,
        };

        _run = ButtonFor(IconKind.Run, StringKeys.DatabaseRun, () => RunInBackground(RunAsync));
        _run.Name = "QueryRun";

        _cancel = ButtonFor(IconKind.Stop, StringKeys.DbCancel, () => _running?.Cancel());
        _cancel.Name = "QueryCancel";
        _cancel.IsVisible = false;

        _historyButton = DatabaseButtons.Create(IconKind.History, Localizer.Get(StringKeys.DbHistory), Localizer.Get(StringKeys.DbHistoryToolTip));
        _historyButton.Name = "QueryHistory";
        _historyButton.Click += (_, _) => RunInBackground(ShowHistoryAsync);

        var saveAs = ButtonFor(IconKind.Save, StringKeys.DbSaveQueryTitle, () => RunInBackground(SaveWithPickerAsync));
        saveAs.Name = "QuerySaveAs";

        var export = ButtonFor(IconKind.Publish, StringKeys.DbExportCsv, () => RunInBackground(ExportWithPickerAsync));
        export.Name = "QueryExport";

        var copy = ButtonFor(IconKind.Copy, StringKeys.DbCopyWithHeaders, () => RunInBackground(CopyActiveAsync));
        copy.Name = "QueryCopy";

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            Children =
            {
                new TextBlock
                {
                    Text = Localizer.Get(StringKeys.DbConnection),
                    FontSize = PanelMetrics.FontSize,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, Spacing.Tight, 0),
                },
                _connection,
                _run,
                _cancel,
                _historyButton,
                saveAs,
                export,
                copy,
            },
        };

        _messages = NewMessages();

        _results = new TabControl { FontSize = PanelMetrics.FontSize };

        _status = new TextBlock
        {
            FontSize = PanelMetrics.SecondaryFontSize,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        // The clock ticks only while a batch runs, so the status line counts up rather than waiting for the end.
        _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _clock.Tick += (_, _) => ShowRunning();

        RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto");

        Place(toolbar, 0);
        Place(_editor, 1);
        Place(new GridSplitter { Height = 4, ResizeDirection = GridResizeDirection.Rows }, 2);
        Place(_results, 3);
        Place(_status, 4);

        _editor.KeyDown += (_, e) =>
        {
            // Stop from the keyboard too: Escape is what most tools use for it.
            if (e.Key == Key.Escape && _running is not null)
            {
                _running.Cancel();
                e.Handled = true;
            }
        };

        AttachedToVisualTree += (_, _) => _workspace.Changed += OnConnectionsChanged;
        DetachedFromVisualTree += (_, _) => _workspace.Changed -= OnConnectionsChanged;

        LoadConnections(connectionId);
        UpdateRunning(false);
    }

    /// <summary>The file the query was opened from or saved to, or null for a query not yet saved.</summary>
    public string? FilePath { get; private set; }

    /// <summary>Raised when the title changes, which happens when the query is first saved.</summary>
    public event EventHandler? TitleChanged;

    /// <summary>The tab's title: the file name, or "Query1.sql" and so on for a query not yet saved.</summary>
    public string Title => FilePath is null
        ? Localizer.Get(StringKeys.DbQueryTitle, _number)
        : Path.GetFileName(FilePath);

    /// <summary>The SQL as typed. For the tests and the document.</summary>
    public string Sql
    {
        get => _editor.Text;
        set => _editor.Text = value;
    }

    /// <summary>The id of the connection the picker shows. For the tests.</summary>
    internal string? SelectedConnectionId => (_connection.SelectedItem as ConnectionChoice)?.Id;

    /// <summary>The status line. For the tests.</summary>
    internal string StatusText => _status.Text ?? "";

    /// <summary>The editor the SQL is in. For the tests, which move the caret and read it back.</summary>
    internal TextEditor Editor => _editor;

    /// <summary>The result tabs, Messages last. For the tests.</summary>
    internal TabControl Results => _results;

    /// <summary>Whether a batch is running now.</summary>
    public bool IsRunning => _running is not null;

    /// <summary>
    /// Whether closing would lose the text: a query never saved has any text at all, and a saved
    /// one has text other than what was last written.
    /// </summary>
    public bool HasUnsavedChanges => FilePath is null
        ? _editor.Text.Trim().Length > 0
        : !string.Equals(_editor.Text, _savedText, StringComparison.Ordinal);

    /// <summary>Runs the selection, or the whole text when nothing is selected. Returns when the batch has finished or been stopped.</summary>
    public async Task RunAsync()
    {
        if (_running is not null) return;

        if (_connection.SelectedItem is not ConnectionChoice choice)
        {
            ShowError(Localizer.Get(StringKeys.DbChooseConnection), 0);
            return;
        }

        var (text, baseLine) = SelectionOrAll();

        if (text.Trim().Length == 0) return;

        var running = new CancellationTokenSource();
        var watch = Stopwatch.StartNew();

        _running = running;
        _messageBase = baseLine;
        _clockWatch = watch;
        UpdateRunning(true);

        // Remembered whatever happens to it: a query that fails or is stopped is one the user will want back to
        // fix. Started here and awaited at the end, not before the batch: waiting for the file first would
        // leave a Stop or a Disconnect in that gap with no session yet to stop, and the batch would then run.
        var recording = _workspace.History.AddAsync(choice.Id, text);

        try
        {
            var session = await _workspace.OpenAsync(choice.Id, running.Token).ConfigureAwait(true);
            var result = await session.ExecuteAsync(text, MaximumRows, running.Token).ConfigureAwait(true);

            // A batch stopped midway reports the stop as an error from the engine; the user asked for it, so it is not an error.
            if (running.IsCancellationRequested) ShowStopped(watch.Elapsed);
            else ShowResult(result, baseLine, watch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            ShowStopped(watch.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowError(ex.Message, watch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            _running = null;
            _clockWatch = null;
            running.Dispose();
            UpdateRunning(false);
        }

        await AwaitRecordingAsync(recording).ConfigureAwait(true);
    }

    /// <summary>Waits for the history write that went with a run. A history that could not be kept is not a failure of the query.</summary>
    private static async Task AwaitRecordingAsync(Task recording)
    {
        try
        {
            await recording.ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine("The query history was not saved: " + ex.Message);
        }
    }

    /// <summary>Writes the active result set to a CSV file. Returns the rows written, or 0 when no result is active.</summary>
    public async Task<int> ExportActiveResultAsync(string path, CancellationToken ct = default)
    {
        if (ActiveResult() is not { } set) return 0;

        // The rows are in memory already; formatting and writing them is the work kept off the interface thread.
        var headers = set.Columns.Select(c => c.Name).ToList();
        var rows = set.Rows;

        var written = await CsvExport.WriteAsync(path, headers, async (csv, token) =>
        {
            await csv.WriteRowsAsync(rows.Select(row => (IReadOnlyList<string?>)row.Select(CellText.Of).ToList()), token)
                .ConfigureAwait(false);

            return rows.Count;
        }, ct).ConfigureAwait(true);

        _status.Text = Localizer.Get(StringKeys.DbExported, written, path);
        return written;
    }

    /// <summary>The menu the History button last opened. For the tests, which choose from it.</summary>
    internal ContextMenu? HistoryMenu { get; private set; }

    /// <summary>
    /// Lists the last queries run on the chosen connection, newest first, under the History button. Choosing
    /// one puts its text in the editor. The list is read on the pool, not on the interface thread.
    /// </summary>
    internal async Task ShowHistoryAsync()
    {
        if (_connection.SelectedItem is not ConnectionChoice choice) return;

        var queries = await _workspace.History.GetAsync(choice.Id).ConfigureAwait(true);

        var items = new List<object>();

        if (queries.Count == 0)
            items.Add(new MenuItem { Header = Localizer.Get(StringKeys.DbHistoryEmpty), IsEnabled = false });

        foreach (var sql in queries)
        {
            var entry = sql;
            var item = new MenuItem { Header = HistoryHeader(entry) };

            ToolTip.SetTip(item, entry);
            item.Click += (_, _) => ReopenFromHistory(entry);
            items.Add(item);
        }

        HistoryMenu = new ContextMenu { ItemsSource = items, PlacementTarget = _historyButton };

        if (_historyButton.IsEffectivelyVisible) HistoryMenu.Open(_historyButton);
    }

    /// <summary>A query as one line of a menu: its lines joined, and cut where it would run off the screen.</summary>
    private static string HistoryHeader(string sql)
    {
        var line = string.Join(' ', sql.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return line.Length <= 80 ? line : line[..79] + "…";
    }

    /// <summary>Puts a query from the history back into the editor, ready to run again.</summary>
    internal void ReopenFromHistory(string sql)
    {
        _editor.Text = sql;
        _editor.CaretOffset = sql.Length;
        _editor.Focus();
    }

    /// <summary>Cancels the batch that is running, if one is. Used when the window closes.</summary>
    internal void CancelRun() => _running?.Cancel();

    /// <summary>
    /// Follows the settings the window applies. The colours change with the theme at once, as an open
    /// code editor's do, rather than on the next query opened.
    /// </summary>
    public void ApplySettings(IdeSettings settings)
    {
        _chosenTheme = settings.Appearance.Theme;
        Retheme();
    }

    /// <summary>The theme the SQL colouring is set for, System resolved. For the tests.</summary>
    internal AppTheme ColouringTheme => _theme;

    /// <summary>
    /// Under System the variant decides, and it is not known until the view is in the window (a view being
    /// built reads the default, which is light) and changes when the operating system switches.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ThemeVariantScope.ActualThemeVariantProperty && _chosenTheme == AppTheme.System) Retheme();
    }

    private void Retheme()
    {
        var theme = ResolvedTheme(_chosenTheme);

        if (theme == _theme) return;

        _theme = theme;
        _highlighting?.SetTheme(theme);
    }

    /// <summary>
    /// The theme "follow the system" came out as. Read from the variant Avalonia settled on, the same way
    /// the code editor reads it.
    /// </summary>
    private AppTheme ResolvedTheme(AppTheme theme) =>
        theme != AppTheme.System
            ? theme
            : ActualThemeVariant == ThemeVariant.Dark ? AppTheme.Dark : AppTheme.Light;

    /// <summary>Releases the colouring installed on the editor.</summary>
    public void Dispose()
    {
        _highlighting?.Dispose();
        _highlighting = null;
    }

    /// <summary>Saves the query to its file, asking for one when it has none yet. Returns false when the user chose none.</summary>
    public async Task<bool> SaveAsync()
    {
        if (FilePath is null) return await SaveWithPickerAsync().ConfigureAwait(true);

        return await WriteToAsync(FilePath).ConfigureAwait(true);
    }

    /// <summary>Puts the caret at the start of a line of the editor, counting from one.</summary>
    internal void MoveCaretToLine(int line)
    {
        var target = Math.Clamp(line, 1, _editor.Document.LineCount);

        _editor.CaretOffset = _editor.Document.GetLineByNumber(target).Offset;
        _editor.ScrollToLine(target);
        _editor.Focus();
    }

    /// <summary>The run from the keyboard: F5 and Ctrl+Enter, the window's shortcut handler calls it.</summary>
    internal void RunFromKeyboard() => RunInBackground(RunAsync);

    /// <summary>Save from the keyboard, when the query has focus.</summary>
    internal void SaveFromKeyboard() => RunInBackground(async () => await SaveAsync().ConfigureAwait(true));

    /// <summary>Starts a task from a button or a key; a failure goes to the status line.</summary>
    private void RunInBackground(Func<Task> work) => Guarded.Run(work, message => _status.Text = message, "database");

    private (string Text, int BaseLine) SelectionOrAll()
    {
        if (_editor.SelectionLength > 0)
            return (_editor.SelectedText, _editor.Document.GetLineByOffset(_editor.SelectionStart).LineNumber);

        return (_editor.Text, 1);
    }

    private ResultSet? ActiveResult()
    {
        var index = _results.SelectedIndex;

        return index >= 0 && index < _resultSets.Count ? _resultSets[index] : null;
    }

    /// <summary>Shows what the batch produced: one tab per result set, and the messages in the last.</summary>
    private void ShowResult(QueryBatchResult result, int baseLine, TimeSpan took)
    {
        _resultSets.Clear();
        _results.Items.Clear();
        _messages = NewMessages();

        foreach (var set in result.ResultSets)
        {
            var index = _resultSets.Count;
            _resultSets.Add(set);

            var footer = new TextBlock
            {
                FontSize = PanelMetrics.SecondaryFontSize,
                Margin = new Thickness(Spacing.Normal, Spacing.Tight),
                Text = set.Truncated
                    ? Localizer.Get(StringKeys.DbRowCount, set.Rows.Count) + "  " + Localizer.Get(StringKeys.DbTruncated, MaximumRows)
                    : Localizer.Get(StringKeys.DbRowCount, set.Rows.Count),
            };

            var panel = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom);
            panel.Children.Add(footer);
            panel.Children.Add(DatabaseGrids.ReadOnly(set.Columns, set.Rows));

            _results.Items.Add(new TabItem { Header = Localizer.Get(StringKeys.DbResultTab, index + 1), Content = panel });
        }

        foreach (var message in result.Messages) AddMessage(message, baseLine);

        var messagesTab = new TabItem
        {
            Header = Localizer.Get(StringKeys.DbMessagesTab),
            Content = new ScrollViewer { Content = _messages },
        };
        _results.Items.Add(messagesTab);

        // The messages are what the user needs when something failed or nothing came back; a result otherwise.
        _results.SelectedItem = result.Failed || _resultSets.Count == 0 ? messagesTab : _results.Items[0];

        _status.Text = result.Failed
            ? Localizer.Get(StringKeys.DbFailedAfter, (long)took.TotalMilliseconds)
            : Localizer.Get(StringKeys.DbElapsed, (long)took.TotalMilliseconds);
    }

    private static StackPanel NewMessages() => new() { Spacing = Spacing.Tight, Margin = new Thickness(Spacing.Normal) };

    private void AddMessage(QueryMessage message, int baseLine)
    {
        var text = message.IsError && message.Line is { } line
            ? Localizer.Get(StringKeys.DbMessageLine, line, message.Text)
            : message.Text;

        var block = new TextBlock
        {
            Text = text,
            FontSize = PanelMetrics.FontSize,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };

        if (message.IsError)
        {
            block.Themed(TextBlock.ForegroundProperty, "ErrorForegroundBrush");

            // An error with a line is a link to that line of the query.
            if (message.Line is { } at)
            {
                block.Cursor = new Cursor(StandardCursorType.Hand);
                block.Tapped += (_, _) => MoveCaretToLine(baseLine + at - 1);
            }
        }

        _messages.Children.Add(block);
    }

    /// <summary>Shows a failure in the Messages tab. A failure with no run behind it (no connection chosen) takes no time.</summary>
    private void ShowError(string text, double tookMilliseconds)
    {
        _results.Items.Clear();
        _resultSets.Clear();
        _messages = NewMessages();

        var block = new TextBlock { Text = text, FontSize = PanelMetrics.FontSize, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        block.Themed(TextBlock.ForegroundProperty, "ErrorForegroundBrush");
        _messages.Children.Add(block);

        var messagesTab = new TabItem { Header = Localizer.Get(StringKeys.DbMessagesTab), Content = new ScrollViewer { Content = _messages } };
        _results.Items.Add(messagesTab);
        _results.SelectedItem = messagesTab;

        _status.Text = tookMilliseconds > 0
            ? Localizer.Get(StringKeys.DbFailedAfter, (long)tookMilliseconds)
            : text;

    }

    private void ShowStopped(TimeSpan took)
    {
        _status.Text = Localizer.Get(StringKeys.DatabaseStopped) + " " + Localizer.Get(StringKeys.DbElapsed, (long)took.TotalMilliseconds);
    }

    private void ShowRunning()
    {
        if (_clockWatch is { } watch)
            _status.Text = Localizer.Get(StringKeys.DbRunning, (long)watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Run is off and Stop is on while a batch runs: one batch at a time on a connection.</summary>
    private void UpdateRunning(bool running)
    {
        _run.IsEnabled = !running;
        _cancel.IsVisible = running;
        _connection.IsEnabled = !running;

        if (running)
        {
            _clock.Start();
            ShowRunning();
        }
        else
        {
            _clock.Stop();
        }
    }

    private void LoadConnections(string? selectedId)
    {
        var choices = _workspace.Connections.Select(c => new ConnectionChoice(c.Id, c.Name)).ToList();
        var previous = selectedId ?? (_connection.SelectedItem as ConnectionChoice)?.Id;

        _connection.ItemsSource = choices;
        _connection.SelectedItem = choices.FirstOrDefault(c => c.Id == previous) ?? choices.FirstOrDefault();
    }

    private void OnConnectionsChanged(object? sender, EventArgs e) => LoadConnections(null);

    private async Task<bool> SaveWithPickerAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return false;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer.Get(StringKeys.DbSaveQueryTitle),
            SuggestedFileName = Title,
            FileTypeChoices = [new FilePickerFileType(Localizer.Get(StringKeys.DbSqlFilter)) { Patterns = ["*.sql"] }],
        });

        if (file?.TryGetLocalPath() is not { } path) return false;

        return await WriteToAsync(path).ConfigureAwait(true);
    }

    /// <summary>Writes the query to a file, and makes that file the query's from now on.</summary>
    internal async Task<bool> SaveToAsync(string path)
    {
        var text = _editor.Text;

        await File.WriteAllTextAsync(path, text).ConfigureAwait(true);

        _savedText = text;

        if (!string.Equals(FilePath, path, StringComparison.Ordinal))
        {
            FilePath = path;
            TitleChanged?.Invoke(this, EventArgs.Empty);
        }

        _status.Text = Localizer.Get(StringKeys.DbSavedQuery, Path.GetFileName(path));
        return true;
    }

    private Task<bool> WriteToAsync(string path) => SaveToAsync(path);

    private async Task ExportWithPickerAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top || ActiveResult() is null) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer.Get(StringKeys.DbExportCsv),
            SuggestedFileName = Localizer.Get(StringKeys.DbResultFileName),
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
        });

        if (file?.TryGetLocalPath() is { } path) await ExportActiveResultAsync(path);
    }

    private async Task CopyActiveAsync()
    {
        if (ActiveResult() is not { } set || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;

        var text = DatabaseGrids.Delimited(
            set.Columns.Select(c => c.Name).ToList(),
            set.Rows.Select(row => (IReadOnlyList<string?>)row.Select(CellText.Of).ToList()).ToList(),
            includeHeaders: true,
            separator: '\t');

        await clipboard.SetTextAsync(text).ConfigureAwait(true);

        _status.Text = Localizer.Get(StringKeys.DbCopied, set.Rows.Count);
    }

    private static Button ButtonFor(IconKind icon, string key, Action click)
    {
        var button = DatabaseButtons.Create(icon, Localizer.Get(key));
        button.Click += (_, _) => click();

        return button;
    }

    private void Place(Control child, int row)
    {
        Children.Add(child);
        SetRow(child, row);
    }

    /// <summary>One connection in the picker, shown by its name.</summary>
    private sealed record ConnectionChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
