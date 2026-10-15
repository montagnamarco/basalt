using Basalt.Core.Localization;

namespace Basalt.Data.Sessions;

/// <summary>
/// MySQL's table scripts: AUTO_INCREMENT, CHANGE COLUMN for a rename and MODIFY COLUMN for the rest. MySQL commits
/// each DDL statement by itself, so there is no transaction to wrap them in; a failure part-way leaves the
/// statements before it applied, which the script's confirmation says.
/// </summary>
public sealed class MySqlTableScripts : TableScriptGenerator
{
    private static readonly string[] Types =
    [
        "int", "bigint", "smallint", "tinyint", "tinyint(1)", "decimal(18,2)", "float", "double",
        "date", "time", "datetime", "timestamp", "year", "char(10)", "varchar(255)", "text", "longtext",
        "blob", "longblob", "json", "enum('a','b')",
    ];

    public MySqlTableScripts()
        : base(new MySqlDialect())
    {
    }

    public override DatabaseKind Kind => DatabaseKind.MySql;

    public override IReadOnlyList<string> CommonTypes => Types;

    public override TableScript Create(TableDesign design)
    {
        var lines = design.Columns.Select(c => "    " + ColumnDefinition(c)).ToList();
        var key = design.KeyColumns;

        if (key.Count > 0)
            lines.Add($"    PRIMARY KEY ({QuotedList(key.Select(c => c.Name), Q)})");

        foreach (var foreign in design.ForeignKeys)
            lines.Add("    " + ForeignKeyClause(foreign));

        return Plain([$"CREATE TABLE {Table(design)} (\n{string.Join(",\n", lines)}\n);"], null);
    }

    public override TableScript Alter(TableDesign original, TableDesign edited)
    {
        var diff = TableDiff.Between(original, edited);

        if (!diff.HasChanges) return TableScript.None;

        var table = Table(edited);
        var statements = new List<string>();
        var hadKey = original.Indexes.Any(i => i.IsPrimaryKey);
        var key = edited.KeyColumns;
        var keyDone = !diff.KeyChanged;

        // Columns renamed after the key is written are still under their stored names when the key names them.
        var renamedLater = diff.Changed
            .Where(c => c.Renamed && !(c.Before.IsIdentity && !c.After.IsIdentity))
            .Select(c => c.After.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // AUTO_INCREMENT is only valid on a key, so the order matters: columns that lose it are redefined before the
        // key goes, a new auto-increment column brings its own key, and columns that gain it follow the new key.
        foreach (var column in diff.Dropped)
            statements.Add($"ALTER TABLE {table} DROP COLUMN {Q(column.Name)};");

        foreach (var change in diff.Changed.Where(c => c.Before.IsIdentity && !c.After.IsIdentity))
            statements.Add(Modify(table, change));

        foreach (var column in diff.Added)
        {
            var ownKey = !keyDone && !hadKey && column.IsIdentity && key.Count == 1 && key[0] == column;

            statements.Add($"ALTER TABLE {table} ADD COLUMN {ColumnDefinition(column)}{(ownKey ? " PRIMARY KEY" : "")};");

            keyDone |= ownKey;
        }

        if (!keyDone)
        {
            // One statement, so the table is never left without the key its AUTO_INCREMENT column needs.
            var drop = hadKey ? "DROP PRIMARY KEY" : "";
            var add = key.Count > 0 ? $"ADD PRIMARY KEY ({QuotedList(key.Select(c => renamedLater.Contains(c.Name) ? c.OriginalName! : c.Name), Q)})" : "";

            statements.Add($"ALTER TABLE {table} {string.Join(", ", new[] { drop, add }.Where(t => t.Length > 0))};");
        }

        foreach (var change in diff.Changed.Where(c => !(c.Before.IsIdentity && !c.After.IsIdentity)))
            statements.Add(Modify(table, change));

        // MODIFY and CHANGE restate the column whole. What the catalogue read tells us about it is written back; if it
        // was not read, the script says that what it does not mention is reset.
        var unread = diff.Changed.Any(c => c.Before.Extras is null);
        var note = unread ? Localizer.Get(StringKeys.TableDesignerMySqlExtrasUnknown) : null;

        return Plain(statements, null, note is null ? null : new[] { note }, comment: note, retypes: diff.Changed.Any(c => c.TypeChanged));
    }

    /// <summary>MODIFY restates the whole column; CHANGE does the same under a new name.</summary>
    private string Modify(string table, ColumnChange change) => change.Renamed
        ? $"ALTER TABLE {table} CHANGE COLUMN {Q(change.Before.Name)} {ColumnDefinition(change.After, keepExtras: !change.TypeChanged)};"
        : $"ALTER TABLE {table} MODIFY COLUMN {ColumnDefinition(change.After, keepExtras: !change.TypeChanged)};";

    private string ColumnDefinition(DesignColumn column, bool keepExtras = false)
    {
        var text = $"{Q(column.Name)} {column.DataType.Trim()}";

        // Character set and collation belong to a text type: they are not carried over a change of type.
        var extras = keepExtras ? column.Extras : null;

        if (extras?.CharacterSet is { Length: > 0 } characterSet) text += " CHARACTER SET " + characterSet;
        if (extras?.Collation is { Length: > 0 } collation) text += " COLLATE " + collation;

        text += !column.IsNullable || column.IsPrimaryKey ? " NOT NULL" : " NULL";

        if (column.IsIdentity) text += " AUTO_INCREMENT";

        if (column.DefaultValue?.Trim() is { Length: > 0 } fallback)
            text += " DEFAULT " + fallback;

        if (extras?.OnUpdate is { Length: > 0 } onUpdate) text += " ON UPDATE " + onUpdate;
        if (extras?.Comment is { Length: > 0 } comment) text += " COMMENT " + TableText.Literal(comment);

        return text;
    }
}
