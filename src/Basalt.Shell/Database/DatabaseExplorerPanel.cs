using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Data.Sessions;
using Basalt.Shell.Controls;

namespace Basalt.Shell.Database;

/// <summary>A request to show a table or view's rows.</summary>
public sealed record DatabaseObjectRequest(string ConnectionId, DbObjectName Name, DbObjectKind Kind);

/// <summary>A request for a query document, with the SQL to start it with.</summary>
public sealed record DatabaseQueryRequest(string ConnectionId, string Sql);

/// <summary>
/// The Database Explorer: the saved connections, and under each one its tables,
/// views, columns, indexes and foreign keys.
/// </summary>
/// <remarks>
/// Nothing is read from a database until a node is expanded. Connecting,
/// listing a connection's tables and reading a table's details each happen on
/// the node's expand, on a worker, so a slow server does not hold the window.
/// Adding, editing and removing a connection go through the connection dialog
/// and the workspace, and the tree is rebuilt from the saved list afterwards.
/// </remarks>
public sealed partial class DatabaseExplorerPanel : UserControl
{
    private readonly DatabaseWorkspace _workspace;
    private readonly ObservableCollection<ExplorerNode> _roots = [];

    private readonly TreeView _tree;
    private readonly Border _empty;
    private readonly TextBlock _status;
    private readonly Button _refresh;
    private readonly Button _disconnect;
    private readonly TextBox _filter;

    /// <summary>Waits for a pause in the typing before the tree is narrowed, so each key does not rebuild it.</summary>
    private readonly DispatcherTimer _filterTimer = new() { Interval = FilterDelay };

    /// <summary>How long the filter waits after the last key.</summary>
    internal static readonly TimeSpan FilterDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Stops the database work the panel started: listing, details, counting and scripting. Cancelled when the
    /// tree is rebuilt, a connection is disconnected, or the panel leaves the window, so a slow count does not
    /// keep running for a tree nobody sees.
    /// </summary>
    private CancellationTokenSource _operation = new();

    /// <summary>The node the context menu was opened on, found from the pointer press before it.</summary>
    private ExplorerNode? _menuNode;

