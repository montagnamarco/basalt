using System.Text;

namespace Basalt.Data.Sessions;

/// <summary>
/// SQL Server's spelling of the SQL the IDE generates: bracketed identifiers,
/// OFFSET/FETCH for a page, and TOP for the starting select.
/// </summary>
public sealed class SqlServerDialect : ISqlDialect
{
    /// <summary>A bracketed identifier; a ] inside it is doubled.</summary>
    public string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    /// <summary>The schema is always written; SQL Server's default is dbo, and a name without one is only the table's own.</summary>
    public string QualifiedName(DbObjectName name) =>
        name.Schema is null ? Quote(name.Name) : $"{Quote(name.Schema)}.{Quote(name.Name)}";

    public string SelectPage(TableDetails table, int offset, int limit, IReadOnlyList<SortSpec> sort, string? filter)
    {
        SqlFilter.CheckPage(offset, limit);

        var builder = new StringBuilder($"SELECT * FROM {QualifiedName(table.Name)}{SqlFilter.Where(filter)}");

        // OFFSET/FETCH is only valid after an ORDER BY, so one is always written.
        builder.Append(" ORDER BY ").Append(OrderBy(table, sort));
        builder.Append($" OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY");

        return builder.ToString();
    }

    public string SelectTop(DbObjectName name, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);

        return $"SELECT TOP ({rows}) * FROM {QualifiedName(name)}";
    }

    /// <summary>
    /// The sort asked for; else the key, so a page is stable; else the first
    /// column; else (SELECT NULL), which is valid and orders nothing.
    /// </summary>
    private string OrderBy(TableDetails table, IReadOnlyList<SortSpec> sort)
    {
        if (sort.Count > 0)
            return string.Join(", ", sort.Select(s => Quote(s.Column) + (s.Descending ? " DESC" : "")));

        if (table.KeyColumns.Count > 0)
            return string.Join(", ", table.KeyColumns.Select(c => Quote(c.Name)));

        if (table.Columns.Count > 0)
            return Quote(table.Columns[0].Name);

        return "(SELECT NULL)";
    }
}
