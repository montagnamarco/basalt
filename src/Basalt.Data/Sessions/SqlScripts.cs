using System.Globalization;
using System.Text;
using Basalt.Core.Localization;

namespace Basalt.Data.Sessions;

/// <summary>
/// The SQL a user starts from when they ask to script a table or view: a SELECT
/// with the columns listed, INSERT, UPDATE and DELETE templates, and a CREATE.
/// </summary>
/// <remarks>
/// Every identifier is quoted by the engine's dialect, so a column named
/// <c>Order Date</c> or a table named <c>select</c> comes out valid. Values are
/// placeholders in angle brackets (<c>&lt;Name&gt;</c>): the text is a template
/// the user completes, not a statement to run as it is. UPDATE and DELETE are
/// limited by the primary key; a table with none gets a <c>&lt;condition&gt;</c>
/// placeholder, because writing no WHERE at all would be a statement that changes
/// every row.
/// </remarks>
public static class SqlScripts
{
    /// <summary>"SELECT" with each column on its own line, so the user can delete the ones they do not want.</summary>
    public static string Select(ISqlDialect dialect, TableDetails table)
    {
        if (table.Columns.Count == 0)
            return $"SELECT *\nFROM {dialect.QualifiedName(table.Name)};";

        var columns = string.Join(",\n", table.Columns.Select(c => "    " + dialect.Quote(c.Name)));

        return $"SELECT\n{columns}\nFROM {dialect.QualifiedName(table.Name)};";
    }

    /// <summary>
    /// INSERT with a placeholder per column. Columns the engine fills in itself (identity, serial,
    /// AUTO_INCREMENT) are left out, as they would be when a person writes the statement; a table made
    /// only of such columns keeps them, so the statement still names something.
    /// </summary>
    public static string Insert(ISqlDialect dialect, TableDetails table)
    {
        var columns = table.Columns.Where(c => !c.IsIdentity).ToList();

        if (columns.Count == 0) columns = [.. table.Columns];

        var names = string.Join(", ", columns.Select(c => dialect.Quote(c.Name)));
        var values = string.Join(", ", columns.Select(Placeholder));

        return $"INSERT INTO {dialect.QualifiedName(table.Name)} ({names})\nVALUES ({values});";
    }

    /// <summary>UPDATE of every column that is not part of the key, limited by the key.</summary>
    public static string Update(ISqlDialect dialect, TableDetails table)
    {
        var assigned = table.Columns.Where(c => !c.IsPrimaryKey).ToList();

        if (assigned.Count == 0) assigned = [.. table.Columns];

        var set = string.Join(",\n", assigned.Select(c => $"    {dialect.Quote(c.Name)} = {Placeholder(c)}"));

        return $"UPDATE {dialect.QualifiedName(table.Name)}\nSET\n{set}\nWHERE {Where(dialect, table)};";
    }

    /// <summary>DELETE limited by the key.</summary>
    public static string Delete(ISqlDialect dialect, TableDetails table) =>
        $"DELETE FROM {dialect.QualifiedName(table.Name)}\nWHERE {Where(dialect, table)};";

    /// <summary>
    /// CREATE for a table or view, as the engine can give it. SQLite keeps the statement it was created
    /// with, so that is returned as it is. For a table on any other engine the statement is written from the
    /// catalogue (columns, primary key, foreign keys); a view's definition is not in what the IDE reads, so
    /// it returns null, which the caller says rather than producing a wrong script.
    /// </summary>
    public static async Task<string?> CreateAsync(IDatabaseSession session, TableDetails table, CancellationToken ct = default)
    {
        if (session.Kind == DatabaseKind.Sqlite)
        {
            var literal = "'" + table.Name.Name.Replace("'", "''") + "'";
            var result = await session.ExecuteAsync(
                $"SELECT sql FROM sqlite_master WHERE name = {literal} AND type IN ('table', 'view')", 1, ct).ConfigureAwait(false);

            var sql = result.ResultSets.FirstOrDefault()?.Rows.FirstOrDefault()?.FirstOrDefault() as string;

            return string.IsNullOrWhiteSpace(sql) ? null : sql.TrimEnd().TrimEnd(';') + ";";
        }

        return table.Kind == DbObjectKind.Table ? CreateFromCatalogue(session.Dialect, table) : null;
    }

