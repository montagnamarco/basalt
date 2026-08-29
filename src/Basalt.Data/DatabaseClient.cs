using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Basalt.Data;

/// <summary>A column of a result, named and typed.</summary>
public sealed record ResultColumn(string Name, string Type);

/// <summary>
/// What a query answered with.
/// </summary>
/// <remarks>
/// Rows are strings rather than objects: they are going into a grid to be
/// read, and every provider has its own idea of what a DATE or a BLOB is.
/// Formatting once, here, beats every caller guessing.
/// </remarks>
public sealed record QueryResult(
    IReadOnlyList<ResultColumn> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    int Affected,
    TimeSpan Took)
{
    /// <summary>A statement that changed rows rather than returning any.</summary>
    public bool IsCount => Columns.Count == 0;
}

/// <summary>A table, and the columns it holds.</summary>
public sealed record TableInfo(string Name, IReadOnlyList<ResultColumn> Columns);

/// <summary>
/// Talking to a database from inside the IDE.
/// </summary>
/// <remarks>
/// SQLite first, and only SQLite for now: it is a file rather than a server,
/// so it works the moment the IDE is installed with nothing to set up. The
/// shape here — open, list, query — is what any other provider would need
/// too, so adding one later is a second implementation rather than a redesign.
///
/// Every call is cancellable. A query over a large table can take minutes,
/// and an IDE that cannot be told to stop is one that has hung.
/// </remarks>
public sealed class DatabaseClient : IAsyncDisposable
{
    private DbConnection? _connection;

    /// <summary>The file this is connected to, or nothing.</summary>
    public string? Source { get; private set; }

    /// <summary>Whether there is a database to talk to.</summary>
    public bool IsOpen => _connection is { State: System.Data.ConnectionState.Open };

    /// <summary>
    /// Opens a database file.
    /// </summary>
    /// <remarks>
    /// Read-write, but the file has to exist: creating one because a path was
    /// mistyped leaves an empty database where the user expected their data,
    /// and no error to say so.
    /// </remarks>
    public async Task OpenAsync(string path, CancellationToken ct = default)
    {
        await CloseAsync().ConfigureAwait(false);

        if (!File.Exists(path))
            throw new FileNotFoundException($"There is no database at {path}.", path);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
        };

        var connection = new SqliteConnection(builder.ToString());

        await connection.OpenAsync(ct).ConfigureAwait(false);

        _connection = connection;
        Source = path;

        try
        {
            // Opening succeeds on anything, including a text file: SQLite
            // does not look at the header until it is asked for something.
            // Reading the catalogue is that question, and asking it here
            // means a file picked by mistake fails now rather than on the
            // user's first query.
            await GetTablesAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await CloseAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// The tables in the database, with their columns.
    /// </summary>
    /// <remarks>
    /// SQLite's own catalogue, which is a table like any other. The internal
    /// ones it keeps for itself are left out: they are not the user's data
    /// and listing them buries what is.
    /// </remarks>
    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default)
    {
        if (_connection is null) return [];

        var names = new List<string>();

        await using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT name FROM sqlite_master
                WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%'
                ORDER BY name
                """;

            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                names.Add(reader.GetString(0));
        }

        var tables = new List<TableInfo>();

        foreach (var name in names)
            tables.Add(new TableInfo(name, await GetColumnsAsync(name, ct).ConfigureAwait(false)));

        return tables;
    }

    /// <summary>The columns of one table.</summary>
    public async Task<IReadOnlyList<ResultColumn>> GetColumnsAsync(
        string table, CancellationToken ct = default)
    {
        if (_connection is null) return [];

        await using var command = _connection.CreateCommand();

        // The table name cannot be a parameter — it is part of the statement,
        // not a value — so it is quoted, with any quote in it doubled.
        command.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\")";

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var columns = new List<ResultColumn>();

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            columns.Add(new ResultColumn(reader.GetString(1), reader.GetString(2)));

        return columns;
    }

    /// <summary>
    /// Runs a statement and reports what came back.
    /// </summary>
    /// <remarks>
    /// A SELECT gives rows; anything else gives a count. Both are answers, and
    /// a panel that only understood one of them would say nothing at all
    /// after an UPDATE.
    ///
    /// How long it took is reported because it is the first thing anyone
    /// wants to know about a slow query.
    /// </remarks>
    public async Task<QueryResult> ExecuteAsync(string sql, CancellationToken ct = default)
    {
        if (_connection is null)
            throw new InvalidOperationException("No database is open.");

        var started = System.Diagnostics.Stopwatch.StartNew();

        await using var command = _connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var columns = new List<ResultColumn>();

        for (var i = 0; i < reader.FieldCount; i++)
            columns.Add(new ResultColumn(reader.GetName(i), reader.GetDataTypeName(i)));

        if (columns.Count == 0)
            return new QueryResult([], [], reader.RecordsAffected, started.Elapsed);

        var rows = new List<IReadOnlyList<string?>>();

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var row = new string?[columns.Count];

            for (var i = 0; i < columns.Count; i++)
                row[i] = reader.IsDBNull(i) ? null : Format(reader.GetValue(i));

            rows.Add(row);

            // A query that returns a million rows would fill memory before it
            // filled the grid; the rest are a page away, not lost.
            if (rows.Count >= MaximumRows) break;
        }

        return new QueryResult(columns, rows, reader.RecordsAffected, started.Elapsed);
    }

    /// <summary>How many rows are read before the rest are left.</summary>
    public const int MaximumRows = 5000;

    /// <summary>
    /// A value as it should read in a grid.
    /// </summary>
    /// <remarks>
    /// Invariant for numbers and a sortable shape for dates: a grid is read
    /// as much as it is skimmed, and a column that does not line up cannot
    /// be compared down its length.
    /// </remarks>
    private static string Format(object value) => value switch
    {
        byte[] bytes => $"<{bytes.Length} bytes>",
        DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable number => number.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    public async Task CloseAsync()
    {
        if (_connection is null) return;

        await _connection.DisposeAsync().ConfigureAwait(false);

        _connection = null;
        Source = null;
    }

    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);
}
