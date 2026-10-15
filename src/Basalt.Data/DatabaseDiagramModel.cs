namespace Basalt.Data.Sessions;

/// <summary>A column as the diagram's box lists it.</summary>
public sealed record DiagramColumn(string Name, string DataType, bool IsNullable, bool IsPrimaryKey, bool IsForeignKey);

/// <summary>A table as the diagram draws it: a box with a title and its columns.</summary>
public sealed record DiagramTable(DbObjectName Name, IReadOnlyList<DiagramColumn> Columns);

/// <summary>A foreign key between two tables of the diagram.</summary>
public sealed record DiagramRelation(
    string Name,
    DbObjectName FromTable,
    IReadOnlyList<string> FromColumns,
    DbObjectName ToTable,
    IReadOnlyList<string> ToColumns);

/// <summary>A point in the diagram's own coordinates, before pan and zoom.</summary>
public sealed record DiagramPoint(double X, double Y);

/// <summary>
/// The tables of a database and the foreign keys between them: what the diagram draws.
/// </summary>
/// <remarks>
/// Read through the session, one table at a time, so the whole read runs on the session's worker and the interface
/// thread never waits for it. Views are left out (they have no keys to draw). A foreign key to a table outside
/// the list has nothing to point at and is left out as well.
/// </remarks>
public sealed record DatabaseDiagramModel(IReadOnlyList<DiagramTable> Tables, IReadOnlyList<DiagramRelation> Relations)
{
    public static async Task<DatabaseDiagramModel> ReadAsync(IDatabaseSession session, CancellationToken ct = default)
    {
        var objects = await session.GetObjectsAsync(ct).ConfigureAwait(false);
        var tables = new List<DiagramTable>();
        var relations = new List<DiagramRelation>();
        var details = new List<TableDetails>();

        foreach (var info in objects.Where(o => o.Kind == DbObjectKind.Table))
            details.Add(await session.GetDetailsAsync(info.Name, ct).ConfigureAwait(false));

        foreach (var table in details)
        {
            var foreign = table.ForeignKeys
                .SelectMany(k => k.Columns)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            tables.Add(new DiagramTable(
                table.Name,
                [.. table.Columns.Select(c => new DiagramColumn(c.Name, c.DataType, c.IsNullable, c.IsPrimaryKey, foreign.Contains(c.Name)))]));
        }

        foreach (var table in details)
        {
            foreach (var key in table.ForeignKeys)
            {
                var target = Find(tables, key.ReferencedTable);

                if (target is null) continue;

                relations.Add(new DiagramRelation(key.Name, table.Name, key.Columns, target.Name, key.ReferencedColumns));
            }
        }

        return new DatabaseDiagramModel(tables, relations);
    }

    /// <summary>
    /// The table a foreign key points at. SQLite names no schema for it, so a reference without one matches by
    /// name alone, as the engine resolves it.
    /// </summary>
    private static DiagramTable? Find(IReadOnlyList<DiagramTable> tables, DbObjectName name) =>
        tables.FirstOrDefault(t =>
            string.Equals(t.Name.Name, name.Name, StringComparison.OrdinalIgnoreCase)
            && (name.Schema is null || string.Equals(t.Name.Schema, name.Schema, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>
/// The size of a table's box, shared by the layout (which must leave room) and the drawing (which must agree).
/// </summary>
public static class DiagramGeometry
{
    public const double BoxWidth = 210;
    public const double HeaderHeight = 26;
    public const double RowHeight = 18;
    public const double BoxPadding = 6;
    public const double GapX = 80;
    public const double GapY = 30;

    /// <summary>Columns drawn before the box says how many more there are; a table of two hundred columns would otherwise fill the screen.</summary>
    public const int MaxVisibleColumns = 24;

    /// <summary>The rows a box draws: its columns up to the cap, and one line "… N more" after them.</summary>
    public static int VisibleRows(DiagramTable table) =>
        table.Columns.Count <= MaxVisibleColumns ? Math.Max(1, table.Columns.Count) : MaxVisibleColumns + 1;

    public static double BoxHeight(DiagramTable table) => HeaderHeight + VisibleRows(table) * RowHeight + BoxPadding;

    /// <summary>The row a column is drawn on; a column past the cap shares the "more" row.</summary>
    public static int RowOf(DiagramTable table, string column)
    {
        var index = table.Columns.ToList().FindIndex(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));

        return Math.Clamp(index < 0 ? 0 : index, 0, VisibleRows(table) - 1);
    }

    /// <summary>The height of the middle of a row, from the top of the box.</summary>
    public static double RowCentre(int row) => HeaderHeight + row * RowHeight + RowHeight / 2;
}

/// <summary>
/// Where the boxes start: layered by the foreign keys, referenced tables on the left and the tables that point at
/// them to their right, each layer a column of boxes.
/// </summary>
public static class DiagramLayoutEngine
{
    /// <summary>A column of boxes is started anew past this height, so a layer of fifty tables is not one tall strip.</summary>
    private const double MaxColumnHeight = 1100;

    public static IReadOnlyDictionary<DbObjectName, DiagramPoint> Auto(DatabaseDiagramModel model)
    {
        var depth = model.Tables.ToDictionary(t => t.Name, _ => 0);

        // Longest path from a table nothing is referenced by: a cycle is cut by the number of passes.
        for (var pass = 0; pass < model.Tables.Count; pass++)
        {
            var moved = false;

            foreach (var relation in model.Relations)
            {
                if (relation.FromTable == relation.ToTable) continue;
                if (!depth.TryGetValue(relation.FromTable, out var from) || !depth.TryGetValue(relation.ToTable, out var to)) continue;
                if (from > to) continue;

                depth[relation.FromTable] = to + 1;
                moved = true;
            }

            if (!moved) break;
        }

        var positions = new Dictionary<DbObjectName, DiagramPoint>();
        var x = 0.0;

        foreach (var layer in model.Tables.GroupBy(t => depth[t.Name]).OrderBy(g => g.Key))
        {
            var y = 0.0;

            foreach (var table in layer.OrderBy(t => t.Name.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                var height = DiagramGeometry.BoxHeight(table);

                if (y > 0 && y + height > MaxColumnHeight)
                {
                    x += DiagramGeometry.BoxWidth + DiagramGeometry.GapX;
                    y = 0;
                }

                positions[table.Name] = new DiagramPoint(x, y);
                y += height + DiagramGeometry.GapY;
            }

            x += DiagramGeometry.BoxWidth + DiagramGeometry.GapX;
        }

        return positions;
    }

    /// <summary>
    /// The saved positions for the tables that have one, the automatic position for the rest (a table created
    /// since the layout was saved), and nothing for a saved table that no longer exists.
    /// </summary>
    public static IReadOnlyDictionary<DbObjectName, DiagramPoint> Merge(
        DatabaseDiagramModel model,
        IReadOnlyDictionary<string, DiagramPoint> saved)
    {
        var automatic = Auto(model);
        var merged = new Dictionary<DbObjectName, DiagramPoint>();

        foreach (var table in model.Tables)
            merged[table.Name] = saved.TryGetValue(table.Name.ToString(), out var point) ? point : automatic[table.Name];

        return merged;
    }
}
