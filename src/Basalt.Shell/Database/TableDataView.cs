using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Basalt.Core.Localization;
using Basalt.Data.Sessions;
using Basalt.Shell.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Basalt.Shell.Database;

/// <summary>
/// One cell of a Table Data row: the text as it is now, NULL or not, and what it was when read.
/// </summary>
internal sealed class DataCell : ObservableObject
{
    private string? _text;
    private bool _isNull;
    private bool _touched;

    public DataCell(string? text, bool isNewRow)
    {
        _text = text;
        _isNull = text is null;
        IsNewRow = isNewRow;
        OriginalText = text;
        OriginalIsNull = text is null;
    }

    public bool IsNewRow { get; }

    public string? OriginalText { get; }

    public bool OriginalIsNull { get; }

    /// <summary>The text, or null for NULL.</summary>
    public string? Text => _text;

    public bool IsNull => _isNull;

    /// <summary>Whether the user set this cell. For a new row, the cell goes into the INSERT only when set.</summary>
    public bool Touched => _touched;

    public bool IsChanged => IsNewRow
        ? _touched
        : _isNull != OriginalIsNull || (!_isNull && _text != OriginalText);

    /// <summary>Sets the cell from the user: a text, or null for NULL.</summary>
    public void Set(string? text)
    {
        _text = text;
        _isNull = text is null;
        _touched = true;

        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(IsNull));
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(Touched));
    }

    /// <summary>Puts the cell back to the text it was read with, dropping the user's change.</summary>
    public void Revert()
    {
        _text = OriginalText;
        _isNull = OriginalIsNull;
        _touched = false;

        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(IsNull));
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(Touched));
    }
}

/// <summary>One row of the Table Data grid, with the key it was read by.</summary>
internal sealed class TableRow : ObservableObject
{
    private bool _deleted;

    public TableRow(DataCell[] cells, IReadOnlyDictionary<string, object?> key, bool isNew)
    {
        Cells = cells;
        Key = key;
        IsNew = isNew;
    }

    public DataCell[] Cells { get; }

    /// <summary>The primary key values as read, which find the row again when it is updated or deleted.</summary>
    public IReadOnlyDictionary<string, object?> Key { get; }

    public bool IsNew { get; }

    public bool IsDeleted
    {
        get => _deleted;
        set => SetProperty(ref _deleted, value);
    }

    /// <summary>Whether Save would write this row.</summary>
    public bool HasChanges => IsNew
        ? !IsDeleted && Cells.Any(c => c.Touched)
        : IsDeleted || Cells.Any(c => c.IsChanged);
}

/// <summary>
/// The rows of one table or view, one page at a time, editable where the table
/// has a primary key.
/// </summary>
/// <remarks>
/// Paging, sorting and filtering are the database's: each asks it for a page,
/// so a table of a million rows costs one page. Edits are pending until Save,
/// which sends them to <see cref="IDatabaseSession.ApplyEditsAsync"/> in one
/// transaction; a refused edit leaves everything pending and selects its row.
/// Paging, sorting and filtering are refused while edits are pending, because
/// reading a new page would throw the edits away without the user asking.
/// </remarks>
public sealed class TableDataView : Grid, IDatabaseDocumentContent
{
    /// <summary>Rows per page.</summary>
    public const int PageSize = 200;

    private readonly DatabaseWorkspace _workspace;
    private readonly string _connectionId;
    private readonly ObservableCollection<TableRow> _rows = [];
    private readonly DataGrid _grid;
    private readonly TextBlock _range;
    private readonly TextBlock _status;
    private readonly TextBlock _notice;
    private readonly TextBox _filter;
    private readonly Button _first;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly Button _last;
    private readonly Button _save;
    private readonly Button _discard;
    private readonly Button _addRow;
    private readonly Button _deleteRow;
    private readonly Button _reconnect;
    private readonly Dictionary<DataGridRow, PropertyChangedEventHandler> _rowWatchers = [];

