using Basalt.Core.Localization;

namespace Basalt.Data.Sessions;

/// <summary>SQL Server's table scripts: IDENTITY columns, sp_rename, and ALTER COLUMN, all inside a transaction.</summary>
/// <remarks>
/// SQL Server names the constraint behind a default, and the catalogue the IDE reads does not report the name,
/// so a default is dropped by looking its name up in the script itself (sys.default_constraints) and dropping
/// it by dynamic SQL. IDENTITY cannot be added to or taken from an existing column; the script says so rather than
/// pretend, and leaves that change to a new column or a new table.
/// </remarks>
public sealed class SqlServerTableScripts : TableScriptGenerator
{
    private static readonly string[] Types =
    [
        "int", "bigint", "smallint", "tinyint", "bit", "decimal(18,2)", "numeric(18,0)", "money", "float", "real",
        "date", "time", "datetime", "datetime2", "datetimeoffset", "char(10)", "varchar(50)", "varchar(max)",
        "nchar(10)", "nvarchar(50)", "nvarchar(max)", "text", "binary(16)", "varbinary(max)", "uniqueidentifier", "xml",
    ];

    private static ScriptTransaction Transaction { get; } = new("BEGIN TRANSACTION;", "COMMIT TRANSACTION;", "ROLLBACK TRANSACTION;");

    // With XACT_ABORT on, an error rolls the whole transaction back by itself, so a failure part-way can never be committed
    // by a COMMIT that follows it. It is a session setting, put back when the script is done.
    private static readonly string[] XactAbortOn = ["SET XACT_ABORT ON;"];
    private static readonly string[] XactAbortOff = ["SET XACT_ABORT OFF;"];

    public SqlServerTableScripts()
        : base(new SqlServerDialect())
    {
    }

    public override DatabaseKind Kind => DatabaseKind.SqlServer;

    public override IReadOnlyList<string> CommonTypes => Types;

    public override TableScript Create(TableDesign design)
    {
        var lines = design.Columns.Select(c => "    " + ColumnDefinition(c, withDefault: true)).ToList();
        var key = design.KeyColumns;

        if (key.Count > 0)
            lines.Add($"    CONSTRAINT {Q("PK_" + design.Name.Name)} PRIMARY KEY ({QuotedList(key.Select(c => c.Name), Q)})");

        foreach (var foreign in design.ForeignKeys)
            lines.Add("    " + ForeignKeyClause(foreign));

        return Plain([$"CREATE TABLE {Table(design)} (\n{string.Join(",\n", lines)}\n);"], Transaction, before: XactAbortOn, after: XactAbortOff);
    }

    public override TableScript Alter(TableDesign original, TableDesign edited)
    {
        var diff = TableDiff.Between(original, edited);

        if (!diff.HasChanges) return TableScript.None;

        var table = Table(edited);
        var tableName = TableText.NLiteral(Dialect.QualifiedName(edited.Name));
        var statements = new List<string>();
        var warnings = new List<string>();

        var oldKey = original.Indexes.FirstOrDefault(i => i.IsPrimaryKey);

        // The key goes first: a column that is part of it cannot be dropped or made NULL while it stands.
        if (diff.KeyChanged && oldKey is not null)
            statements.Add($"ALTER TABLE {table} DROP CONSTRAINT {Q(oldKey.Name)};");

        foreach (var column in diff.Dropped)
        {
            statements.Add(DropDefault(table, tableName, column.Name, statements.Count));
            statements.Add($"ALTER TABLE {table} DROP COLUMN {Q(column.Name)};");
        }

        foreach (var change in diff.Changed)
        {
            // Renamed before anything else touches the column, so the statements below use its new name.
            if (change.Renamed)
            {
                statements.Add(
                    $"EXEC sp_rename {TableText.NLiteral(Dialect.QualifiedName(edited.Name) + "." + Q(change.Before.Name))}, " +
                    $"{TableText.NLiteral(change.After.Name)}, N'COLUMN';");
            }

            if (change.IdentityChanged)
            {
                warnings.Add(Localizer.Get(StringKeys.TableDesignerIdentityUnsupported, change.After.Name));
            }

            // A column cannot be retyped while a default constraint depends on it: the default is dropped first and put back after.
            var storedDefault = change.Before.DefaultValue?.Trim() is { Length: > 0 };
            var redefineDefault = change.DefaultChanged || (change.TypeChanged && storedDefault);

            if (redefineDefault)
                statements.Add(DropDefault(table, tableName, change.After.Name, statements.Count));

            if (change.TypeChanged || change.NullabilityChanged)
            {
                // ALTER COLUMN restates the type and nullability; the default is a separate constraint.
                statements.Add(
                    $"ALTER TABLE {table} ALTER COLUMN {Q(change.After.Name)} {change.After.DataType.Trim()} {(change.After.IsNullable ? "NULL" : "NOT NULL")};");
            }

            if (redefineDefault && change.After.DefaultValue?.Trim() is { Length: > 0 } fallback)
                statements.Add($"ALTER TABLE {table} ADD CONSTRAINT {Q($"DF_{edited.Name.Name}_{change.After.Name}")} DEFAULT {fallback} FOR {Q(change.After.Name)};");
        }

        foreach (var column in diff.Added)
            statements.Add($"ALTER TABLE {table} ADD {ColumnDefinition(column, withDefault: true)};");

        var key = edited.KeyColumns;

        if (diff.KeyChanged && key.Count > 0)
        {
            statements.Add(
                $"ALTER TABLE {table} ADD CONSTRAINT {Q("PK_" + edited.Name.Name)} PRIMARY KEY ({QuotedList(key.Select(c => c.Name), Q)});");
        }

        return Plain(statements, Transaction, warnings, XactAbortOn, XactAbortOff, retypes: diff.Changed.Any(c => c.TypeChanged));
    }

    /// <summary>Drops the default constraint of a column, whatever it is named, if it has one.</summary>
    /// <remarks>The variable is numbered: one script holds several of these, in one batch when it is run from a query tab.</remarks>
    private string DropDefault(string table, string tableName, string column, int number) =>
        $"DECLARE @df{number} nvarchar(256) = (SELECT d.name FROM sys.default_constraints d " +
        "JOIN sys.columns c ON c.object_id = d.parent_object_id AND c.column_id = d.parent_column_id " +
        $"WHERE d.parent_object_id = OBJECT_ID({tableName}) AND c.name = {TableText.NLiteral(column)}); " +
        $"IF @df{number} IS NOT NULL EXEC(N'ALTER TABLE ' + {TableText.NLiteral(table)} + N' DROP CONSTRAINT ' + QUOTENAME(@df{number}));";

    private string ColumnDefinition(DesignColumn column, bool withDefault)
    {
        var text = $"{Q(column.Name)} {column.DataType.Trim()}";

        if (column.IsIdentity) text += " IDENTITY(1,1)";

        text += column.IsNullable && !column.IsPrimaryKey ? " NULL" : " NOT NULL";

        if (withDefault && column.DefaultValue?.Trim() is { Length: > 0 } fallback)
            text += " DEFAULT " + fallback;

        return text;
    }
}
