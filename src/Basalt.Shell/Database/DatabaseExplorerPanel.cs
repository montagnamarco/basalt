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
public sealed class DatabaseExplorerPanel : UserControl
{
    private readonly DatabaseWorkspace _workspace;
    private readonly ObservableCollection<ExplorerNode> _roots = [];

    private readonly TreeView _tree;
    private readonly Border _empty;
    private readonly TextBlock _status;
    private readonly Button _refresh;
    private readonly Button _disconnect;

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
                (node, _) => new TextBlock
                {
                    FontSize = PanelMetrics.FontSize,
                    Margin = new Thickness(0, Spacing.Hairline),
                    [!TextBlock.TextProperty] = new Binding(nameof(ExplorerNode.Text)),
                    [!ToolTip.TipProperty] = new Binding(nameof(ExplorerNode.Text)),
                },
                node => node is null ? NoChildren : node.Children),
        };

        var add = new Button { Content = Localizer.Get(StringKeys.DbAddConnection), FontSize = PanelMetrics.FontSize };
        add.Click += (_, _) => Run(AddConnectionAsync);

        _refresh = new Button { Content = Localizer.Get(StringKeys.DatabaseRefresh), FontSize = PanelMetrics.FontSize };
        _refresh.Click += (_, _) => Run(() => RefreshAsync(_tree.SelectedItem as ExplorerNode));

        _disconnect = new Button { Content = Localizer.Get(StringKeys.DatabaseDisconnect), FontSize = PanelMetrics.FontSize };
        _disconnect.Click += (_, _) => Run(DisconnectSelectedAsync);

        var toolbar = PanelToolbar.Row(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            Children = { add, _refresh, _disconnect },
        });

        // Shown instead of the tree while there is nothing to list: a line that
        // says what to do, and the button that does it.
        var addFirst = new Button { Content = Localizer.Get(StringKeys.DbAddConnection), FontSize = PanelMetrics.FontSize, HorizontalAlignment = HorizontalAlignment.Center };
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

        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var content = new Grid { Children = { _tree, _empty } };

        Grid.SetRow(toolbar, 0);
        Grid.SetRow(content, 1);
        Grid.SetRow(_status, 2);
        body.Children.Add(toolbar);
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

        _tree.ContextMenu = ContextMenus.Build(MenuActions());

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
                await table.LoadDetailsAsync(CancellationToken.None).ConfigureAwait(true);
                break;
        }
    }

    private async Task ListConnectionAsync(ConnectionNode connection)
    {
        connection.ShowLoading();

        try
        {
            var session = await _workspace.OpenAsync(connection.ConnectionId).ConfigureAwait(true);

            await connection.LoadAsync(session, CancellationToken.None).ConfigureAwait(true);

            _status.Text = Localizer.Get(StringKeys.DbConnected, session.DisplayName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
                await table.LoadDetailsAsync(CancellationToken.None).ConfigureAwait(true);
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
        _roots.Clear();

        foreach (var saved in _workspace.Connections) _roots.Add(new ConnectionNode(saved));

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
