namespace Basalt.Data.Sessions;

/// <summary>
/// Writes the SQL that creates a table from a design, or alters a stored table into an edited design, in one
/// engine's spelling.
/// </summary>
/// <remarks>
/// ALTER is written from the difference between the loaded design and the edited one, never from the edited
/// design alone: a column the user did not touch produces no statement, and a renamed column is renamed, not
/// dropped and added, so its data stays.
/// </remarks>
public abstract class TableScriptGenerator
{
    protected TableScriptGenerator(ISqlDialect dialect) => Dialect = dialect;

    protected ISqlDialect Dialect { get; }

    /// <summary>The generator for an engine.</summary>
    public static TableScriptGenerator For(DatabaseKind kind) => kind switch
    {
        DatabaseKind.Sqlite => new SqliteTableScripts(),
        DatabaseKind.SqlServer => new SqlServerTableScripts(),
        DatabaseKind.PostgreSql => new PostgreSqlTableScripts(),
        DatabaseKind.MySql => new MySqlTableScripts(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public abstract DatabaseKind Kind { get; }

    /// <summary>The types offered in the designer's dropdown. The box stays editable: a type outside the list is still a type.</summary>
    public abstract IReadOnlyList<string> CommonTypes { get; }

    /// <summary>CREATE TABLE for a new design.</summary>
    public abstract TableScript Create(TableDesign design);

    /// <summary>
    /// The statements that turn the stored table into the edited one. Empty when nothing changed.
    /// </summary>
    public abstract TableScript Alter(TableDesign original, TableDesign edited);

    /// <summary>The script for a design: CREATE when there is no stored table, ALTER from the difference when there is.</summary>
    public TableScript Script(TableDesign? original, TableDesign edited) =>
        original is null ? Create(edited) : Alter(original, edited);

    protected string Q(string identifier) => Dialect.Quote(identifier);

    protected string Table(TableDesign design) => Dialect.QualifiedName(design.Name);

    /// <summary>A statement with its terminating semicolon.</summary>
    protected static string End(string statement) => statement.TrimEnd().EndsWith(';') ? statement : statement + ";";

    protected static string QuotedList(IEnumerable<string> names, Func<string, string> quote) =>
        string.Join(", ", names.Select(quote));

    /// <summary>The foreign key clause of a CREATE TABLE.</summary>
    protected string ForeignKeyClause(DesignForeignKey key)
    {
        var clause =
            $"FOREIGN KEY ({QuotedList(key.Columns, Q)}) " +
            $"REFERENCES {Dialect.QualifiedName(key.ReferencedTable)} ({QuotedList(key.ReferencedColumns, Q)})";

        if (key.OnDelete is { Length: > 0 } onDelete && !onDelete.Equals("NO ACTION", StringComparison.OrdinalIgnoreCase))
            clause += " ON DELETE " + onDelete;

        if (key.OnUpdate is { Length: > 0 } onUpdate && !onUpdate.Equals("NO ACTION", StringComparison.OrdinalIgnoreCase))
            clause += " ON UPDATE " + onUpdate;

        if (key.Deferrable is { Length: > 0 } deferrable && deferrable != DesignForeignKey.AmbiguousDeferrable)
            clause += " " + deferrable;

        return clause;
    }

    protected static TableScript Plain(
        IEnumerable<string> statements,
        ScriptTransaction? transaction,
        IEnumerable<string>? warnings = null,
        IEnumerable<string>? before = null,
        IEnumerable<string>? after = null,
        string? comment = null,
        bool retypes = false) =>
        new([.. statements], transaction, [.. before ?? []], [.. after ?? []], null, [.. warnings ?? []], Comment: comment, RetypesColumns: retypes);
}
