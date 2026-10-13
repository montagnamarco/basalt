using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Basalt.Core.Localization;
using Npgsql;
using NpgsqlTypes;

namespace Basalt.Data.Sessions;

/// <summary>
/// A session on one PostgreSQL database. Its catalogue is information_schema for
/// the tables and views, and pg_catalog for their columns, indexes and keys.
/// </summary>
/// <remarks>
/// Not sealed, so that a test can expose the hooks; nothing else derives from it.
/// </remarks>
public class PostgreSqlSession : AdoNetSession
{
    public PostgreSqlSession(DbConnection connection, string displayName)
        : base(connection, displayName)
    {
    }

    public override DatabaseKind Kind { get; } = DatabaseKind.PostgreSql;

    public override ISqlDialect Dialect { get; } = new PostgreSqlDialect();

    /// <summary>
    /// RAISE NOTICE arrives on the connection's Notice event while the batch runs.
    /// </summary>
    protected override IDisposable? ListenForMessages(DbCommand command, Action<QueryMessage> report)
    {
        if (command.Connection is not NpgsqlConnection pg) return null;

        NoticeEventHandler handler = (_, e) => report(new QueryMessage(e.Notice.MessageText, false, null));

        pg.Notice += handler;

        return new EventSubscription(() => pg.Notice -= handler);
    }

    /// <summary>
    /// PostgreSQL gives the error as a character position counted from 1 in the
    /// statement that failed, so the batch line starts from that statement's start.
    /// </summary>
    protected override int? ErrorLine(DbException exception, string batch, int statement)
    {
        if (exception is not PostgresException { Position: > 0 } error) return null;

        var starts = StatementStarts(batch);
        var start = statement < starts.Count ? starts[statement] : 0;

        return LineOf(batch, start + error.Position);
    }

    /// <summary>
    /// Where each statement of a batch starts, split the way the client splits it: at each
    /// semicolon outside a quoted name or string, a comment, or a dollar-quoted body. A piece
    /// of white space alone is not a statement.
    /// </summary>
    public static IReadOnlyList<int> StatementStarts(string batch)
    {
        var starts = new List<int>();
        var start = -1;
        var i = 0;

        while (i < batch.Length)
        {
            var c = batch[i];

            if (c == ';')
            {
                if (start >= 0) starts.Add(start);
                start = -1;
                i++;
                continue;
            }

            if (start < 0 && !char.IsWhiteSpace(c)) start = i;

            if (c is '\'' or '"')
            {
                i = EndOfQuoted(batch, i, c);
            }
            else if (c == '-' && i + 1 < batch.Length && batch[i + 1] == '-')
            {
                var line = batch.IndexOf('\n', i);
                i = line < 0 ? batch.Length : line + 1;
            }
            else if (c == '/' && i + 1 < batch.Length && batch[i + 1] == '*')
            {
                var close = batch.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? batch.Length : close + 2;
            }
            else if (c == '$' && DollarTag(batch, i) is { } tag)
            {
                var close = batch.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                i = close < 0 ? batch.Length : close + tag.Length;
            }
            else
            {
                i++;
            }
        }

        if (start >= 0) starts.Add(start);

        return starts;
    }

    /// <summary>The index just past a quoted name or string that opens at <paramref name="open"/>; a doubled quote is part of it.</summary>
    private static int EndOfQuoted(string batch, int open, char quote)
    {
        var i = open + 1;

        while (true)
        {
            var close = batch.IndexOf(quote, i);

            if (close < 0) return batch.Length;
            if (close + 1 < batch.Length && batch[close + 1] == quote)
            {
                i = close + 2;
                continue;
            }

            return close + 1;
        }
    }

    /// <summary>The dollar quote that opens at <paramref name="at"/>, as "$tag$", or null when the dollar is not one.</summary>
    private static string? DollarTag(string batch, int at)
    {
        // A dollar inside a word (a$b) or a parameter marker ($1) is not a quote.
        if (at > 0 && (char.IsLetterOrDigit(batch[at - 1]) || batch[at - 1] == '_')) return null;

        var end = at + 1;

        while (end < batch.Length && (char.IsLetterOrDigit(batch[end]) || batch[end] == '_')) end++;

        if (end >= batch.Length || batch[end] != '$') return null;
        if (end > at + 1 && char.IsDigit(batch[at + 1])) return null;

        return batch.Substring(at, end - at + 1);
    }

    /// <summary>The one-based line that holds a one-based character position.</summary>
    private static int LineOf(string batch, int position)
    {
        var line = 1;
        var end = Math.Min(position - 1, batch.Length);

        for (var i = 0; i < end; i++)
        {
            if (batch[i] == '\n') line++;
        }

        return line;
    }