    /// <summary>
    /// A CREATE TABLE written from what the catalogue reports: columns with their identity, nullability and
    /// default, the primary key, the foreign keys, and the other indexes (unique ones as CREATE UNIQUE INDEX,
    /// which is how a unique constraint is kept by every engine here). The result says in a leading comment that it
    /// is approximate: check constraints, collations, referential actions, included columns and storage options
    /// are not read, so the script is a starting point to review, not a dump.
    /// </summary>
    public static string CreateFromCatalogue(ISqlDialect dialect, TableDetails table)
    {
        var lines = new List<string>();

        foreach (var column in table.Columns)
        {
            var line = new StringBuilder("    ").Append(dialect.Quote(column.Name)).Append(' ').Append(column.DataType);

            if (column.IsIdentity)
            {
                line.Append(dialect switch
                {
                    SqlServerDialect => " IDENTITY(1,1)",
                    PostgreSqlDialect => " GENERATED BY DEFAULT AS IDENTITY",
                    _ => "",
                });
            }

            if (!column.IsNullable) line.Append(" NOT NULL");

            if (column.IsIdentity && dialect is MySqlDialect) line.Append(" AUTO_INCREMENT");

            // An identity's default on PostgreSQL is its sequence (nextval), which GENERATED replaces.
            var keepDefault = !(column.IsIdentity && dialect is PostgreSqlDialect);

            if (keepDefault && column.DefaultValue is { Length: > 0 } fallback) line.Append(" DEFAULT ").Append(fallback);

            lines.Add(line.ToString());
        }

        var key = table.KeyColumns;

        if (key.Count > 0)
            lines.Add($"    PRIMARY KEY ({string.Join(", ", key.Select(c => dialect.Quote(c.Name)))})");

        foreach (var foreign in table.ForeignKeys)
        {
            lines.Add(
                $"    FOREIGN KEY ({string.Join(", ", foreign.Columns.Select(dialect.Quote))}) " +
                $"REFERENCES {dialect.QualifiedName(foreign.ReferencedTable)} ({string.Join(", ", foreign.ReferencedColumns.Select(dialect.Quote))})");
        }

        var script = new StringBuilder("-- Approximate: written from the catalogue. Check constraints, collations, referential actions\n")
            .Append("-- and storage options are not included; review it before running.\n")
            .Append($"CREATE TABLE {dialect.QualifiedName(table.Name)} (\n{string.Join(",\n", lines)}\n);");

        foreach (var index in table.Indexes.Where(i => !i.IsPrimaryKey))
        {
            // An expression index has no column names to write, so it is left out rather than written wrong.
            if (index.Columns.Any(c => c == "(expression)"))
            {
                script.Append($"\n-- Index {index.Name} is on an expression and is not scripted.");
                continue;
            }

            script.Append(
                $"\nCREATE {(index.IsUnique ? "UNIQUE " : "")}INDEX {dialect.Quote(index.Name)} " +
                $"ON {dialect.QualifiedName(table.Name)} ({string.Join(", ", index.Columns.Select(dialect.Quote))});");
        }

        return script.ToString();
    }

    private static string Placeholder(ColumnInfo column) => "<" + column.Name + ">";

    private static string Where(ISqlDialect dialect, TableDetails table)
    {
        var key = table.KeyColumns;

        return key.Count == 0
            ? "<condition>"
            : string.Join(" AND ", key.Select(c => $"{dialect.Quote(c.Name)} = {Placeholder(c)}"));
    }
}

/// <summary>What a table or view holds, counted: its rows from the data, the rest from its details.</summary>
public sealed record ObjectStatistics(DbObjectName Name, DbObjectKind Kind, long RowCount, int ColumnCount, int IndexCount, int ForeignKeyCount)
{
    /// <summary>
    /// Reads the counts through the session, so every engine does it on its own worker. The row count is a
    /// <c>COUNT(*)</c> written by the dialect's quoting: the engines agree on that statement.
    /// </summary>
    public static async Task<ObjectStatistics> ReadAsync(IDatabaseSession session, DbObjectName name, CancellationToken ct = default)
    {
        var details = await session.GetDetailsAsync(name, ct).ConfigureAwait(false);

        var result = await session.ExecuteAsync(
            $"SELECT COUNT(*) FROM {session.Dialect.QualifiedName(details.Name)}", 1, ct).ConfigureAwait(false);

        // A failed batch normally says why; if an engine fails without a message, say that much.
        if (result.Failed)
        {
            var reason = result.Messages.FirstOrDefault(m => m.IsError)?.Text;

            throw new InvalidOperationException(string.IsNullOrWhiteSpace(reason) ? Localizer.Get(StringKeys.DbCountFailed) : reason);
        }

        // No row is not "zero rows": COUNT(*) always answers, so an answer that is missing is a failure.
        var count = result.ResultSets.FirstOrDefault()?.Rows.FirstOrDefault()?.FirstOrDefault()
            ?? throw new InvalidOperationException(Localizer.Get(StringKeys.DbCountFailed));

        return new ObjectStatistics(
            details.Name,
            details.Kind,
            Convert.ToInt64(count, CultureInfo.InvariantCulture),
            details.Columns.Count,
            details.Indexes.Count,
            details.ForeignKeys.Count);
    }
}
