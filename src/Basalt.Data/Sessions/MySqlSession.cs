using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Basalt.Core.Localization;

namespace Basalt.Data.Sessions;

/// <summary>
/// A session on one MySQL or MariaDB database. Its catalogue is the information_schema
/// of the current database: TABLES, COLUMNS, STATISTICS and KEY_COLUMN_USAGE.
/// </summary>
/// <remarks>
/// Not sealed, so that a test can expose the hooks; nothing else derives from it.
/// MySqlException carries no line number, so errors here have none.
/// </remarks>
public class MySqlSession : AdoNetSession
{
    public MySqlSession(DbConnection connection, string displayName)
        : base(connection, displayName)
    {
    }

    public override DatabaseKind Kind { get; } = DatabaseKind.MySql;

    public override ISqlDialect Dialect { get; } = new MySqlDialect();

    /// <summary>An insert with no values is written "() VALUES ()", which MySQL takes; DEFAULT VALUES is not MySQL.</summary>
    protected override string EmptyInsertClause => "() VALUES ()";

    /// <summary>
    /// The type with the modifiers that the base conversion does not read removed.
    /// "int unsigned" is an integer, and "tinyint(1)" is MySQL's boolean.
    /// </summary>
    protected override object? ConvertValue(string? text, ColumnInfo column) =>
        base.ConvertValue(text, column with { DataType = Normalised(column.DataType) });

    protected override async Task<IReadOnlyList<DbObjectInfo>> ReadObjectsAsync(DbConnection connection, CancellationToken ct)
    {
        var rows = await ReadOneSetAsync(
            connection,
            """
            SELECT TABLE_NAME, TABLE_TYPE
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE IN ('BASE TABLE', 'VIEW')
            ORDER BY TABLE_NAME
            """,
            int.MaxValue,
            ct).ConfigureAwait(false);

        // A MySQL schema is the database, so the objects have none.
        return rows.Rows
            .Select(r => new DbObjectInfo(
                new DbObjectName(null, Text(r[0])),
                Text(r[1]) == "VIEW" ? DbObjectKind.View : DbObjectKind.Table))
            .ToList();
    }

    protected override async Task<TableDetails> ReadDetailsAsync(DbConnection connection, DbObjectName name, CancellationToken ct)
    {
        var schema = name.Schema is null ? "DATABASE()" : Literal(name.Schema);

        var found = await ReadOneSetAsync(
            connection,
            $"""
            SELECT TABLE_NAME, TABLE_TYPE
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = {schema} AND TABLE_NAME = {Literal(name.Name)}
              AND TABLE_TYPE IN ('BASE TABLE', 'VIEW')
            """,
            int.MaxValue,
            ct).ConfigureAwait(false);

        if (found.Rows.Count == 0)
            throw new InvalidOperationException(Localizer.Get(StringKeys.DatabaseNoObject, name.ToString()));

        var canonical = new DbObjectName(name.Schema, Text(found.Rows[0][0]));
        var kind = Text(found.Rows[0][1]) == "VIEW" ? DbObjectKind.View : DbObjectKind.Table;

        var columnRows = await ReadOneSetAsync(
            connection,
            $"""
            SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, EXTRA, COLUMN_DEFAULT
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = {schema} AND TABLE_NAME = {Literal(name.Name)}
            ORDER BY ORDINAL_POSITION
            """,
            int.MaxValue,
            ct).ConfigureAwait(false);

        var indexes = await ReadIndexesAsync(connection, schema, name.Name, ct).ConfigureAwait(false);
        var primaryKey = indexes.Where(i => i.IsPrimaryKey).SelectMany(i => i.Columns).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var columns = columnRows.Rows
            .Select((r, index) =>
            {
                var columnName = Text(r[0]);

                return new ColumnInfo(
                    columnName,
                    Text(r[1]),
                    Text(r[2]) == "YES",
                    primaryKey.Contains(columnName),
                    Text(r[3]).Contains("auto_increment", StringComparison.OrdinalIgnoreCase),
                    TextOrNull(r[4]),
                    index + 1);
            })
            .ToList();

        var foreignKeys = await ReadForeignKeysAsync(connection, schema, name.Name, ct).ConfigureAwait(false);

        return new TableDetails(canonical, kind, columns, indexes, foreignKeys);
    }

    private async Task<IReadOnlyList<IndexInfo>> ReadIndexesAsync(DbConnection connection, string schema, string table, CancellationToken ct)
    {
        // An index named PRIMARY is the primary key. A functional key part has no column name (MySQL 8).
        var rows = await ReadOneSetAsync(
            connection,
            $"""
            SELECT INDEX_NAME, NON_UNIQUE, COLUMN_NAME, SEQ_IN_INDEX
            FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = {schema} AND TABLE_NAME = {Literal(table)}
            ORDER BY INDEX_NAME, SEQ_IN_INDEX
            """,
            int.MaxValue,
            ct).ConfigureAwait(false);

        return rows.Rows
            .GroupBy(r => Text(r[0]))
            .Select(g => new IndexInfo(
                g.Key,
                g.Select(r => TextOrNull(r[2]) ?? "(expression)").ToList(),
                Convert.ToInt64(g.First()[1], CultureInfo.InvariantCulture) == 0,
                g.Key == "PRIMARY"))
            .ToList();
    }

    private async Task<IReadOnlyList<ForeignKeyInfo>> ReadForeignKeysAsync(DbConnection connection, string schema, string table, CancellationToken ct)
    {
        // A referenced table in the session's own database has no schema, as the contract has it.
        var rows = await ReadOneSetAsync(
            connection,
            $"""
            SELECT CONSTRAINT_NAME, ORDINAL_POSITION, COLUMN_NAME,
                   CASE WHEN REFERENCED_TABLE_SCHEMA = DATABASE() THEN NULL ELSE REFERENCED_TABLE_SCHEMA END,
                   REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME
            FROM information_schema.KEY_COLUMN_USAGE
            WHERE TABLE_SCHEMA = {schema} AND TABLE_NAME = {Literal(table)}
              AND REFERENCED_TABLE_NAME IS NOT NULL
            ORDER BY CONSTRAINT_NAME, ORDINAL_POSITION
            """,
            int.MaxValue,
            ct).ConfigureAwait(false);

        return rows.Rows
            .GroupBy(r => Text(r[0]))
            .Select(g =>
            {
                var parts = g.ToList();

                return new ForeignKeyInfo(
                    g.Key,
                    parts.Select(r => Text(r[2])).ToList(),
                    new DbObjectName(TextOrNull(parts[0][3]), Text(parts[0][4])),
                    parts.Select(r => Text(r[5])).ToList());
            })
            .ToList();
    }

    /// <summary>"int(10) unsigned" and "int unsigned" are the same integer; "tinyint(1)" is the boolean MySQL keeps as one.</summary>
    private static string Normalised(string dataType)
    {
        if (Regex.IsMatch(dataType, @"^\s*tinyint\(1\)", RegexOptions.IgnoreCase))
            return "bit";

        return Regex.Replace(dataType, @"\s+(unsigned|zerofill|signed)\b", "", RegexOptions.IgnoreCase).Trim();
    }

    private static string? TextOrNull(object? value) => value switch
    {
        null => null,
        string text => text,
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        _ => value.ToString(),
    };

    private static string Text(object? value) => TextOrNull(value) ?? "";

    /// <summary>A SQL string literal, with any quote or backslash in it escaped.</summary>
    private static string Literal(string text) =>
        "'" + text.Replace("\\", "\\\\").Replace("'", "''") + "'";
}
