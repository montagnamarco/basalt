using System.Text;

namespace Basalt.Data.Sessions;

/// <summary>
/// SQLite's spelling of the SQL the IDE generates: double-quoted identifiers,
/// and LIMIT/OFFSET for a page.
/// </summary>
public sealed class SqliteDialect : ISqlDialect
{
    public string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    /// <summary>SQLite has no schema unless one is attached, so the name is usually one part.</summary>
    public string QualifiedName(DbObjectName name) =>
        name.Schema is null ? Quote(name.Name) : $"{Quote(name.Schema)}.{Quote(name.Name)}";

    public string SelectPage(TableDetails table, int offset, int limit, IReadOnlyList<SortSpec> sort, string? filter)
    {
        SqlFilter.CheckPage(offset, limit);

        var builder = new StringBuilder($"SELECT * FROM {QualifiedName(table.Name)}{SqlFilter.Where(filter)}");

        // Without a sort the page is taken by the key: rows come back in no fixed
        // order otherwise, and a page could repeat one row and miss another.
        var order = sort.Count > 0
            ? sort.Select(s => Quote(s.Column) + (s.Descending ? " DESC" : ""))
            : table.KeyColumns.Select(c => Quote(c.Name));

        var terms = order.ToList();

        if (terms.Count > 0)
            builder.Append(" ORDER BY ").Append(string.Join(", ", terms));

        builder.Append($" LIMIT {limit} OFFSET {offset}");

        return builder.ToString();
    }

    public string SelectTop(DbObjectName name, int rows) => $"SELECT * FROM {QualifiedName(name)} LIMIT {rows}";
}
