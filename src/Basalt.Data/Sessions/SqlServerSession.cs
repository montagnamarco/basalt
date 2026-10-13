using System.Data.Common;
using System.Globalization;
using System.Text;
using Basalt.Core.Localization;
using Microsoft.Data.SqlClient;

namespace Basalt.Data.Sessions;

/// <summary>
/// A session on one SQL Server database. Its catalogue is INFORMATION_SCHEMA for
/// the names, and sys.columns, sys.identity_columns, sys.indexes, sys.index_columns
/// and sys.foreign_keys for the details.
/// </summary>
/// <remarks>
/// Not sealed, so that a test can expose the hooks; nothing else derives from it.
/// </remarks>
public class SqlServerSession : AdoNetSession
{
    public SqlServerSession(DbConnection connection, string displayName)
        : base(connection, displayName)
    {
    }

    public override DatabaseKind Kind { get; } = DatabaseKind.SqlServer;

    public override ISqlDialect Dialect { get; } = new SqlServerDialect();

    /// <summary>
    /// RecordsAffected is filled only when a reader closes, so a batch takes its
    /// per-statement counts from <see cref="SqlCommand.StatementCompleted"/> instead.
    /// </summary>
    protected override bool ReportsRowCounts => false;

    /// <summary>
    /// PRINT output arrives on <see cref="SqlConnection.InfoMessage"/>, and each
    /// statement's row count on <see cref="SqlCommand.StatementCompleted"/>, while the batch runs.
    /// </summary>
    protected override IDisposable? ListenForMessages(DbCommand command, Action<QueryMessage> report)
    {
        if (command is not SqlCommand sql || sql.Connection is not SqlConnection connection) return null;

        SqlInfoMessageEventHandler info = (_, e) =>
        {
            // PRINT carries its batch line on the error that holds the message, when SQL Server gives one.
            var line = e.Errors.Count > 0 && e.Errors[0].LineNumber > 0 ? e.Errors[0].LineNumber : (int?)null;
            report(new QueryMessage(e.Message, false, line));
        };

        // The event is declared in System.Data, which SqlClient raises.
        System.Data.StatementCompletedEventHandler completed = (_, e) =>
        {
            // -1 is a statement that counts nothing, such as a DDL statement, or any statement under SET NOCOUNT ON.
            if (e.RecordCount < 0) return;

            report(new QueryMessage(
                Localizer.Get(e.RecordCount == 1 ? StringKeys.DatabaseRowAffected : StringKeys.DatabaseRowsAffected, e.RecordCount),
                false,
                null));
        };

        connection.InfoMessage += info;
        sql.StatementCompleted += completed;

        return new EventSubscription(() =>
        {
            connection.InfoMessage -= info;
            sql.StatementCompleted -= completed;
        });
    }

    /// <summary>
    /// The statement's count from @@ROWCOUNT, read in the same command: under
    /// SET NOCOUNT ON, which a user's own SQL or a server default can leave on,
    /// ExecuteNonQuery returns -1, and a row that vanished since it was read went
    /// unreported while its edit did nothing. @@ROWCOUNT is the statement's own
    /// count after its triggers too: measured on SQL Server 2022, an update of one
    /// row whose trigger inserts three reads 1. The count is the last result
    /// set, not the first: a trigger may return rows of its own before it.
    /// </summary>
    protected override async Task<int> ExecuteEditAsync(DbCommand command, CancellationToken ct)
    {
        command.CommandText += "\nSELECT @@ROWCOUNT;";

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        object? count = null;

        do
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                count = reader.FieldCount > 0 ? reader.GetValue(0) : null;
        }
        while (await reader.NextResultAsync(ct).ConfigureAwait(false));

