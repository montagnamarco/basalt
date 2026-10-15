using Basalt.Data.Sessions;

namespace Basalt.Shell.Database;

/// <summary>A request for the table designer: an existing table, or (Name null) a new one.</summary>
public sealed record DatabaseDesignRequest(string ConnectionId, DbObjectName? Name);

/// <summary>A request for the diagram of a connection's tables.</summary>
public sealed record DatabaseDiagramRequest(string ConnectionId);