    public DatabaseExplorerPanel(DatabaseWorkspace workspace)
    {
        _workspace = workspace;

        _tree = new TreeView
        {
            ItemsSource = _roots,
            Background = Brushes.Transparent,
            BorderThickness = default,
            ItemTemplate = new FuncTreeDataTemplate<ExplorerNode>(
                (node, _) => BuildRow(),
                node => node is null ? NoChildren : node.Children),
        };

        // A node opens and closes through its own flag, so a filter can open the groups that hold a match and
        // the tree keeps what the user folded when the list is rebuilt under it.
        _tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters = { new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(ExplorerNode.IsExpanded)) { Mode = BindingMode.TwoWay }) },
        });

        _filter = new TextBox
        {
            Name = "DatabaseFilter",
            Watermark = Localizer.Get(StringKeys.DbFilterWatermark),
            FontSize = PanelMetrics.FontSize,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
        };
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            ApplyFilter(_filter.Text ?? "");
        };
        _filter.TextChanged += (_, _) =>
        {
            _filterTimer.Stop();
            _filterTimer.Start();
        };
        DetachedFromVisualTree += (_, _) => CancelOperations();

        var add = DatabaseButtons.Create(IconKind.DatabaseAdd, Localizer.Get(StringKeys.DbAddConnection));
        add.Click += (_, _) => Run(AddConnectionAsync);

        _refresh = DatabaseButtons.Create(IconKind.Refresh, Localizer.Get(StringKeys.DatabaseRefresh));
        _refresh.Click += (_, _) => Run(() => RefreshAsync(_tree.SelectedItem as ExplorerNode));

        _disconnect = DatabaseButtons.Create(IconKind.Disconnect, Localizer.Get(StringKeys.DatabaseDisconnect));
        _disconnect.Click += (_, _) => Run(DisconnectSelectedAsync);

        var toolbar = PanelToolbar.Row(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            Children = { add, _refresh, _disconnect },
        });

        // Shown instead of the tree while there is nothing to list: a line that
        // says what to do, and the button that does it.
        var addFirst = DatabaseButtons.Create(IconKind.DatabaseAdd, Localizer.Get(StringKeys.DbAddConnection));
        addFirst.HorizontalAlignment = HorizontalAlignment.Center;
        addFirst.Click += (_, _) => Run(AddConnectionAsync);

        _empty = new Border
        {
            Child = new StackPanel
            {
                Spacing = Spacing.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new EmptyState(Localizer.Get(StringKeys.DbNoConnections), IconKind.Information),
                    addFirst,
                },
            },
        };

        _status = new TextBlock
        {
            FontSize = PanelMetrics.SecondaryFontSize,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        var content = new Grid { Children = { _tree, _empty } };

        Grid.SetRow(toolbar, 0);
        Grid.SetRow(_filter, 1);
        Grid.SetRow(content, 2);
        Grid.SetRow(_status, 3);
        body.Children.Add(toolbar);
        body.Children.Add(_filter);
        body.Children.Add(content);
        body.Children.Add(_status);

        Content = body;

        _tree.AddHandler(TreeViewItem.ExpandedEvent, (_, e) =>
        {
            if (e.Source is TreeViewItem { DataContext: ExplorerNode node })
                Run(() => LoadNodeAsync(node));
        });

        _tree.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(_tree).Properties.IsRightButtonPressed) _menuNode = NodeOf(e.Source);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        _tree.ContextMenu = BuildContextMenu();

        // Double-clicking a table is the question a person usually asks of one:
        // what is in it. The tree's own selection stands in when the tap's source is not a row.
        _tree.DoubleTapped += (_, e) =>
        {
            if ((NodeOf(e.Source) ?? _tree.SelectedItem as ExplorerNode) is ObjectNode table) OpenData(table);
        };

        _workspace.Changed += (_, _) => Rebuild();

        Rebuild();
    }

    /// <summary>The children of a node that has none: an empty list, so the tree shows no expander.</summary>
    private static readonly IEnumerable<ExplorerNode> NoChildren = [];

    /// <summary>One row of the tree: the node's icon and its text, as the Solution Explorer draws its rows.</summary>
    private static Control BuildRow()
    {
        var text = new TextBlock
        {
            FontSize = PanelMetrics.FontSize,
            Margin = new Thickness(0, Spacing.Hairline),
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.TextProperty] = new Binding(nameof(ExplorerNode.Text)),
            [!ToolTip.TipProperty] = new Binding(nameof(ExplorerNode.Text)),
        };

        var icon = new IconView
        {
            Classes = { "rowIcon" },
            [RowIcon.FollowsSelectionProperty] = true,
            IconSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            [!IconView.KindProperty] = new Binding(nameof(ExplorerNode.Icon)),
        };

        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { icon, text } };
    }

    /// <summary>The filter box. For the tests, which type into it.</summary>
    internal TextBox FilterBox => _filter;

    /// <summary>The tree. For the tests, which read the rows it drew.</summary>
    internal TreeView Tree => _tree;

    /// <summary>The tree's context menu. For the tests.</summary>
    internal ContextMenu? TreeMenu => _tree.ContextMenu;

    /// <summary>Raised with a sentence about a table or view, for the Output window.</summary>
    public event EventHandler<string>? ObjectDescribed;

    /// <summary>
    /// Narrows every connection's objects to those whose name contains the text, ignoring case. The filter
    /// is kept, so a connection listed afterwards is narrowed too.
    /// </summary>
    private void ApplyFilter(string text)
    {
        FilterRuns++;
        _filterText = text;

        foreach (var connection in _roots.OfType<ConnectionNode>()) connection.ApplyFilter(text);
    }

    private string _filterText = "";

    /// <summary>How many times the tree was narrowed. For the tests: typing three letters should do it once.</summary>
    internal int FilterRuns { get; private set; }

    /// <summary>The text the tree is narrowed by now, which trails the box by the typing pause. For the tests.</summary>
    internal string AppliedFilter => _filterText;

    /// <summary>Stops what the panel has running and starts a fresh token for what comes next.</summary>
    internal void CancelOperations()
    {
        var stopped = _operation;

        _operation = new CancellationTokenSource();
        stopped.Cancel();
        stopped.Dispose();
    }

    /// <summary>Raised when the rows of a table or view are asked for.</summary>
    public event EventHandler<DatabaseObjectRequest>? DataRequested;

    /// <summary>Raised when a query is asked for, with its starting SQL.</summary>
    public event EventHandler<DatabaseQueryRequest>? QueryRequested;

    /// <summary>What the explorer last said. For the tests and the status line.</summary>
    internal string StatusText => _status.Text ?? "";

    /// <summary>
    /// Makes a node the one the context menu acts on, as a right-click on it does, and returns the menu's
    /// actions. For the tests, which invoke the same actions the menu does.
    /// </summary>
    internal IReadOnlyList<MenuAction> MenuActionsForTests(ExplorerNode node)
    {
        _menuNode = node;

        return MenuActions();
    }

    /// <summary>The connection the selection is in, or null when none is selected.</summary>
    public string? SelectedConnectionId => ConnectionOf(_tree.SelectedItem as ExplorerNode)?.ConnectionId;

    /// <summary>The saved connections as the tree lists them. For the tests.</summary>
    internal IReadOnlyList<ExplorerNode> Roots => _roots;

    /// <summary>Raised when a connection is removed, so the tabs that read it can say so.</summary>
    public event EventHandler<string>? ConnectionRemoved;

    /// <summary>Raised when a connection's settings were changed, so the tabs that read it stop writing.</summary>
    public event EventHandler<string>? ConnectionEdited;

    /// <summary>Opens the dialog that adds a connection, and saves it if the user says OK.</summary>
    public async Task AddConnectionAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var draft = await DatabaseConnectionDialog.AskAsync(
            owner, Localizer.Get(StringKeys.DbDialogAddTitle), DatabaseDraft.Empty(DatabaseKind.Sqlite), _workspace, null);

        if (draft is null) return;

        var saved = await _workspace.AddAsync(draft);

        _status.Text = Localizer.Get(StringKeys.DbConnected, saved.Name);
    }

    /// <summary>Opens the dialog that edits a connection, and saves the change if the user says OK.</summary>
    public async Task EditConnectionAsync(ConnectionNode connection)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var current = ConnectionStrings.Parse(connection.Saved, password: null);

        var draft = await DatabaseConnectionDialog.AskAsync(
            owner, Localizer.Get(StringKeys.DbDialogEditTitle), current, _workspace, connection.ConnectionId);

        if (draft is null) return;

        await _workspace.UpdateAsync(connection.ConnectionId, draft);

        ConnectionEdited?.Invoke(this, connection.ConnectionId);
        _status.Text = Localizer.Get(StringKeys.DbConnected, draft.Name);
    }

    /// <summary>
    /// Connects and lists a connection's tables and views, for the expand and for Refresh.
    /// A failure is shown on the node, where the user is looking.
    /// </summary>
    private async Task LoadNodeAsync(ExplorerNode node)
    {
        switch (node)
        {
            case ConnectionNode connection when !connection.IsLoaded:
                await ListConnectionAsync(connection).ConfigureAwait(true);
                break;

            case ObjectNode table:
                await table.LoadDetailsAsync(_operation.Token).ConfigureAwait(true);
                break;
        }
    }

    private async Task ListConnectionAsync(ConnectionNode connection)
    {
        connection.ShowLoading();

        try
        {
            var session = await _workspace.OpenAsync(connection.ConnectionId, _operation.Token).ConfigureAwait(true);

            connection.ApplyFilter(_filterText);
            await connection.LoadAsync(session, _operation.Token).ConfigureAwait(true);

            _status.Text = Localizer.Get(StringKeys.DbConnected, session.DisplayName);
        }
        catch (OperationCanceledException)
        {
            // Stopped by a rebuild, a disconnect or the panel leaving: nothing to report, and nothing was read.
            connection.Reset();
        }
        catch (Exception ex)
        {
            var message = Localizer.Get(StringKeys.DbOpenFailed, connection.Text, ex.Message);

            connection.ShowFailure(message);
            _status.Text = message;
        }
    }

    /// <summary>Re-reads a connection's objects, or a table's details, from the database.</summary>
    private async Task RefreshAsync(ExplorerNode? node)
    {
        switch (node)
        {
            case ObjectNode table:
                table.ForgetDetails();
                await table.LoadDetailsAsync(_operation.Token).ConfigureAwait(true);
                break;

            case var other when ConnectionOf(other) is { } connection:
                await ListConnectionAsync(connection).ConfigureAwait(true);
                break;
        }
    }

    /// <summary>Closes the selected connection. Its saved settings stay; the next expand connects again.</summary>
    private async Task DisconnectSelectedAsync()
    {
        if (ConnectionOf(_tree.SelectedItem as ExplorerNode) is not { } connection) return;

        CancelOperations();
        await _workspace.CloseAsync(connection.ConnectionId).ConfigureAwait(true);

        connection.Reset();
        _status.Text = Localizer.Get(StringKeys.DbDisconnected, connection.Text);
    }

    /// <summary>Removes a connection after the user confirms. Its password goes with it, and its tables are marked as removed.</summary>
    internal async Task RemoveConnectionAsync(ConnectionNode connection)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var confirmed = await DatabaseQuestions.ConfirmAsync(
            owner,
            Localizer.Get(StringKeys.DbRemoveQuestion, connection.Text),
            Localizer.Get(StringKeys.DbRemove));

        if (!confirmed) return;

        await _workspace.RemoveAsync(connection.ConnectionId);

        ConnectionRemoved?.Invoke(this, connection.ConnectionId);
        _status.Text = Localizer.Get(StringKeys.DbDisconnected, connection.Text);
    }

    private void OpenData(ObjectNode table) =>
        DataRequested?.Invoke(this, new DatabaseObjectRequest(ConnectionIdOf(table), table.Name, table.Info.Kind));

    private void NewQuery(string connectionId, string sql = "") =>
        QueryRequested?.Invoke(this, new DatabaseQueryRequest(connectionId, sql));

    /// <summary>The context menu. Each entry is greyed out for the nodes it does not apply to.</summary>
    private IReadOnlyList<MenuAction> MenuActions() =>
    [
        new(Localizer.Get(StringKeys.DbOpenData), () => OpenData((ObjectNode)_menuNode!))
        {
            IsAvailable = () => _menuNode is ObjectNode,
            Icon = IconKind.Explorer,
        },
        new(Localizer.Get(StringKeys.DbNewQuery), () => NewQuery(ConnectionIdOfMenu()))
        {
            IsAvailable = () => ConnectionOf(_menuNode) is not null,
            Icon = IconKind.New,
        },
        new(Localizer.Get(StringKeys.DbSelectTop), () => SelectTop((ObjectNode)_menuNode!))
        {
            IsAvailable = () => _menuNode is ObjectNode,
        },
        new(Localizer.Get(StringKeys.DbObjectProperties), () => Run(() => DescribeAsync((ObjectNode)_menuNode!)))
        {
            IsAvailable = () => _menuNode is ObjectNode,
            Icon = IconKind.Properties,
        },
        .. DesignMenuActions(),
        new(Localizer.Get(StringKeys.DatabaseRefresh), () => Run(() => RefreshAsync(_menuNode)))
        {
            IsAvailable = () => ConnectionOf(_menuNode) is not null,
            Icon = IconKind.Refresh,
        },
        new(Localizer.Get(StringKeys.DbCopyName), () => _ = CopyNameAsync(_menuNode!.Text))
        {
            IsAvailable = () => _menuNode is ObjectNode or ConnectionNode,
        },
        MenuAction.Separator,
        new(Localizer.Get(StringKeys.DbEditConnection), () => Run(() => EditConnectionAsync((ConnectionNode)_menuNode!)))
        {
            IsAvailable = () => _menuNode is ConnectionNode,
            Icon = IconKind.Settings,
        },
        new(Localizer.Get(StringKeys.DbRemoveConnection), () => Run(() => RemoveConnectionAsync((ConnectionNode)_menuNode!)))
        {
            IsAvailable = () => _menuNode is ConnectionNode,
        },
    ];

    /// <summary>
    /// The context menu: the entries of <see cref="MenuActions"/>, and the "Script as" submenu after the
    /// query entries. ContextMenus.Build has no submenus, so this one is added to the list it built.
    /// </summary>
    private ContextMenu BuildContextMenu()
    {
        var menu = ContextMenus.Build(MenuActions());
        var items = (IList<object>)menu.ItemsSource!;

        var scriptAs = new MenuItem
        {
            Header = Localizer.Get(StringKeys.DbScriptAs),
            Icon = new IconView { Kind = IconKind.ScriptFile, IconSize = 14 },
        };

        var live = new List<(MenuAction Action, MenuItem Item)>();

        foreach (var action in ScriptActions())
        {
            var entry = new MenuItem { Header = action.Header };
            var captured = action;

            entry.Click += (_, _) => captured.Invoke();
            live.Add((action, entry));
            scriptAs.Items.Add(entry);
        }

        // After "Select Top": the entries that open a query are together.
        var selectTop = items.OfType<MenuItem>().FirstOrDefault(i => i.Header as string == Localizer.Get(StringKeys.DbSelectTop));

        items.Insert(selectTop is null ? items.Count : items.IndexOf(selectTop) + 1, scriptAs);

        menu.Opening += (_, _) =>
        {
            scriptAs.IsEnabled = _menuNode is ObjectNode;

            foreach (var (action, item) in live) item.IsEnabled = action.IsAvailable?.Invoke() ?? true;
        };

        return menu;
    }

    /// <summary>The "Script as" entries. Each opens a new query tab with the text, quoted as the engine quotes.</summary>
    private IReadOnlyList<MenuAction> ScriptActions() =>
    [
        new(Localizer.Get(StringKeys.DbScriptSelect), () => Run(() => ScriptAsync((ObjectNode)_menuNode!, ScriptKind.Select)))
        {
            IsAvailable = () => _menuNode is ObjectNode,
        },
        new(Localizer.Get(StringKeys.DbScriptInsert), () => Run(() => ScriptAsync((ObjectNode)_menuNode!, ScriptKind.Insert)))
        {
            IsAvailable = () => _menuNode is ObjectNode { IsView: false },
        },
        new(Localizer.Get(StringKeys.DbScriptUpdate), () => Run(() => ScriptAsync((ObjectNode)_menuNode!, ScriptKind.Update)))
        {
            IsAvailable = () => _menuNode is ObjectNode { IsView: false },
        },
        new(Localizer.Get(StringKeys.DbScriptDelete), () => Run(() => ScriptAsync((ObjectNode)_menuNode!, ScriptKind.Delete)))
        {
            IsAvailable = () => _menuNode is ObjectNode { IsView: false },
        },
        new(Localizer.Get(StringKeys.DbScriptCreate), () => Run(() => ScriptAsync((ObjectNode)_menuNode!, ScriptKind.Create)))
        {
            IsAvailable = () => _menuNode is ObjectNode,
        },
    ];

    /// <summary>The "Script as" entries as actions, for a test that chooses one as the menu does.</summary>
    internal IReadOnlyList<MenuAction> ScriptActionsForTests(ExplorerNode node)
    {
        _menuNode = node;

        return ScriptActions();
    }

    private enum ScriptKind { Select, Insert, Update, Delete, Create }

    /// <summary>
    /// Reads the table's columns on the session's worker and opens a query tab with the script. The tab opens
    /// when the text is ready, not before, so it never shows an empty query the user might start typing in.
    /// </summary>
    private async Task ScriptAsync(ObjectNode table, ScriptKind kind)
    {
        var token = _operation.Token;

        try
        {
            var details = await table.Session.GetDetailsAsync(table.Name, token).ConfigureAwait(true);
            var dialect = table.Session.Dialect;

            var sql = kind switch
            {
                ScriptKind.Select => SqlScripts.Select(dialect, details),
                ScriptKind.Insert => SqlScripts.Insert(dialect, details),
                ScriptKind.Update => SqlScripts.Update(dialect, details),
                ScriptKind.Delete => SqlScripts.Delete(dialect, details),
                _ => await SqlScripts.CreateAsync(table.Session, details, token).ConfigureAwait(true),
            };

            if (sql is null)
            {
                _status.Text = Localizer.Get(StringKeys.DbScriptNoCreate, table.Name.ToString());
                return;
            }

            NewQuery(ConnectionIdOf(table), sql);
        }
        catch (OperationCanceledException)
        {
            // Stopped with the tree it was started from.
        }
        catch (Exception ex)
        {
            _status.Text = Localizer.Get(StringKeys.DbScriptFailed, table.Name.ToString(), ex.Message);
        }
    }

    /// <summary>
    /// Counts a table's rows and describes it: the count is a query on the session's worker, so a large
    /// table does not hold the window. The sentence goes to the status line and the Output window.
    /// </summary>
    internal async Task DescribeAsync(ObjectNode table)
    {
        var token = _operation.Token;

        try
        {
            _status.Text = Localizer.Get(StringKeys.DbLoading);

            var stats = await ObjectStatistics.ReadAsync(table.Session, table.Name, token).ConfigureAwait(true);

            var sentence = Localizer.Get(
                StringKeys.DbObjectPropertiesText, stats.Name, stats.RowCount, stats.ColumnCount, stats.IndexCount, stats.ForeignKeyCount);

            _status.Text = sentence;
            ObjectDescribed?.Invoke(this, sentence);
        }
        catch (OperationCanceledException)
        {
            // Stopped with the tree it was started from: the count is no longer wanted.
        }
        catch (Exception ex)
        {
            _status.Text = Localizer.Get(StringKeys.DbObjectPropertiesFailed, table.Name.ToString(), ex.Message);
        }
    }

    private void SelectTop(ObjectNode table)
    {
        // The engine writes the statement: a SELECT TOP on SQL Server, LIMIT elsewhere.
        var sql = table.Session.Dialect.SelectTop(table.Name, 1000);

        NewQuery(ConnectionIdOf(table), sql);
    }

    private string ConnectionIdOfMenu() => ConnectionOf(_menuNode)?.ConnectionId ?? "";

    private string ConnectionIdOf(ObjectNode table) => ConnectionOf(table)?.ConnectionId ?? "";

    private async Task CopyNameAsync(string name)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(name).ConfigureAwait(true);

        _status.Text = Localizer.Get(StringKeys.EditorCopied, name);
    }

    /// <summary>Rebuilds the connection list from what was saved. The tree is shown only when there is something in it.</summary>
    private void Rebuild()
    {
        // The nodes the running work would fill are about to be replaced.
        CancelOperations();

        _roots.Clear();

        foreach (var saved in _workspace.Connections)
        {
            var node = new ConnectionNode(saved);

            node.ApplyFilter(_filterText);
            _roots.Add(node);
        }

        _empty.IsVisible = _roots.Count == 0;
        _tree.IsVisible = _roots.Count > 0;
    }

    /// <summary>The connection a node belongs to: itself, or the one its tree hangs under.</summary>
    private ConnectionNode? ConnectionOf(ExplorerNode? node)
    {
        if (node is null) return null;
        if (node is ConnectionNode connection) return connection;

        return _roots.OfType<ConnectionNode>().FirstOrDefault(root => Contains(root, node));
    }

    private static bool Contains(ExplorerNode parent, ExplorerNode node) =>
        parent.Children.Any(child => ReferenceEquals(child, node) || Contains(child, node));

    /// <summary>The tree node a visual sits in, if any.</summary>
    private static ExplorerNode? NodeOf(object? source) =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<TreeViewItem>().FirstOrDefault()?.DataContext as ExplorerNode;

    /// <summary>Runs an action from a button or a menu, reporting a failure in the status line rather than throwing.</summary>
    private void Run(Func<Task> work) =>
        Guarded.Run(work, message => _status.Text = message, "database");
}
