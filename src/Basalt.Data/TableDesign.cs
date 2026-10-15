using System.Text;
using System.Text.RegularExpressions;
using Basalt.Core.Localization;

namespace Basalt.Data.Sessions;

/// <summary>
/// What MySQL keeps on a column besides its type, which MODIFY and CHANGE would otherwise reset: its character set,
/// collation, comment and ON UPDATE clause. Null on a column whose extras were not read.
/// </summary>
public sealed record ColumnExtras(string? CharacterSet, string? Collation, string? Comment, string? OnUpdate);

/// <summary>A column of a table being designed.</summary>
/// <param name="OriginalName">
/// The column's name in the table as it is stored, or null for a column the user added. It is what ties an
/// edited column to a stored one, so a rename is a rename and not a drop followed by an add: the data is kept.
/// </param>
public sealed record DesignColumn(
    string Name,
    string DataType,
    bool IsNullable,
    bool IsPrimaryKey,
    string? DefaultValue,
    bool IsIdentity,
    string? OriginalName = null,
    ColumnExtras? Extras = null)
{
    /// <summary>A column as the catalogue describes it, tied to itself.</summary>
    public static DesignColumn From(ColumnInfo column) => new(
        column.Name,
        column.DataType,
        column.IsNullable,
        column.IsPrimaryKey,
        // A sequence-backed default (PostgreSQL serial) is the identity, not a default the user wrote.
        column.IsIdentity ? null : column.DefaultValue,
        column.IsIdentity,
        column.Name);
}

/// <summary>A foreign key of a table being designed, with the actions the catalogue model does not carry.</summary>
/// <param name="Deferrable">SQLite's <c>DEFERRABLE INITIALLY ...</c> clause as the table was created with it, or null.</param>
public sealed record DesignForeignKey(
    string Name,
    IReadOnlyList<string> Columns,
    DbObjectName ReferencedTable,
    IReadOnlyList<string> ReferencedColumns,
    string? OnDelete = null,
    string? OnUpdate = null,
    string? Deferrable = null)
{
    /// <summary>
    /// Stands in for <see cref="Deferrable"/> when two keys to one table cannot be told apart in the CREATE
    /// statement and one of them is deferrable: a rebuild would have to guess which.
    /// </summary>
    public const string AmbiguousDeferrable = "?";
}

