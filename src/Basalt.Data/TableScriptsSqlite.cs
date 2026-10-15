using System.Text.RegularExpressions;
using Basalt.Core.Localization;

namespace Basalt.Data.Sessions;

/// <summary>
/// SQLite's table scripts. SQLite can add a column and little else, so every other change uses the table
/// rebuild SQLite documents (https://www.sqlite.org/lang_altertable.html#otheralter): foreign keys off, then in one
/// transaction create the new table, copy the rows, drop the old table, rename the new one, recreate the indexes
/// and triggers, check the foreign keys, and commit.
/// </summary>
/// <remarks>
/// A rebuild carries over the columns, the primary key, UNIQUE constraints, foreign keys with their actions,
/// plain indexes and triggers. CHECK constraints, collations, generated columns and WITHOUT ROWID are not in what
/// the catalogue reports, so the script warns instead of dropping them silently.
/// </remarks>
public sealed partial class SqliteTableScripts : TableScriptGenerator
{
    private static readonly string[] Types =
        ["INTEGER", "TEXT", "REAL", "NUMERIC", "BLOB", "BOOLEAN", "DATE", "DATETIME", "VARCHAR(255)", "DECIMAL(10,2)"];

    public SqliteTableScripts()
        : base(new SqliteDialect())
    {
    }

    public override DatabaseKind Kind => DatabaseKind.Sqlite;

    public override IReadOnlyList<string> CommonTypes => Types;

    private static ScriptTransaction Transaction { get; } = new("BEGIN TRANSACTION;", "COMMIT;", "ROLLBACK;");

    public override TableScript Create(TableDesign design) =>
        Plain([CreateTable(design, design.Name.Name, design.Columns, design.ForeignKeys, [])], null);

    public override TableScript Alter(TableDesign original, TableDesign edited)
    {
        var diff = TableDiff.Between(original, edited);

        if (!diff.HasChanges) return TableScript.None;

        if (diff.OnlyAdditions && diff.Added.All(CanAddColumn))
        {
            var table = Table(edited);

            return Plain(diff.Added.Select(c => $"ALTER TABLE {table} ADD COLUMN {ColumnDefinition(c, inlineKey: false)};"), Transaction);
        }

        return Rebuild(original, edited);
    }

    /// <summary>
    /// ADD COLUMN cannot add a key, a NOT NULL column without a default (the existing rows would break it), or a
    /// default that is not a constant (CURRENT_TIMESTAMP, an expression).
    /// </summary>
    private static bool CanAddColumn(DesignColumn column)
    {
        if (column.IsPrimaryKey || column.IsIdentity) return false;

        var fallback = column.DefaultValue?.Trim();

        if (string.IsNullOrEmpty(fallback)) return column.IsNullable;

        return TableText.IsSimpleLiteral(fallback)
            && !fallback.StartsWith("CURRENT_", StringComparison.OrdinalIgnoreCase);
    }