        return Convert.ToInt32(count, CultureInfo.InvariantCulture);
    }

    /// <summary>The line SQL Server gives for an error in the batch.</summary>
    protected override int? ErrorLine(DbException exception) =>
        exception is SqlException { LineNumber: > 0 } error ? error.LineNumber : null;

    protected override async Task<IReadOnlyList<DbObjectInfo>> ReadObjectsAsync(DbConnection connection, CancellationToken ct)
    {
        // Schemas that belong to the engine are left out, and so are the objects Microsoft ships in user schemas.
        var rows = await ReadOneSetAsync(
            connection,
            """
            SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE IN ('BASE TABLE', 'VIEW')
              AND TABLE_SCHEMA NOT IN ('sys', 'INFORMATION_SCHEMA')
              AND OBJECTPROPERTY(OBJECT_ID(QUOTENAME(TABLE_SCHEMA) + N'.' + QUOTENAME(TABLE_NAME)), N'IsMSShipped') = 0
            ORDER BY TABLE_SCHEMA, TABLE_NAME
            """,
            int.MaxValue,
            ct).ConfigureAwait(false);

        return rows.Rows
            .Select(r => new DbObjectInfo(
                new DbObjectName(Text(r[0]), Text(r[1])),
                Text(r[2]) == "VIEW" ? DbObjectKind.View : DbObjectKind.Table))
            .ToList();
    }

    protected override async Task<TableDetails> ReadDetailsAsync(DbConnection connection, DbObjectName name, CancellationToken ct)
    {
        var schemaName = name.Schema ?? "dbo";

        // The object's id anchors every sys query after this one, so they describe the same object.
        var found = await ReadOneSetAsync(
            connection,
            $"""
            SELECT s.name, o.name, o.object_id, o.type
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE s.name = {Literal(schemaName)} AND o.name = {Literal(name.Name)}
              AND o.type IN ('U', 'V') AND o.is_ms_shipped = 0
            """,
            int.MaxValue,
            ct).ConfigureAwait(false);

        if (found.Rows.Count == 0)
            throw new InvalidOperationException(Localizer.Get(StringKeys.DatabaseNoObject, name.ToString()));

        var canonical = new DbObjectName(Text(found.Rows[0][0]), Text(found.Rows[0][1]));
        var id = Convert.ToInt32(found.Rows[0][2], CultureInfo.InvariantCulture);
        var kind = Text(found.Rows[0][3]).Trim() == "V" ? DbObjectKind.View : DbObjectKind.Table;

        var indexes = await ReadIndexesAsync(connection, id, ct).ConfigureAwait(false);
        var primaryKey = indexes.Where(i => i.IsPrimaryKey).SelectMany(i => i.Columns).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The type is written as a user would write it: nvarchar(50), decimal(10,2), datetime2(3).
        var columnRows = await ReadOneSetAsync(
            connection,
            $"""
            SELECT c.name,
                   CASE
                       WHEN t.name IN ('nvarchar', 'nchar') THEN t.name + '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CONVERT(varchar(10), c.max_length / 2) END + ')'
                       WHEN t.name IN ('varchar', 'char', 'varbinary', 'binary') THEN t.name + '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CONVERT(varchar(10), c.max_length) END + ')'
                       WHEN t.name IN ('decimal', 'numeric') THEN t.name + '(' + CONVERT(varchar(10), c.precision) + ',' + CONVERT(varchar(10), c.scale) + ')'
                       WHEN t.name IN ('datetime2', 'datetimeoffset', 'time') THEN t.name + '(' + CONVERT(varchar(10), c.scale) + ')'
                       ELSE t.name
                   END,
                   c.is_nullable,
                   CASE WHEN ic.column_id IS NULL THEN 0 ELSE 1 END,
                   dc.definition
            FROM sys.columns c
            JOIN sys.types t ON t.user_type_id = c.user_type_id
            LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE c.object_id = {id}
            ORDER BY c.column_id
            """,
            int.MaxValue,
            ct).ConfigureAwait(false);

        var columns = columnRows.Rows
            .Select((r, index) =>
            {
                var columnName = Text(r[0]);

                return new ColumnInfo(
                    columnName,
                    Text(r[1]),
                    Convert.ToBoolean(r[2], CultureInfo.InvariantCulture),
                    primaryKey.Contains(columnName),
                    Convert.ToBoolean(r[3], CultureInfo.InvariantCulture),
                    TextOrNull(r[4]),
                    index + 1);
            })
            .ToList();

        var foreignKeys = await ReadForeignKeysAsync(connection, id, ct).ConfigureAwait(false);

        return new TableDetails(canonical, kind, columns, indexes, foreignKeys);
    }

    private async Task<IReadOnlyList<IndexInfo>> ReadIndexesAsync(DbConnection connection, int id, CancellationToken ct)
    {
        // Heaps have no index name and no key, so they are left out; included (non-key) columns are not part of the index's key.
        var rows = await ReadOneSetAsync(
            connection,
            $"""
            SELECT i.name, i.is_unique, i.is_primary_key, c.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = {id} AND i.type > 0 AND i.name IS NOT NULL
            ORDER BY i.name, ic.key_ordinal
            """,
            int.MaxValue,
            ct).ConfigureAwait(false);

        return rows.Rows
            .GroupBy(r => Text(r[0]))
            .Select(g => new IndexInfo(
                g.Key,
                g.Select(r => Text(r[3])).ToList(),
                Convert.ToBoolean(g.First()[1], CultureInfo.InvariantCulture),
                Convert.ToBoolean(g.First()[2], CultureInfo.InvariantCulture)))
            .ToList();
    }

    private async Task<IReadOnlyList<ForeignKeyInfo>> ReadForeignKeysAsync(DbConnection connection, int id, CancellationToken ct)
    {
        var rows = await ReadOneSetAsync(
            connection,
            $"""
            SELECT fk.name, fkc.constraint_column_id, pc.name, rs.name, rt.name, rc.name
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.tables rt ON rt.object_id = fkc.referenced_object_id
            JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fk.parent_object_id = {id}
            ORDER BY fk.name, fkc.constraint_column_id
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
                    new DbObjectName(Text(parts[0][3]), Text(parts[0][4])),
                    parts.Select(r => Text(r[5])).ToList());
            })
            .ToList();
    }

    private static string? TextOrNull(object? value) => value switch
    {
        null => null,
        string text => text,
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        _ => value.ToString(),
    };

    private static string Text(object? value) => TextOrNull(value) ?? "";

    /// <summary>A SQL Unicode string literal, with any quote in it doubled.</summary>
    private static string Literal(string text) => "N'" + text.Replace("'", "''") + "'";
}