    private TableDetails? _details;
    private bool _removed;

    // The connection's settings version the rows were read under; a save under another is refused.
    private int _settingsVersion;
    private bool _stale;
    private bool _saving;
    private int _offset;
    private int _pageRows;
    private long? _total;
    private SortSpec? _sort;
    private string? _filterText;

    public TableDataView(DatabaseWorkspace workspace, string connectionId, DbObjectName name, DbObjectKind kind)
    {
        _workspace = workspace;
        _connectionId = connectionId;
        Table = name;
        Kind = kind;

        _grid = new DataGrid
        {
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            CanUserSortColumns = true,
            CanUserReorderColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            SelectionMode = DataGridSelectionMode.Extended,
            FontSize = PanelMetrics.FontSize,
        };

        DatabaseGrids.StyleHeaders(_grid);

        _grid.Sorting += (_, e) => RunInBackground(() => SortByAsync(e.Column.SortMemberPath));
        _grid.CellEditEnding += OnCellEditEnding;
        _grid.LoadingRow += (_, e) => WatchRow(e.Row);
        _grid.UnloadingRow += (_, e) => UnwatchRow(e.Row);
        _grid.ContextMenu = ContextMenus.Build(
        [
            new(Localizer.Get(StringKeys.DbSetNull), SetCurrentToNull)
            {
                IsAvailable = () => !IsReadOnly && CurrentCell() is not null,
            },
            MenuAction.Separator,
            new(Localizer.Get(StringKeys.DbAddRow), AddRow) { IsAvailable = () => !IsReadOnly },
            new(Localizer.Get(StringKeys.DbDeleteRow), DeleteSelectedRow)
            {
                IsAvailable = () => !IsReadOnly && _grid.SelectedItem is TableRow,
            },
        ]);

        _range = new TextBlock { FontSize = PanelMetrics.FontSize, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Spacing.Normal, 0) };

        _first = Command(IconKind.PageFirst, StringKeys.DbFirst, () => GoToAsync(0));
        _previous = Command(IconKind.Back, StringKeys.DbPrevious, () => GoToAsync(Math.Max(0, _offset - PageSize)));
        _next = Command(IconKind.Forward, StringKeys.DbNext, () => GoToAsync(_offset + PageSize));
        _last = Command(IconKind.PageLast, StringKeys.DbLast, () => GoToAsync(LastOffset()));

        var refresh = Command(IconKind.Refresh, StringKeys.DatabaseRefresh, () => GoToAsync(_offset));
        _save = Command(IconKind.Save, StringKeys.DbSave, () => SaveAsync());
        _discard = Command(IconKind.Undo, StringKeys.DbDiscard, DiscardAsync);
        _addRow = Immediate(IconKind.RowAdd, StringKeys.DbAddRow, AddRow);
        _deleteRow = Immediate(IconKind.RowDelete, StringKeys.DbDeleteRow, DeleteSelectedRow);
        var export = Command(IconKind.Publish, StringKeys.DbExportCsv, ExportAsync);
        var copy = Command(IconKind.Copy, StringKeys.DbCopyWithHeaders, CopyWithHeadersAsync);

        var paging = Row(_first, _previous, _next, _last, _range);
        _reconnect = Command(IconKind.Connect, StringKeys.DbReconnect, ReconnectAsync);
        _reconnect.IsVisible = false;

        var editing = Row(refresh, _reconnect, _save, _discard, _addRow, _deleteRow, export, copy);

