using System.Text;

namespace Basalt.Data.Sessions;

/// <summary>
/// PostgreSQL's spelling of the SQL the IDE generates: double-quoted identifiers,
/// and LIMIT/OFFSET for a page.
/// </summary>
public sealed class PostgreSqlDialect : ISqlDialect
{
    public string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    /// <summary>A name without a schema is found through the search path, as the engine would find it.</summary>
    public string QualifiedName(DbObjectName name) =>
        name.Schema is null ? Quote(name.Name) : $"{Quote(name.Schema)}.{Quote(name.Name)}";

    public string SelectPage(TableDetails table, int offset, int limit, IReadOnlyList<SortSpec> sort, string? filter)
    {
        SqlFilter.CheckPage(offset, limit);

        var builder = new StringBuilder($"SELECT * FROM {QualifiedName(table.Name)}{SqlFilter.Where(filter)}");

        // Without a sort the page is taken by the key, so rows come back in a stable order.
        var order = sort.Count > 0
            ? sort.Select(s => Quote(s.Column) + (s.Descending ? " DESC" : ""))
            : table.KeyColumns.Select(c => Quote(c.Name));

        var terms = order.ToList();

        if (terms.Count > 0)
            builder.Append(" ORDER BY ").Append(string.Join(", ", terms));

        builder.Append($" LIMIT {limit} OFFSET {offset}");

        return builder.ToString();
    }

    public string SelectTop(DbObjectName name, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);

        return $"SELECT * FROM {QualifiedName(name)} LIMIT {rows}";
    }
}
