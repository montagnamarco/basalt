namespace Basalt.Data.Sessions;

/// <summary>
/// An open connection to one database, and everything the IDE asks of it.
/// </summary>
/// <remarks>
/// Implementations run every call off the interface thread and allow one
/// operation at a time on their connection (a provider connection is not made
/// for two threads at once). Cancelling a call stops the statement on the
/// server, not only the wait for it: a query over a large table would
/// otherwise run to its end after the user pressed Stop.
/// </remarks>
public interface IDatabaseSession : IAsyncDisposable
{
    DatabaseKind Kind { get; }

    /// <summary>What the explorer shows for this connection: a file name, or server and database.</summary>
    string DisplayName { get; }

    /// <summary>The SQL spelling of this engine.</summary>
    ISqlDialect Dialect { get; }

    /// <summary>The tables and views the user can see, system objects left out, ordered by schema then name.</summary>
    Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(CancellationToken ct = default);

    /// <summary>Columns, indexes and foreign keys of one table or view.</summary>
    Task<TableDetails> GetDetailsAsync(DbObjectName name, CancellationToken ct = default);

    /// <summary>
    /// Runs a batch as the user wrote it: every statement, every result set,
    /// the engine's messages and counts. An error is a message in the result,
    /// not an exception, so the results produced before it still show.
    /// </summary>
    /// <param name="maxRows">Rows kept per result set; the rest are skipped and the set marked truncated.</param>
    Task<QueryBatchResult> ExecuteAsync(string sql, int maxRows = 10_000, CancellationToken ct = default);

    /// <summary>One page of a table's rows, sorted and filtered on the server.</summary>
    Task<DataPage> ReadPageAsync(DataPageRequest request, CancellationToken ct = default);

    /// <summary>
    /// Applies the data view's edits in one transaction, with parameters, by
    /// primary key: all of them, or none. Returns the rows changed. Throws
    /// <see cref="DatabaseEditException"/> naming the edit that failed.
    /// </summary>
    Task<int> ApplyEditsAsync(DbObjectName table, IReadOnlyList<RowEdit> edits, CancellationToken ct = default);
}

/// <summary>The way one engine spells the SQL the IDE generates.</summary>
public interface ISqlDialect
{
    /// <summary>An identifier quoted for this engine: [name], "name" or `name`, inner quotes doubled.</summary>
    string Quote(string identifier);

    /// <summary>A table's name, schema included where the engine has one, each part quoted.</summary>
    string QualifiedName(DbObjectName name);

    /// <summary>
    /// "SELECT … FROM table [WHERE filter] ORDER BY … " limited to one page:
    /// LIMIT/OFFSET, or OFFSET/FETCH for SQL Server, which also needs an ORDER BY
    /// and gets one on the key, or on the first column, when none is asked for.
    /// </summary>
    string SelectPage(TableDetails table, int offset, int limit, IReadOnlyList<SortSpec> sort, string? filter);

    /// <summary>A starting SELECT a user can edit: the first rows of a table, as a new query opens with.</summary>
    string SelectTop(DbObjectName name, int rows);
}

/// <summary>Opens sessions for one engine.</summary>
public interface IDatabaseProvider
{
    DatabaseKind Kind { get; }

    /// <summary>
    /// Opens and checks a connection, so a wrong password or an unreachable
    /// server fails here, with the engine's message, not on the first query.
    /// </summary>
    Task<IDatabaseSession> OpenAsync(string connectionString, CancellationToken ct = default);
}

/// <summary>An edit the database refused; the transaction was rolled back and nothing was changed.</summary>
public sealed class DatabaseEditException(int editIndex, string message, Exception inner)
    : Exception(message, inner)
{
    /// <summary>The position of the refused edit in the list given to ApplyEditsAsync.</summary>
    public int EditIndex { get; } = editIndex;
}
