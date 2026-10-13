using System.Data.Common;
using System.Globalization;
using Basalt.Core.Localization;
using Microsoft.Data.Sqlite;

namespace Basalt.Data.Sessions;

/// <summary>
/// A session on one SQLite file. Its catalogue is sqlite_master and the PRAGMAs
/// table_info, index_list, index_info and foreign_key_list.
/// </summary>
/// <remarks>
/// Not sealed so that a test can observe the error hook; nothing else derives from it.
/// </remarks>
public class SqliteSession : AdoNetSession
{
    public SqliteSession(DbConnection connection, string displayName)
        : base(connection, displayName)
    {
    }

    public override DatabaseKind Kind { get; } = DatabaseKind.Sqlite;

    public override ISqlDialect Dialect { get; } = new SqliteDialect();

    /// <summary>
    /// Asks SQLite to stop the statement running on this connection.
    /// </summary>
    /// <remarks>
    /// Microsoft.Data.Sqlite's <c>DbCommand.Cancel</c> does nothing, so the native
    /// call is made on the connection's handle. sqlite3_interrupt is the one SQLite
    /// call that is safe to make from another thread than the one running the statement.
    /// </remarks>
    protected override void Interrupt(DbCommand command)
    {
        if (command.Connection is SqliteConnection { Handle: { } handle })
            SQLitePCL.raw.sqlite3_interrupt(handle);
    }

    /// <summary>
    /// SQLite has no date type: a DATE column holds the text it is given. The base
    /// conversion checks the text as an ISO date, but it hands back a DateTime,
    /// which the provider would store as "2024-03-05 00:00:00" and so change the
    /// shape of the dates already in the file. The checked text is what is stored.
    /// </summary>
    protected override object? ConvertValue(string? text, ColumnInfo column)
    {
        var converted = base.ConvertValue(text, column);

        return converted is DateTime ? text!.Trim() : converted;
    }

    protected override async Task<IReadOnlyList<DbObjectInfo>> ReadObjectsAsync(DbConnection connection, CancellationToken ct)
    {
        // The internal sqlite_ tables are the engine's own; listing them buries the user's.
        // The underscore is a LIKE wildcard, so it is escaped.
        var rows = await ReadOneSetAsync(
            connection,
            """
            SELECT name, type FROM sqlite_master
            WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
            ORDER BY name
            """,
            int.MaxValue,
            ct).ConfigureAwait(false);

        return rows.Rows
            .Select(r => new DbObjectInfo(new DbObjectName(null, Text(r[0])), KindOf(Text(r[1]))))
            .ToList();
    }

    protected override async Task<TableDetails> ReadDetailsAsync(DbConnection connection, DbObjectName name, CancellationToken ct)
    {
        // The name as the catalogue spells it, so the PRAGMAs see the same
        // identifier whatever case the caller used.
        var found = await ReadOneSetAsync(
            connection,
            $"SELECT name, type FROM sqlite_master WHERE name = {Literal(name.Name)} COLLATE NOCASE AND type IN ('table', 'view')",
            int.MaxValue,
            ct).ConfigureAwait(false);

        if (found.Rows.Count == 0)
            throw new InvalidOperationException(Localizer.Get(StringKeys.DatabaseNoObject, name.Name));

        var canonical = Text(found.Rows[0][0]);
        var kind = KindOf(Text(found.Rows[0][1]));
        var quoted = Dialect.Quote(canonical);

        var tableInfo = await PragmaAsync(connection, $"PRAGMA table_info({quoted})", ct).ConfigureAwait(false);

        // A table has a rowid alias only when its primary key is one INTEGER column.
        var keyCount = tableInfo.Count(r => Int(r[5]) > 0);

        var columns = tableInfo.Select(r =>
        {
            var declared = Text(r[2]);
            var position = Int(r[5]); // 1-based place in the primary key, 0 when not in it
            var identity = position == 1 && keyCount == 1 && declared.Equals("INTEGER", StringComparison.OrdinalIgnoreCase);

            // SQLite's INTEGER PRIMARY KEY cannot hold NULL: it is assigned on insert.
            return new ColumnInfo(
                Text(r[1]),
                declared,
                Int(r[3]) == 0 && !identity,
                position > 0,
                identity,
                r[4] as string,
                (int)Int(r[0]) + 1);
        }).ToList();

        var indexes = new List<IndexInfo>();

        foreach (var index in await PragmaAsync(connection, $"PRAGMA index_list({quoted})", ct).ConfigureAwait(false))
        {
            var indexName = Text(index[1]);
            var parts = await PragmaAsync(connection, $"PRAGMA index_info({Dialect.Quote(indexName)})", ct).ConfigureAwait(false);

            // An expression index has no column name; say so rather than list nothing.
            var names = parts
                .OrderBy(p => Int(p[0]))
                .Select(p => p[2] as string ?? "(expression)")
                .ToList();

            // origin "pk" is the index SQLite builds for a primary key that is not an INTEGER.
            indexes.Add(new IndexInfo(indexName, names, Int(index[2]) == 1, Text(index[3]) == "pk"));
        }

        var foreignKeys = new List<ForeignKeyInfo>();
        var keyRows = await PragmaAsync(connection, $"PRAGMA foreign_key_list({quoted})", ct).ConfigureAwait(false);

        foreach (var group in keyRows.GroupBy(r => Int(r[0])).OrderBy(g => g.Key))
        {
            var parts = group.OrderBy(r => Int(r[1])).ToList();
            var referenced = Text(parts[0][2]);
            var from = parts.Select(r => Text(r[3])).ToList();

            // A foreign key that omits its target columns points at the referenced
            // table's primary key; the catalogue names it rather than leave the list empty.
            IReadOnlyList<string> to = parts.Any(r => r[4] is null)
                ? await PrimaryKeyNamesAsync(connection, referenced, ct).ConfigureAwait(false)
                : parts.Select(r => Text(r[4])).ToList();

            // SQLite names no foreign keys; the table and the number are what identify one.
            foreignKeys.Add(new ForeignKeyInfo($"fk_{canonical}_{group.Key}", from, new DbObjectName(null, referenced), to));
        }

        return new TableDetails(
            new DbObjectName(null, canonical),
            kind,
            columns,
            indexes,
            foreignKeys);
    }

    private async Task<IReadOnlyList<string>> PrimaryKeyNamesAsync(DbConnection connection, string table, CancellationToken ct)
    {
        var rows = await PragmaAsync(connection, $"PRAGMA table_info({Dialect.Quote(table)})", ct).ConfigureAwait(false);

        return rows
            .Where(r => Int(r[5]) > 0)
            .OrderBy(r => Int(r[5]))
            .Select(r => Text(r[1]))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyList<object?>>> PragmaAsync(DbConnection connection, string sql, CancellationToken ct) =>
        (await ReadOneSetAsync(connection, sql, int.MaxValue, ct).ConfigureAwait(false)).Rows;

    private static DbObjectKind KindOf(string type) =>
        type == "view" ? DbObjectKind.View : DbObjectKind.Table;

    private static string Text(object? value) => value as string ?? "";

    private static long Int(object? value) => value is null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);

    /// <summary>A SQL string literal, with any quote in it doubled.</summary>
    private static string Literal(string text) => "'" + text.Replace("'", "''") + "'";
}