    private TableScript Rebuild(TableDesign original, TableDesign edited)
    {
        var warnings = new List<string>();
        var table = Table(edited);
        var temporary = "new_" + edited.Name.Name;
        var temporaryQuoted = Q(temporary);

        // Where each stored column went: a renamed column keeps its data under its new name.
        var renamed = edited.Columns
            .Where(c => c.OriginalName is not null)
            .ToDictionary(c => c.OriginalName!, c => c.Name, StringComparer.OrdinalIgnoreCase);

        // The stored names of the columns that no longer exist under that name: what indexes and triggers may still mention.
        var affected = original.Columns
            .Where(c => !renamed.TryGetValue(c.Name, out var now) || !string.Equals(now, c.Name, StringComparison.Ordinal))
            .Select(c => c.Name)
            .ToList();

        var uniques = new List<IReadOnlyList<string>>();
        var indexes = new List<string>();

        foreach (var index in original.Indexes.Where(i => !i.IsPrimaryKey))
        {
            if (index.Name.StartsWith("sqlite_autoindex", StringComparison.OrdinalIgnoreCase))
            {
                // A UNIQUE constraint declared on the table: it comes back as part of the CREATE TABLE.
                var mappedUnique = MapColumns(index.Columns, renamed);

                if (mappedUnique is null)
                    warnings.Add(Localizer.Get(StringKeys.TableDesignerIndexDropped, index.Name));
                else if (index.IsUnique)
                    uniques.Add(mappedUnique);

                continue;
            }

            // The statement the index was created with keeps its WHERE, its DESC and its COLLATE.
            if (original.IndexSql is { } stored && stored.TryGetValue(index.Name, out var indexSql))
            {
                switch (SqliteSchemaSql.AdjustIndex(indexSql, renamed, affected, Q, out var adjusted))
                {
                    case SqliteSchemaSql.Outcome.Unsafe:
                        return TableScript.Refused(Localizer.Get(StringKeys.TableDesignerIndexUnsafe, index.Name));

                    case SqliteSchemaSql.Outcome.Dropped:
                        warnings.Add(Localizer.Get(StringKeys.TableDesignerIndexDropped, index.Name));
                        break;

                    default:
                        indexes.Add(adjusted);
                        break;
                }

                continue;
            }

            var mapped = MapColumns(index.Columns, renamed);

            if (mapped is null)
            {
                warnings.Add(Localizer.Get(StringKeys.TableDesignerIndexDropped, index.Name));
                continue;
            }

            indexes.Add(
                $"CREATE {(index.IsUnique ? "UNIQUE " : "")}INDEX {Q(index.Name)} ON {table} ({QuotedList(mapped, Q)});");
        }

        foreach (var trigger in original.Triggers)
        {
            // A trigger body cannot be rewritten for a renamed column: it is kept as it is, or the change is refused.
            if (SqliteSchemaSql.Mentions(trigger, affected))
                return TableScript.Refused(Localizer.Get(StringKeys.TableDesignerTriggerUnsafe, SqliteSchemaSql.TriggerName(trigger)));
        }

        var foreignKeys = new List<DesignForeignKey>();

        foreach (var key in edited.ForeignKeys)
        {
            if (key.Deferrable == DesignForeignKey.AmbiguousDeferrable)
                return TableScript.Refused(Localizer.Get(StringKeys.TableDesignerDeferrableAmbiguous, edited.Name.Name, key.ReferencedTable.ToString()));

            // The key may name columns by their stored names; the edited columns carry the new ones.
            var mapped = MapColumns(key.Columns, renamed);

            // A key to this very table also points at columns that may have been renamed.
            var target = string.Equals(key.ReferencedTable.Name, edited.Name.Name, StringComparison.OrdinalIgnoreCase)
                ? MapColumns(key.ReferencedColumns, renamed)
                : key.ReferencedColumns;

            if (mapped is null || target is null)
            {
                warnings.Add(Localizer.Get(StringKeys.TableDesignerForeignKeyDropped, key.Name));
                continue;
            }

            foreignKeys.Add(key with { Columns = mapped, ReferencedColumns = target });
        }

        if (original.SourceSql is { } source && LosesDefinition(source))
            warnings.Add(Localizer.Get(StringKeys.TableDesignerRebuildLoses, original.Name.Name));

        var copied = edited.Columns.Where(c => c.OriginalName is not null && renamed.ContainsKey(c.OriginalName)).ToList();

        // AUTOINCREMENT is what keeps ids from being reused after a delete; the column loses it only if the user took the identity away.
        var autoincrement = original.SourceSql is { } sql
            && AutoincrementPattern().IsMatch(sql)
            && edited.Columns.Any(c => c.IsIdentity && c.OriginalName is not null && original.Columns.Any(o => o.IsIdentity && string.Equals(o.Name, c.OriginalName, StringComparison.OrdinalIgnoreCase)));

        var statements = new List<string>
        {
            CreateTable(edited, temporary, edited.Columns, foreignKeys, uniques, autoincrement),
        };

        if (copied.Count > 0)
        {
            statements.Add(
                $"INSERT INTO {temporaryQuoted} ({QuotedList(copied.Select(c => c.Name), Q)}) " +
                $"SELECT {QuotedList(copied.Select(c => c.OriginalName!), Q)} FROM {table};");
        }

        if (autoincrement)
        {
            // The counter lives in sqlite_sequence under the table's name. Copying the rows has set the new table's to
            // the highest id copied; the old table's may be higher (rows deleted since), and the next id must continue from it.
            var oldName = TableText.Literal(original.Name.Name);

            statements.Add($"DELETE FROM sqlite_sequence WHERE name = {TableText.Literal(temporary)};");
            statements.Add(
                $"INSERT INTO sqlite_sequence (name, seq) SELECT {TableText.Literal(temporary)}, seq FROM sqlite_sequence WHERE name = {oldName};");
        }

        statements.Add($"DROP TABLE {table};");
        statements.Add($"ALTER TABLE {temporaryQuoted} RENAME TO {Q(edited.Name.Name)};");
        statements.AddRange(indexes);
        statements.AddRange(original.Triggers.Select(End));

        return new TableScript(
            statements,
            Transaction,
            ["PRAGMA foreign_keys = OFF;"],
            ["PRAGMA foreign_keys = ON;"],
            "PRAGMA foreign_key_check;",
            warnings,
            Comment: Localizer.Get(StringKeys.TableDesignerRebuildComment),
            SettingQuery: "PRAGMA foreign_keys;",
            SettingRestore: "PRAGMA foreign_keys = {0};");
    }

