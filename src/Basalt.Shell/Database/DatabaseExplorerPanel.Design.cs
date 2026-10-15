using Basalt.Core.Localization;
using Basalt.Shell.Controls;

namespace Basalt.Shell.Database;

/// <summary>The Explorer's entries for the table designer and the database diagram.</summary>
public sealed partial class DatabaseExplorerPanel
{
    /// <summary>Raised when the designer is asked for: a table to change, or a new table.</summary>
    public event EventHandler<DatabaseDesignRequest>? DesignRequested;

    /// <summary>Raised when the diagram of a connection is asked for.</summary>
    public event EventHandler<DatabaseDiagramRequest>? DiagramRequested;

    /// <summary>
    /// "Design Table" on a table, "New Table" on the Tables folder and "Database Diagram" on a connection. Each is
    /// greyed out for the nodes it does not apply to, as the other entries are.
    /// </summary>
    private IReadOnlyList<MenuAction> DesignMenuActions() =>
    [
        new(Localizer.Get(StringKeys.DbDesignTable),
            () => DesignRequested?.Invoke(this, new DatabaseDesignRequest(ConnectionIdOf((ObjectNode)_menuNode!), ((ObjectNode)_menuNode!).Name)))
        {
            IsAvailable = () => _menuNode is ObjectNode { IsView: false },
            Icon = IconKind.Table,
        },
        new(Localizer.Get(StringKeys.DbNewTable),
            () => DesignRequested?.Invoke(this, new DatabaseDesignRequest(ConnectionIdOfMenu(), null)))
        {
            IsAvailable = () => _menuNode is GroupNode { Icon: IconKind.FolderTables },
            Icon = IconKind.New,
        },
        new(Localizer.Get(StringKeys.DbDiagram),
            () => DiagramRequested?.Invoke(this, new DatabaseDiagramRequest(ConnectionIdOfMenu())))
        {
            IsAvailable = () => _menuNode is ConnectionNode,
        },
    ];

    /// <summary>
    /// Lists a connection's tables again, after the designer created or changed one. A connection that was
    /// never opened has nothing to refresh.
    /// </summary>
    internal async Task RefreshConnectionAsync(string connectionId)
    {
        if (Roots.OfType<ConnectionNode>().FirstOrDefault(c => c.ConnectionId == connectionId) is { IsLoaded: true } connection)
            await ListConnectionAsync(connection).ConfigureAwait(true);
    }

    /// <summary>The actions of the design entries for a node, as the menu offers them. For the tests.</summary>
    internal IReadOnlyList<MenuAction> DesignActionsForTests(ExplorerNode node)
    {
        _menuNode = node;

        return DesignMenuActions();
    }
}