    /// <summary>
    /// The value a typed text becomes for a column, in the CLR type that
    /// PostgreSQL's parameter for that column takes: int for integer, short for
    /// smallint, float for real, and DateTimeOffset for a timestamp with a zone,
    /// which Npgsql refuses as an unspecified DateTime.
    /// </summary>
    protected override object? ConvertValue(string? text, ColumnInfo column)
    {
        if (text is null) return null;

        var stem = TypeStem(column.DataType);

        if (stem is "timestamp with time zone" or "timestamptz")
        {
            // Converted to UTC: a timestamp with a zone with any other offset is refused by Npgsql. Text with no
            // zone is taken as local time, and stored at its UTC instant.
            return DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var offset)
                ? offset.ToUniversalTime()
                : throw BadValue(text, ValueKind.Date, column);
        }

        var value = base.ConvertValue(text, column);

        switch (stem, value)
        {
            case ("integer" or "int" or "int4" or "serial", long number):
                return number is >= int.MinValue and <= int.MaxValue
                    ? (int)number
                    : throw BadValue(text, ValueKind.Integer, column);

            case ("smallint" or "int2" or "smallserial", long number):
                return number is >= short.MinValue and <= short.MaxValue
                    ? (short)number
                    : throw BadValue(text, ValueKind.Integer, column);

            case ("real" or "float4", double real):
                return (float)real;

            default:
                return value;
        }
    }

    /// <summary>
    /// Types the parameter from the column's type, because PostgreSQL refuses a
    /// text parameter for an integer column. A string for a type that is not
    /// listed (an enum, a network address) is sent untyped, so PostgreSQL
    /// converts it to the column's own type.
    /// </summary>
    protected override DbParameter CreateParameter(DbCommand command, string name, object? value, ColumnInfo column)
    {
        var parameter = new NpgsqlParameter(name, value ?? DBNull.Value);

        if (value is not null && TypeOf(column.DataType, value) is { } type)
            parameter.NpgsqlDbType = type;

        return parameter;
    }

    protected override async Task<IReadOnlyList<DbObjectInfo>> ReadObjectsAsync(DbConnection connection, CancellationToken ct)
    {
        // The engine's own schemas, and the ones it keeps for TOAST and for other sessions' temporary tables, are left out.
        var rows = await ReadOneSetAsync(
            connection,
            """
            SELECT table_schema, table_name, table_type
            FROM information_schema.tables
            WHERE table_type IN ('BASE TABLE', 'VIEW')
              AND table_schema NOT IN ('pg_catalog', 'information_schema')
              AND table_schema NOT LIKE 'pg\_toast%'
              AND table_schema NOT LIKE 'pg\_temp%'
            ORDER BY table_schema, table_name
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
        // The table's oid anchors the pg_catalog queries after this one. Without a schema, the table is the one on the search path.
        var schemaFilter = name.Schema is null ? "current_schema()" : Literal(name.Schema);

        var found = await ReadOneSetAsync(
            connection,
            $"""
            SELECT t.table_schema, t.table_name, t.table_type, c.oid
            FROM information_schema.tables t
            JOIN pg_catalog.pg_namespace n ON n.nspname = t.table_schema
            JOIN pg_catalog.pg_class c ON c.relnamespace = n.oid AND c.relname = t.table_name
            WHERE t.table_schema = {schemaFilter} AND t.table_name = {Literal(name.Name)}
              AND t.table_type IN ('BASE TABLE', 'VIEW')
            """,
            int.MaxValue,
            ct).ConfigureAwait(false);

        if (found.Rows.Count == 0)
            throw new InvalidOperationException(Localizer.Get(StringKeys.DatabaseNoObject, name.ToString()));

        var canonical = new DbObjectName(Text(found.Rows[0][0]), Text(found.Rows[0][1]));
        var kind = Text(found.Rows[0][2]) == "VIEW" ? DbObjectKind.View : DbObjectKind.Table;
        var oid = Convert.ToUInt32(found.Rows[0][3], CultureInfo.InvariantCulture);

        var indexes = await ReadIndexesAsync(connection, oid, ct).ConfigureAwait(false);
        var primaryKey = indexes.Where(i => i.IsPrimaryKey).SelectMany(i => i.Columns).ToHashSet(StringComparer.Ordinal);

        // format_type gives the type as a user writes it: "character varying(50)", "numeric(10,2)".
        // A column is an identity when it is GENERATED, or when its default draws from a sequence, which is what serial does.
        var columnRows = await ReadOneSetAsync(
            connection,
            $"""
            SELECT a.attname,
                   format_type(a.atttypid, a.atttypmod),
                   NOT a.attnotnull,
                   a.attidentity IN ('a', 'd') OR COALESCE(pg_catalog.pg_get_expr(d.adbin, d.adrelid) LIKE 'nextval(%', false),
                   pg_catalog.pg_get_expr(d.adbin, d.adrelid)
            FROM pg_catalog.pg_attribute a
            LEFT JOIN pg_catalog.pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
            WHERE a.attrelid = {oid}::oid AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY a.attnum
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

        var foreignKeys = await ReadForeignKeysAsync(connection, oid, ct).ConfigureAwait(false);

        return new TableDetails(canonical, kind, columns, indexes, foreignKeys);
    }

    private async Task<IReadOnlyList<IndexInfo>> ReadIndexesAsync(DbConnection connection, uint oid, CancellationToken ct)
    {
        // indkey is an int2vector, so it is cast to an array to unnest it. Only the key columns count:
        // INCLUDE columns come after indnkeyatts and are not part of the index's key. An expression has no column (attnum 0).
        var rows = await ReadOneSetAsync(
            connection,
            $"""
            SELECT ic.relname, i.indisunique, i.indisprimary, a.attname
            FROM pg_catalog.pg_index i
            JOIN pg_catalog.pg_class ic ON ic.oid = i.indexrelid
            CROSS JOIN LATERAL unnest(i.indkey::int2[]) WITH ORDINALITY AS k(attnum, ord)
            LEFT JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
            WHERE i.indrelid = {oid}::oid AND k.ord <= i.indnkeyatts
            ORDER BY ic.relname, k.ord
            """,
            int.MaxValue,
            ct).ConfigureAwait(false);

        return rows.Rows
            .GroupBy(r => Text(r[0]))
            .Select(g => new IndexInfo(
                g.Key,
                g.Select(r => TextOrNull(r[3]) ?? "(expression)").ToList(),
                Convert.ToBoolean(g.First()[1], CultureInfo.InvariantCulture),
                Convert.ToBoolean(g.First()[2], CultureInfo.InvariantCulture)))
            .ToList();
    }

    private async Task<IReadOnlyList<ForeignKeyInfo>> ReadForeignKeysAsync(DbConnection connection, uint oid, CancellationToken ct)
    {
        // One row per column pair: the table's column and the column it points at, in the constraint's order.
        var rows = await ReadOneSetAsync(
            connection,
            $"""
            SELECT c.conname, k.ord, a.attname, rn.nspname, rt.relname, ra.attname
            FROM pg_catalog.pg_constraint c
            JOIN pg_catalog.pg_class rt ON rt.oid = c.confrelid
            JOIN pg_catalog.pg_namespace rn ON rn.oid = rt.relnamespace
            CROSS JOIN LATERAL unnest(c.conkey, c.confkey) WITH ORDINALITY AS k(attnum, refnum, ord)
            JOIN pg_catalog.pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.attnum
            JOIN pg_catalog.pg_attribute ra ON ra.attrelid = c.confrelid AND ra.attnum = k.refnum
            WHERE c.contype = 'f' AND c.conrelid = {oid}::oid
            ORDER BY c.conname, k.ord
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

    /// <summary>
    /// The NpgsqlDbType for a column's type, when the value's CLR type is the one
    /// that type takes; null leaves Npgsql to infer it from the value.
    /// </summary>
    private static NpgsqlDbType? TypeOf(string dataType, object value) => TypeStem(dataType) switch
    {
        "integer" or "int" or "int4" or "serial" => NpgsqlDbType.Integer,
        "bigint" or "int8" or "bigserial" => NpgsqlDbType.Bigint,
        "smallint" or "int2" or "smallserial" => NpgsqlDbType.Smallint,
        "numeric" or "decimal" => NpgsqlDbType.Numeric,
        "real" or "float4" => NpgsqlDbType.Real,
        "double precision" or "float8" => NpgsqlDbType.Double,
        "boolean" or "bool" => NpgsqlDbType.Boolean,
        "timestamp without time zone" or "timestamp" => NpgsqlDbType.Timestamp,
        "timestamp with time zone" or "timestamptz" => NpgsqlDbType.TimestampTz,
        "date" => NpgsqlDbType.Date,
        "uuid" => NpgsqlDbType.Uuid,
        "bytea" => NpgsqlDbType.Bytea,
        "json" => NpgsqlDbType.Json,
        "jsonb" => NpgsqlDbType.Jsonb,
        "text" or "character varying" or "varchar" or "character" or "char" or "bpchar" => NpgsqlDbType.Text,

        // A type not listed takes a text only as PostgreSQL's own literal, which it converts to the column's type.
        _ => value is string ? NpgsqlDbType.Unknown : null,
    };

    /// <summary>
    /// The type's name without its length or precision, lower-cased, with single
    /// spaces: "numeric(10,2)" is "numeric", "timestamp(3) with time zone" is
    /// "timestamp with time zone".
    /// </summary>
    private static string TypeStem(string dataType) =>
        string.Join(' ', Regex.Replace(dataType, @"\([^)]*\)", " ")
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string? TextOrNull(object? value) => value switch
    {
        null => null,
        string text => text,
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        _ => value.ToString(),
    };

    private static string Text(object? value) => TextOrNull(value) ?? "";

    /// <summary>A SQL string literal, with any quote in it doubled.</summary>
    private static string Literal(string text) => "'" + text.Replace("'", "''") + "'";
}