/// <summary>
/// A table as the designer holds it: the columns the user edits, and the parts of the table that an edit of the
/// columns must carry along (indexes, foreign keys, triggers).
/// </summary>
/// <param name="Triggers">The CREATE statements of the table's triggers, SQLite only: a rebuild drops them with the table.</param>
/// <param name="SourceSql">The statement a SQLite table was created with, so a rebuild can warn about what it cannot carry over.</param>
/// <param name="IndexSql">The CREATE INDEX statement of each SQLite index by name, so a rebuild recreates partial, DESC and COLLATE indexes as they were.</param>
public sealed record TableDesign(
    DbObjectName Name,
    IReadOnlyList<DesignColumn> Columns,
    IReadOnlyList<IndexInfo> Indexes,
    IReadOnlyList<DesignForeignKey> ForeignKeys,
    IReadOnlyList<string> Triggers,
    string? SourceSql = null,
    IReadOnlyDictionary<string, string>? IndexSql = null)
{
    /// <summary>
    /// Whether another read of the same table has the same columns, indexes, foreign keys and triggers. A designer
    /// compares what it loaded with what the database has before it runs a script written from the first.
    /// </summary>
    public bool SameStructureAs(TableDesign other) =>
        Columns.SequenceEqual(other.Columns)
        && Triggers.SequenceEqual(other.Triggers)
        && string.Equals(SourceSql, other.SourceSql, StringComparison.Ordinal)
        && (IndexSql ?? new Dictionary<string, string>()).OrderBy(i => i.Key, StringComparer.Ordinal)
            .SequenceEqual((other.IndexSql ?? new Dictionary<string, string>()).OrderBy(i => i.Key, StringComparer.Ordinal))
        && Indexes.Select(IndexKey).SequenceEqual(other.Indexes.Select(IndexKey))
        && ForeignKeys.Select(ForeignKeyKey).SequenceEqual(other.ForeignKeys.Select(ForeignKeyKey));

    private static string IndexKey(IndexInfo index) =>
        $"{index.Name}|{string.Join(",", index.Columns)}|{index.IsUnique}|{index.IsPrimaryKey}";

    private static string ForeignKeyKey(DesignForeignKey key) =>
        $"{string.Join(",", key.Columns)}>{key.ReferencedTable}({string.Join(",", key.ReferencedColumns)})|{key.OnDelete}|{key.OnUpdate}|{key.Deferrable}";

    /// <summary>A table with no columns yet, for New Table.</summary>
    public static TableDesign Empty(DbObjectName name) => new(name, [], [], [], []);

    /// <summary>The design of an existing table, from its catalogue details.</summary>
    public static TableDesign FromDetails(TableDetails details) => new(
        details.Name,
        [.. details.Columns.Select(DesignColumn.From)],
        details.Indexes,
        [.. details.ForeignKeys.Select(k => new DesignForeignKey(k.Name, k.Columns, k.ReferencedTable, k.ReferencedColumns))],
        []);

    /// <summary>The columns that make up the primary key, in column order.</summary>
    public IReadOnlyList<DesignColumn> KeyColumns => [.. Columns.Where(c => c.IsPrimaryKey)];

    /// <summary>
    /// What is wrong with the design, as sentences for the user; empty when the script can be generated.
    /// Checked before a script is written because a script made from a nameless or duplicated column would
    /// only fail at the database, with a message about syntax rather than about the column.
    /// </summary>
    public IReadOnlyList<string> Validate(DatabaseKind kind)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(Name.Name))
            issues.Add(Localizer.Get(StringKeys.TableDesignerTableNameRequired));

        if (Columns.Count == 0)
            issues.Add(Localizer.Get(StringKeys.TableDesignerNoColumns));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < Columns.Count; i++)
        {
            var column = Columns[i];
            var position = i + 1;

            if (string.IsNullOrWhiteSpace(column.Name))
                issues.Add(Localizer.Get(StringKeys.TableDesignerColumnNameRequired, position));
            else if (!seen.Add(column.Name))
                issues.Add(Localizer.Get(StringKeys.TableDesignerDuplicateColumn, column.Name));

            if (string.IsNullOrWhiteSpace(column.DataType))
                issues.Add(Localizer.Get(StringKeys.TableDesignerTypeRequired, DisplayName(column, position)));
        }

        var identities = Columns.Where(c => c.IsIdentity).ToList();

        if (identities.Count > 1)
            issues.Add(Localizer.Get(StringKeys.TableDesignerOneIdentity));

        // SQLite's autoincrement is the rowid alias, which only a lone INTEGER PRIMARY KEY can be.
        if (kind == DatabaseKind.Sqlite && identities.Count == 1 && !(identities[0].IsPrimaryKey && KeyColumns.Count == 1))
            issues.Add(Localizer.Get(StringKeys.TableDesignerIdentityNeedsKey, identities[0].Name));

        // MySQL's AUTO_INCREMENT column must be a key.
        if (kind == DatabaseKind.MySql && identities.Count == 1 && !identities[0].IsPrimaryKey)
            issues.Add(Localizer.Get(StringKeys.TableDesignerIdentityNeedsKey, identities[0].Name));

        return issues;
    }

    private static string DisplayName(DesignColumn column, int position) =>
        string.IsNullOrWhiteSpace(column.Name) ? "#" + position : column.Name;
}

/// <summary>How a script is wrapped: the statements that open, finish and abandon a transaction.</summary>
public sealed record ScriptTransaction(string Begin, string Commit, string Rollback);