        _filter = new TextBox { FontSize = PanelMetrics.FontSize, PlaceholderText = Localizer.Get(StringKeys.DbFilterHint), VerticalAlignment = VerticalAlignment.Center };
        _filter.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) RunInBackground(ApplyFilterAsync);
        };

        var apply = Command(IconKind.Find, StringKeys.DbApply, ApplyFilterAsync);
        var filtering = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            Children =
            {
                new TextBlock
                {
                    Text = Localizer.Get(StringKeys.DbFilter),
                    FontSize = PanelMetrics.FontSize,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, Spacing.Normal, 0),
                },
                apply,
                _filter,
            },
        };
        DockPanel.SetDock(apply, Avalonia.Controls.Dock.Right);

        _notice = new TextBlock
        {
            FontSize = PanelMetrics.SecondaryFontSize,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            IsVisible = false,
        }.Themed(TextBlock.ForegroundProperty, "WarningForegroundBrush");

        _status = new TextBlock
        {
            FontSize = PanelMetrics.SecondaryFontSize,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto");

        Place(paging, 0);
        Place(editing, 1);
        Place(filtering, 2);
        Place(_notice, 3);
        Place(_grid, 4);
        Place(_status, 5);

        UpdateCommands();
    }

    /// <summary>The connection the table is read through, so a table is not opened twice.</summary>
    public string ConnectionId => _connectionId;

    /// <summary>The table or view shown.</summary>
    public DbObjectName Table { get; }

    public DbObjectKind Kind { get; }

    /// <summary>Whether the table can be changed from here: a table with a primary key.</summary>
    public bool IsReadOnly => _details is null || !_details.IsEditable;

    /// <summary>Whether a change is pending: a row added, changed or marked for deletion.</summary>
    public bool HasUnsavedChanges => _rows.Any(r => r.HasChanges);

    /// <summary>The text of the status line. For the tests and the document's own use.</summary>
    internal string StatusText => _status.Text ?? "";

    /// <summary>The range the paging line shows. For the tests.</summary>
    internal string RangeText => _range.Text ?? "";

    /// <summary>The grid. For the tests, which drive it the way a user would.</summary>
    internal DataGrid Grid => _grid;

    /// <summary>Reads the table's details and its first page.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            // Taken before the session, so an edit that lands while it opens counts as later than the read.
            _settingsVersion = _workspace.SettingsVersionOf(_connectionId);

            var session = await SessionAsync(ct).ConfigureAwait(true);
            _details = await session.GetDetailsAsync(Table, ct).ConfigureAwait(true);

            BuildColumns(_details.Columns);

            _grid.IsReadOnly = IsReadOnly;
            _notice.Text = Localizer.Get(StringKeys.DatabaseNotEditable, Table.ToString());
            _notice.IsVisible = IsReadOnly;

            await ReadAsync(0, ct).ConfigureAwait(true);
            _reconnect.IsVisible = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed(ex);
        }

        UpdateCommands();
    }

    /// <summary>
    /// Writes the pending changes in one transaction, then reads the page again. A second call while one is
    /// in flight does nothing: the first would otherwise write the same rows twice.
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        if (_saving) return false;
        if (!HasUnsavedChanges || _details is null) return !HasUnsavedChanges;

        // A stale tab's rows may belong to a database the connection no longer points at: nothing is written from it.
        if (_stale || _removed)
        {
            _status.Text = Localizer.Get(_stale ? StringKeys.DbConnectionChanged : StringKeys.DbConnectionRemoved);
            return false;
        }

        var (edits, rows) = BuildEdits();

        _saving = true;
        UpdateCommands();

        try
        {
            var session = await SessionAsync(CancellationToken.None).ConfigureAwait(true);

            // The stale check above can pass while an edit of the connection holds its gate: the session handed
            // back afterwards is the edited connection's, and these rows' keys belong to the database read before.
            if (_workspace.SettingsVersionOf(_connectionId) != _settingsVersion)
            {
                MarkStale();
                _status.Text = Localizer.Get(StringKeys.DbConnectionChanged);
                return false;
            }

            var changed = await session.ApplyEditsAsync(Table, edits).ConfigureAwait(true);

            // Written, so the edits are no longer pending, whatever the reload does next.
            _rows.Clear();
            _status.Text = Localizer.Get(StringKeys.DbSavedRows, changed);

            await ReadAsync(_offset).ConfigureAwait(true);
            return true;
        }
        catch (DatabaseEditException ex)
        {
            // The row the engine refused is the one the user needs to see: selected, and named by its number.
            var row = rows[Math.Clamp(ex.EditIndex, 0, rows.Count - 1)];

            _grid.SelectedItem = row;
            _grid.ScrollIntoView(row, null);

            _status.Text = Localizer.Get(StringKeys.DbEditRefused, _rows.IndexOf(row) + 1, ex.InnerException?.Message ?? ex.Message);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed(ex);
            return false;
        }
        finally
        {
            _saving = false;
            UpdateCommands();
        }
    }

    /// <summary>Saves from the keyboard: Ctrl+S in this tab.</summary>
    internal void SaveFromKeyboard() => RunInBackground(async () => { await SaveAsync().ConfigureAwait(true); });

    /// <summary>
    /// Stops showing the table after its connection was removed: nothing more is read or saved from it,
    /// and the tab says so.
    /// </summary>
    public void MarkRemoved()
    {
        _removed = true;
        _notice.Text = Localizer.Get(StringKeys.DbConnectionRemoved);
        _notice.IsVisible = true;
        _reconnect.IsVisible = false;
        _grid.IsReadOnly = true;
        UpdateCommands();
    }

    /// <summary>Whether the connection this tab reads was removed.</summary>
    public bool IsRemoved => _removed;

    /// <summary>
    /// Asks for the connection again after a failure. Pending edits are kept: the session is reconnected and
    /// the page is not read again while there are changes the database does not have yet.
    /// </summary>
    private async Task ReconnectAsync()
    {
        // A stale or removed tab reads nothing, so it has nothing to reconnect for.
        if (_stale || _removed) return;

        try
        {
            // A new session, not the cached one: the cached session is the connection that failed.
            await _workspace.ReconnectAsync(_connectionId, CancellationToken.None).ConfigureAwait(true);
            _reconnect.IsVisible = false;

            if (_details is null)
                await LoadAsync().ConfigureAwait(true);
            else if (HasUnsavedChanges)
                _status.Text = Localizer.Get(StringKeys.DbReconnectedKeepsEdits);
            else
                await ReadGuardedAsync(_offset).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed(ex);
        }

        UpdateCommands();
    }

    /// <summary>
    /// The connection was edited since this table was read: its rows and keys may belong to another database
    /// now, so nothing more is read or written from this tab. Its pending edits can still be discarded.
    /// </summary>
    public void MarkStale()
    {
        _stale = true;
        _notice.Text = Localizer.Get(StringKeys.DbConnectionChanged);
        _notice.IsVisible = true;
        _reconnect.IsVisible = false;
        _grid.IsReadOnly = true;
        UpdateCommands();
    }

    /// <summary>Whether the connection was edited after this table was read.</summary>
    public bool IsStale => _stale;

    /// <summary>Drops the pending changes and reads the page as the database has it.</summary>
    /// <remarks>
    /// A stale or removed tab reads nothing, so its pending changes are dropped from the rows already shown.
    /// Discarding must still work there: a change the user does not want cannot be left pending for ever.
    /// </remarks>
    public Task DiscardAsync()
    {
        if (!_stale && !_removed) return ReadGuardedAsync(_offset);

        DropPendingEdits();
        UpdateCommands();

        return Task.CompletedTask;
    }

    /// <summary>Puts the rows back as they were read: added rows go, deleted rows come back, changed cells take their read text.</summary>
    private void DropPendingEdits()
    {
        foreach (var row in _rows.ToList())
        {
            if (row.IsNew)
            {
                _rows.Remove(row);
                continue;
            }

            row.IsDeleted = false;

            foreach (var cell in row.Cells) cell.Revert();
        }
    }

    /// <summary>Writes every row of the table, filtered and sorted as the grid is, to a CSV file. Returns the rows written.</summary>
    /// <remarks>
    /// The pages are read, formatted and written on the thread pool; only the final status comes back to the
    /// interface thread. The file is replaced only when the whole table has been written.
    /// </remarks>
    public async Task<int> ExportCsvAsync(string path, CancellationToken ct = default)
    {
        // Nothing has been read yet, so there are no columns to write.
        if (_details is null) return 0;

        // A stale or removed tab would export another database's rows, or none, under this table's headers.
        if (_stale || _removed) return 0;

        // Taken here, on the interface thread: the background work never reads the grid or the view's fields.
        var table = Table;
        var sort = SortList();
        var filter = _filterText;
        var headers = _details.Columns.Select(c => c.Name).ToList();
        var session = await SessionAsync(ct).ConfigureAwait(true);

        // As for Save: an edit of the connection may have held its gate while the session was asked for.
        if (_workspace.SettingsVersionOf(_connectionId) != _settingsVersion)
        {
            MarkStale();
            return 0;
        }

        var written = await CsvExport.WriteAsync(path, headers, async (csv, token) =>
        {
            var rows = 0;

            // A page at a time, written as it is read: the whole table is never held in memory.
            for (var offset = 0; ; offset += PageSize)
            {
                var page = await session.ReadPageAsync(new DataPageRequest(table, offset, PageSize, sort, filter), token)
                    .ConfigureAwait(false);

                await csv.WriteRowsAsync(
                    page.Rows.Select(values => (IReadOnlyList<string?>)values.Select(CellText.Of).ToList()), token)
                    .ConfigureAwait(false);

                rows += page.Rows.Count;

                if (page.Rows.Count < PageSize) return rows;
            }
        }, ct).ConfigureAwait(true);

        _status.Text = Localizer.Get(StringKeys.DbExported, written, path);
        return written;
    }

    /// <summary>Sorts the grid by a column on the server, the first click ascending and the next descending.</summary>
    internal Task SortByAsync(string column)
    {
        _sort = _sort is { } current && current.Column == column
            ? new SortSpec(column, !current.Descending)
            : new SortSpec(column, false);

        return GoToAsync(0);
    }

    /// <summary>Reads the page the paging buttons point at, unless edits are pending.</summary>
    private async Task GoToAsync(int offset)
    {
        if (HasUnsavedChanges)
        {
            _status.Text = Localizer.Get(StringKeys.DbSaveFirst);
            return;
        }

        await ReadGuardedAsync(offset).ConfigureAwait(true);
    }

    private async Task ApplyFilterAsync()
    {
        if (HasUnsavedChanges)
        {
            _status.Text = Localizer.Get(StringKeys.DbSaveFirst);
            return;
        }

        var previous = _filterText;
        _filterText = _filter.Text?.Trim() is { Length: > 0 } text ? text : null;

        try
        {
            await ReadAsync(0).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A condition the database does not accept leaves the rows as they were.
            _filterText = previous;
            _status.Text = ex.Message;
        }

        UpdateCommands();
    }

    private async Task ReadGuardedAsync(int offset)
    {
        try
        {
            await ReadAsync(offset).ConfigureAwait(true);
            _status.Text = "";
            _reconnect.IsVisible = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed(ex);
        }

        UpdateCommands();
    }

    /// <summary>
    /// The connection's session, opened when it is not open: after Disconnect, or after an Edit that closed it,
    /// the next read connects again with the connection's current settings.
    /// </summary>
    private Task<IDatabaseSession> SessionAsync(CancellationToken ct) => _workspace.OpenAsync(_connectionId, ct);

    /// <summary>Says why a read or a save failed, and offers Reconnect when the connection could not be opened.</summary>
    private void Failed(Exception ex)
    {
        _status.Text = ex.Message;
        _reconnect.IsVisible = !_removed;
    }

    /// <summary>Reads one page and replaces the grid's rows with it.</summary>
    private async Task ReadAsync(int offset, CancellationToken ct = default)
    {
        if (_details is null || _removed || _stale) return;

        var session = await SessionAsync(ct).ConfigureAwait(true);
        var page = await session.ReadPageAsync(
            new DataPageRequest(Table, offset, PageSize, SortList(), _filterText), ct).ConfigureAwait(true);

        // Rows removed by a save can leave this page empty: the last page that has rows is shown instead.
        if (page.Rows.Count == 0 && offset > 0)
        {
            var total = page.TotalRows ?? 0;
            var last = total > 0 ? (int)((total - 1) / PageSize * PageSize) : 0;

            if (last < offset)
            {
                await ReadAsync(last, ct).ConfigureAwait(true);
                return;
            }
        }

        var details = _details;

        _rows.Clear();

        foreach (var values in page.Rows) _rows.Add(RowFrom(details, values));

        _offset = offset;
        _pageRows = page.Rows.Count;
        _total = page.TotalRows;

        UpdatePaging();
        UpdateCommands();
    }

    private IReadOnlyList<SortSpec> SortList() => _sort is null ? [] : [_sort];

    private static TableRow RowFrom(TableDetails details, IReadOnlyList<object?> values)
    {
        var cells = new DataCell[details.Columns.Count];
        var key = new Dictionary<string, object?>(StringComparer.Ordinal);

        for (var i = 0; i < cells.Length; i++)
        {
            var value = i < values.Count ? values[i] : null;

            cells[i] = new DataCell(CellText.Of(value), isNewRow: false);

            if (details.Columns[i].IsPrimaryKey) key[details.Columns[i].Name] = value;
        }

        return new TableRow(cells, key, isNew: false);
    }

    private void BuildColumns(IReadOnlyList<ColumnInfo> columns)
    {
        _grid.Columns.Clear();

        for (var i = 0; i < columns.Count; i++)
        {
            var at = i;

            _grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = columns[i].Name,
                Tag = at,
                SortMemberPath = columns[i].Name,
                CanUserSort = true,
                CellTemplate = new FuncDataTemplate<TableRow>((row, _) => CellFor(row, at), supportsRecycling: false),
                CellEditingTemplate = new FuncDataTemplate<TableRow>(
                    (row, _) => new TextBox
                    {
                        Text = row?.Cells[at].Text ?? "",
                        FontSize = PanelMetrics.FontSize,
                        BorderThickness = default,

                        // What the box started with: leaving it unchanged must not turn a NULL into an empty text.
                        Tag = row is null ? null : new EditStart(row.Cells[at].IsNull, row.Cells[at].Text ?? ""),
                    },
                    supportsRecycling: false),
            });
        }
    }

    /// <summary>
    /// One cell of the grid. NULL is shown as grey italics; a changed cell takes a tint, so pending
    /// edits can be found by eye before they are saved.
    /// </summary>
    private static Control CellFor(TableRow? row, int at)
    {
        var text = new TextBlock
        {
            FontSize = PanelMetrics.FontSize,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var border = new Border
        {
            Padding = new Thickness(Spacing.Tight, 2),
            Child = text,
        };

        if (row is null) return border;

        var cell = row.Cells[at];

        void Refresh()
        {
            text.Text = cell.IsNull ? Localizer.Get(StringKeys.DbNull) : cell.Text ?? "";
            text.FontStyle = cell.IsNull ? FontStyle.Italic : FontStyle.Normal;

            // A local null Foreground is not "the theme's colour": it is no brush at all, so the text
            // is drawn transparent. A value takes its colour from the theme only once the local value is cleared.
            if (cell.IsNull && DescriptionBrush(border) is { } description) text.Foreground = description;
            else text.ClearValue(TextBlock.ForegroundProperty);

            border.Background = cell.IsChanged ? ChangedBrush(border) : null;
        }

        PropertyChangedEventHandler handler = (_, _) => Refresh();

        // Listening only while on screen: a grid that recycles its cells must not keep them alive.
        border.AttachedToVisualTree += (_, _) =>
        {
            cell.PropertyChanged += handler;
            Refresh();
        };
        border.DetachedFromVisualTree += (_, _) => cell.PropertyChanged -= handler;

        Refresh();

        return border;
    }

    private static IBrush? DescriptionBrush(Control scope) =>
        scope.TryFindResource("DescriptionForegroundBrush", out var brush) ? brush as IBrush : null;

    /// <summary>The warning colour at low strength: visible on light and dark themes alike.</summary>
    private static IBrush? ChangedBrush(Control scope) =>
        scope.TryFindResource("WarningForegroundBrush", out var brush) && brush is ISolidColorBrush solid
            ? new SolidColorBrush(solid.Color, 0.25)
            : null;

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.EditingElement is not TextBox box) return;
        if (e.Column.Tag is not int at) return;
        if (e.Row.DataContext is not TableRow row) return;

        var cell = row.Cells[at];
        var text = box.Text ?? "";

        if (box.Tag is EditStart start && text == start.Text && start.IsNull == cell.IsNull) return;

        cell.Set(text);
        UpdateCommands();
    }

    /// <summary>A cell's content when its editor opened.</summary>
    private sealed record EditStart(bool IsNull, string Text);

    private void WatchRow(DataGridRow gridRow)
    {
        if (gridRow.DataContext is not TableRow row) return;

        void Paint() =>
            gridRow.Background = row.IsDeleted
                ? new SolidColorBrush(Colors.Red, 0.18)
                : row.IsNew
                    ? new SolidColorBrush(Colors.Green, 0.12)
                    : null;

        PropertyChangedEventHandler handler = (_, _) => Paint();
        row.PropertyChanged += handler;
        _rowWatchers[gridRow] = handler;
        Paint();
    }

    private void UnwatchRow(DataGridRow gridRow)
    {
        if (gridRow.DataContext is TableRow row && _rowWatchers.Remove(gridRow, out var handler))
            row.PropertyChanged -= handler;
    }

    private (List<RowEdit> Edits, List<TableRow> Rows) BuildEdits()
    {
        var edits = new List<RowEdit>();
        var rows = new List<TableRow>();
        var columns = _details!.Columns;

        foreach (var row in _rows.Where(r => r.HasChanges))
        {
            if (row.IsNew)
            {
                var values = new Dictionary<string, string?>(StringComparer.Ordinal);

                for (var i = 0; i < columns.Count; i++)
                    if (row.Cells[i].Touched) values[columns[i].Name] = row.Cells[i].Text;

                edits.Add(new RowInsert(values));
            }
            else if (row.IsDeleted)
            {
                edits.Add(new RowDelete(row.Key));
            }
            else
            {
                var changes = new Dictionary<string, string?>(StringComparer.Ordinal);

                for (var i = 0; i < columns.Count; i++)
                    if (row.Cells[i].IsChanged) changes[columns[i].Name] = row.Cells[i].Text;

                edits.Add(new RowUpdate(row.Key, changes));
            }

            rows.Add(row);
        }

        return (edits, rows);
    }

    private void AddRow()
    {
        if (IsReadOnly || _details is null) return;

        var cells = _details.Columns.Select(_ => new DataCell(null, isNewRow: true)).ToArray();
        var row = new TableRow(cells, new Dictionary<string, object?>(), isNew: true);

        _rows.Add(row);
        _grid.SelectedItem = row;
        _grid.ScrollIntoView(row, null);

        UpdateCommands();
    }

    private void DeleteSelectedRow()
    {
        if (IsReadOnly || _grid.SelectedItem is not TableRow row) return;

        // A row added and not yet saved has nothing in the database to delete: it just goes.
        if (row.IsNew) _rows.Remove(row);
        else row.IsDeleted = true;

        UpdateCommands();
    }

    /// <summary>The cell the grid has current, as its row and column index.</summary>
    private (TableRow Row, int Column)? CurrentCell()
    {
        if (_grid.SelectedItem is not TableRow row || _grid.CurrentColumn?.Tag is not int at) return null;

        return (row, at);
    }

    private void SetCurrentToNull()
    {
        if (CurrentCell() is not { } cell) return;

        cell.Row.Cells[cell.Column].Set(null);
        UpdateCommands();
    }

    private async Task ExportAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer.Get(StringKeys.DbExportCsv),
            SuggestedFileName = Table.Name + ".csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
        });

        if (file?.TryGetLocalPath() is { } path) await ExportCsvAsync(path).ConfigureAwait(true);
    }

    private async Task CopyWithHeadersAsync()
    {
        if (_details is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;

        var selected = _grid.SelectedItems.OfType<TableRow>().ToList();
        var chosen = selected.Count > 0 ? selected : _rows.ToList();

        var text = DatabaseGrids.Delimited(
            _details.Columns.Select(c => c.Name).ToList(),
            chosen.Select(row => (IReadOnlyList<string?>)row.Cells.Select(c => c.IsNull ? null : c.Text).ToList()).ToList(),
            includeHeaders: true,
            separator: '\t');

        await clipboard.SetTextAsync(text).ConfigureAwait(true);

        _status.Text = Localizer.Get(StringKeys.DbCopied, chosen.Count);
    }

    private void UpdatePaging()
    {
        var from = _pageRows == 0 ? 0 : _offset + 1;
        var to = _offset + _pageRows;

        _range.Text = _total is { } total
            ? Localizer.Get(StringKeys.DbRowsRange, from, to, total)
            : Localizer.Get(StringKeys.DbRowsRangeUnknown, from, to);

        _first.IsEnabled = _offset > 0;
        _previous.IsEnabled = _offset > 0;
        _next.IsEnabled = _total is { } known ? _offset + PageSize < known : _pageRows == PageSize;
        _last.IsEnabled = _total is { } last && _offset < LastOffset();
    }

    /// <summary>The offset of the page that holds the last row.</summary>
    private int LastOffset() => _total is { } total && total > 0
        ? (int)((total - 1) / PageSize * PageSize)
        : 0;

    private void UpdateCommands()
    {
        var editable = !IsReadOnly && _details is not null && !_removed && !_stale;
        var pending = HasUnsavedChanges;

        _save.IsEnabled = editable && pending && !_saving;
        _discard.IsEnabled = pending;
        _addRow.IsEnabled = editable;
        _deleteRow.IsEnabled = editable;
        _filter.IsEnabled = !pending;
    }

    /// <summary>A button whose work is asynchronous; a failure is written to the status line.</summary>
    private Button Command(IconKind icon, string key, Func<Task> action)
    {
        var button = DatabaseButtons.Create(icon, Localizer.Get(key));
        button.Click += (_, _) => RunInBackground(action);

        return button;
    }

    /// <summary>A button whose work is done at once, on the interface thread.</summary>
    private static Button Immediate(IconKind icon, string key, Action action)
    {
        var button = DatabaseButtons.Create(icon, Localizer.Get(key));
        button.Click += (_, _) => action();

        return button;
    }

    /// <summary>Runs work started by the grid or a button, reporting a failure on the status line rather than throwing.</summary>
    private void RunInBackground(Func<Task> work) =>
        Guarded.Run(work, message => _status.Text = message, "database");

    private void Place(Control child, int row)
    {
        Children.Add(child);
        SetRow(child, row);
    }

    private static StackPanel Row(params Control[] children)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
        };

        foreach (var child in children) panel.Children.Add(child);

        return panel;
    }
}