    /// <summary>The columns under their edited names, or null when one of them no longer exists.</summary>
    private static IReadOnlyList<string>? MapColumns(IEnumerable<string> columns, Dictionary<string, string> renamed)
    {
        var mapped = new List<string>();

        foreach (var column in columns)
        {
            if (!renamed.TryGetValue(column, out var now)) return null;

            mapped.Add(now);
        }

        return mapped;
    }

    private string CreateTable(
        TableDesign design,
        string name,
        IReadOnlyList<DesignColumn> columns,
        IReadOnlyList<DesignForeignKey> foreignKeys,
        IReadOnlyList<IReadOnlyList<string>> uniques,
        bool autoincrement = false)
    {
        var lines = new List<string>();
        var identity = columns.Any(c => c.IsIdentity);

        foreach (var column in columns)
            lines.Add("    " + ColumnDefinition(column, inlineKey: true, autoincrement));

        var key = columns.Where(c => c.IsPrimaryKey).ToList();

        // An INTEGER PRIMARY KEY is declared on the column itself, which is what makes it the rowid alias.
        if (key.Count > 0 && !identity)
            lines.Add($"    PRIMARY KEY ({QuotedList(key.Select(c => c.Name), Q)})");

        foreach (var unique in uniques)
            lines.Add($"    UNIQUE ({QuotedList(unique, Q)})");

        foreach (var foreign in foreignKeys)
            lines.Add("    " + ForeignKeyClause(foreign));

        return $"CREATE TABLE {Q(name)} (\n{string.Join(",\n", lines)}\n);";
    }

    private string ColumnDefinition(DesignColumn column, bool inlineKey, bool autoincrement = false)
    {
        // The rowid alias is spelled INTEGER PRIMARY KEY exactly; INT PRIMARY KEY is an ordinary column.
        if (column.IsIdentity && inlineKey)
            return $"{Q(column.Name)} INTEGER PRIMARY KEY" + (autoincrement ? " AUTOINCREMENT" : "");

        var text = $"{Q(column.Name)} {column.DataType.Trim()}";

        if (!column.IsNullable) text += " NOT NULL";

        if (column.DefaultValue?.Trim() is { Length: > 0 } fallback)
            text += " DEFAULT " + (TableText.IsSimpleLiteral(fallback) || IsParenthesised(fallback) ? fallback : "(" + fallback + ")");

        return text;
    }

    private static bool IsParenthesised(string value) =>
        value.StartsWith('(') && value.EndsWith(')') && TableText.NormalizeDefault(value) != value.Trim();

    [GeneratedRegex(@"\bAUTOINCREMENT\b", RegexOptions.IgnoreCase)]
    private static partial Regex AutoincrementPattern();

    // What the catalogue does not report and the rebuilt CREATE TABLE does not write. Deferrable keys, AUTOINCREMENT and
    // the indexes' own WHERE, DESC and COLLATE are carried over and so are not here.
    [GeneratedRegex(@"\bCHECK\s*\(|\bCOLLATE\b|\bGENERATED\b|\bWITHOUT\s+ROWID\b|\bSTRICT\b|\bON\s+CONFLICT\b", RegexOptions.IgnoreCase)]
    private static partial Regex LostByRebuild();

    private static bool LosesDefinition(string sql) => LostByRebuild().IsMatch(sql);
}