/// <summary>
/// The SQL that carries a table design to the database.
/// </summary>
/// <param name="Statements">The statements, each ending in a semicolon, run in order.</param>
/// <param name="Transaction">How to wrap them, or null where the engine commits each DDL statement itself (MySQL).</param>
/// <param name="Before">Statements that must run outside the transaction, before it (SQLite's foreign_keys off).</param>
/// <param name="After">Statements that run outside the transaction, after it, whether it succeeded or not.</param>
/// <param name="ForeignKeyCheck">
/// A statement that lists rows breaking a foreign key; run before the commit, and when it returns any row the
/// script is rolled back. SQLite's rebuild turns enforcement off, so this is where a violation is found.
/// </param>
/// <param name="Warnings">What the script does not carry over or could not express, to show before the user confirms.</param>
/// <param name="Comment">Lines written as SQL comments at the head of the script text.</param>
/// <param name="Refusal">Why the generator would not write a script, or null.</param>
/// <param name="SettingQuery">A statement that reads a setting the script changes (SQLite's foreign_keys), run before anything else.</param>
/// <param name="RetypesColumns">Whether an existing column gets another type, which can fail on or change the values it holds.</param>
/// <param name="SettingRestore">A format for the statement that puts that setting back to what was read, in place of <paramref name="After"/>.</param>
public sealed record TableScript(
    IReadOnlyList<string> Statements,
    ScriptTransaction? Transaction,
    IReadOnlyList<string> Before,
    IReadOnlyList<string> After,
    string? ForeignKeyCheck,
    IReadOnlyList<string> Warnings,
    string? Comment = null,
    string? Refusal = null,
    string? SettingQuery = null,
    string? SettingRestore = null,
    bool RetypesColumns = false)
{
    public static TableScript None { get; } = new([], null, [], [], null, []);

    /// <summary>A change the generator will not make, with the reason: nothing is written and nothing may be run.</summary>
    public static TableScript Refused(string reason) => new([], null, [], [], null, [], Refusal: reason);

    /// <summary>Whether the edit changes nothing in the database.</summary>
    public bool IsEmpty => Statements.Count == 0;

    /// <summary>
    /// Whether the script drops something or rebuilds the table, which the confirmation then makes hard to run by
    /// reflex: Cancel is its default button, not Run.
    /// </summary>
    public bool IsDestructive => SettingQuery is not null || RetypesColumns || Statements.Any(s => DropPattern.IsMatch(s));

    private static readonly Regex DropPattern = new(@"\bDROP\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The script as one text a person can read and run in a query tab: the comment, the wrapper lines, the
    /// statements, and the foreign key check, in the order <see cref="TableDesignRunner"/> runs them.
    /// </summary>
    public string Text
    {
        get
        {
            if (IsEmpty) return "";

            var text = new StringBuilder();

            if (Comment is { Length: > 0 })
            {
                foreach (var line in Comment.ReplaceLineEndings("\n").Split('\n')) text.AppendLine("-- " + line);
            }

            foreach (var line in Before) text.AppendLine(line);
            if (Transaction is not null) text.AppendLine(Transaction.Begin);
            foreach (var statement in Statements) text.AppendLine(statement);
            if (ForeignKeyCheck is not null) text.AppendLine(ForeignKeyCheck);
            if (Transaction is not null) text.AppendLine(Transaction.Commit);
            foreach (var line in After) text.AppendLine(line);

            return text.ToString().TrimEnd();
        }
    }
}

/// <summary>
/// The difference between a stored table and its edited design, column by column.
/// </summary>
internal sealed class TableDiff
{
    private TableDiff()
    {
    }

    /// <summary>Stored columns the edit no longer has.</summary>
    public required IReadOnlyList<DesignColumn> Dropped { get; init; }

    /// <summary>Stored columns that changed in some way, with their edited form.</summary>
    public required IReadOnlyList<ColumnChange> Changed { get; init; }

    /// <summary>Columns the user added.</summary>
    public required IReadOnlyList<DesignColumn> Added { get; init; }

    /// <summary>Whether the set of primary key columns differs, or one of them was renamed.</summary>
    public required bool KeyChanged { get; init; }

    /// <summary>Whether the kept columns are in another order, or a new column is not last.</summary>
    public required bool OrderChanged { get; init; }

    public bool HasChanges => Dropped.Count > 0 || Changed.Count > 0 || Added.Count > 0 || KeyChanged || OrderChanged;

    /// <summary>Whether the edit adds columns and does nothing else.</summary>
    public bool OnlyAdditions => Added.Count > 0 && Dropped.Count == 0 && Changed.Count == 0 && !KeyChanged && !OrderChanged;

    public static TableDiff Between(TableDesign original, TableDesign edited)
    {
        var stored = original.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = new List<ColumnChange>();
        var added = new List<DesignColumn>();

        foreach (var column in edited.Columns)
        {
            if (column.OriginalName is { } was && stored.TryGetValue(was, out var before))
            {
                kept.Add(before.Name);

                var change = new ColumnChange(before, column);

                if (change.Any) changed.Add(change);
            }
            else
            {
                added.Add(column);
            }
        }

        var dropped = original.Columns.Where(c => !kept.Contains(c.Name)).ToList();

        var keptOrder = edited.Columns
            .Where(c => c.OriginalName is not null && stored.ContainsKey(c.OriginalName))
            .Select(c => stored[c.OriginalName!].Name)
            .ToList();

        var storedOrder = original.Columns.Where(c => kept.Contains(c.Name)).Select(c => c.Name).ToList();

        // A column added in the middle can only be placed by a rebuild: ADD COLUMN appends.
        var firstAdded = edited.Columns.ToList().FindIndex(c => added.Contains(c));
        var addedNotLast = firstAdded >= 0 && edited.Columns.Skip(firstAdded).Any(c => !added.Contains(c));

        var oldKey = original.KeyColumns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newKey = edited.KeyColumns.Select(c => c.OriginalName ?? "\0new:" + c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new TableDiff
        {
            Dropped = dropped,
            Changed = changed,
            Added = added,
            KeyChanged = !oldKey.SetEquals(newKey),
            OrderChanged = !keptOrder.SequenceEqual(storedOrder, StringComparer.OrdinalIgnoreCase) || addedNotLast,
        };
    }
}

/// <summary>A stored column and the form the user edited it into.</summary>
internal sealed record ColumnChange(DesignColumn Before, DesignColumn After)
{
    public bool Renamed => !string.Equals(Before.Name, After.Name, StringComparison.Ordinal);

    public bool TypeChanged => TableText.NormalizeType(Before.DataType) != TableText.NormalizeType(After.DataType);

    public bool NullabilityChanged => Before.IsNullable != After.IsNullable;

    public bool DefaultChanged => TableText.NormalizeDefault(Before.DefaultValue) != TableText.NormalizeDefault(After.DefaultValue);

    public bool IdentityChanged => Before.IsIdentity != After.IsIdentity;

    public bool KeyChanged => Before.IsPrimaryKey != After.IsPrimaryKey;

    public bool Any => Renamed || TypeChanged || NullabilityChanged || DefaultChanged || IdentityChanged || KeyChanged;
}

/// <summary>Small text rules the generators share.</summary>
internal static partial class TableText
{
    /// <summary>A type compared for equality: case and spaces do not make a different type.</summary>
    public static string NormalizeType(string type) =>
        string.Concat(type.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();

    /// <summary>
    /// A default compared for equality. SQL Server reports <c>((0))</c> for <c>DEFAULT 0</c>, so the
    /// parentheses around the whole expression are not part of its value.
    /// </summary>
    public static string NormalizeDefault(string? value)
    {
        var text = (value ?? "").Trim();

        while (text.Length >= 2 && text[0] == '(' && text[^1] == ')' && Balanced(text[1..^1]))
            text = text[1..^1].Trim();

        return text;
    }

    private static bool Balanced(string inner)
    {
        var depth = 0;
        var quoted = false;

        foreach (var c in inner)
        {
            if (c == '\'') quoted = !quoted;
            if (quoted) continue;

            if (c == '(') depth++;
            else if (c == ')' && --depth < 0) return false;
        }

        return depth == 0;
    }

    /// <summary>A SQL string literal, quotes doubled.</summary>
    public static string Literal(string text) => "'" + text.Replace("'", "''") + "'";

    /// <summary>A literal in a SQL Server Unicode string: N'...'.</summary>
    public static string NLiteral(string text) => "N" + Literal(text);

    [GeneratedRegex(@"^[+-]?\d+(\.\d+)?([eE][+-]?\d+)?$|^'([^']|'')*'$|^(NULL|TRUE|FALSE|CURRENT_TIME|CURRENT_DATE|CURRENT_TIMESTAMP)$|^x'[0-9a-fA-F]*'$", RegexOptions.IgnoreCase)]
    private static partial Regex SimpleLiteralPattern();

    /// <summary>Whether a default is a plain literal or keyword, which SQLite accepts without parentheses.</summary>
    public static bool IsSimpleLiteral(string value) => SimpleLiteralPattern().IsMatch(value.Trim());
}
