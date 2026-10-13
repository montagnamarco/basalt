namespace Basalt.Data.Sessions;

/// <summary>The database engines the IDE talks to.</summary>
public enum DatabaseKind
{
    Sqlite,
    SqlServer,
    PostgreSql,
    MySql,
}

/// <summary>Whether a schema object holds rows of its own or is a stored query.</summary>
public enum DbObjectKind
{
    Table,
    View,
}

/// <summary>
/// A table or a view, named as the engine names it.
/// </summary>
/// <remarks>
/// The schema is null where the engine has none: SQLite, and MySQL, where a
/// "schema" is a database and the session is already inside one.
/// </remarks>
public sealed record DbObjectName(string? Schema, string Name)
{
    public override string ToString() => Schema is null ? Name : $"{Schema}.{Name}";
}

/// <summary>A table or view in the catalogue, without its details.</summary>
public sealed record DbObjectInfo(DbObjectName Name, DbObjectKind Kind);

/// <summary>A column, as the catalogue describes it.</summary>
/// <param name="DataType">The engine's own type name, as a user would write it: "nvarchar(50)", "integer".</param>
/// <param name="IsIdentity">Filled in by the engine on insert: identity, serial, AUTO_INCREMENT, or SQLite's INTEGER PRIMARY KEY.</param>
/// <param name="DefaultValue">The default as the catalogue spells it, or null.</param>
/// <param name="Ordinal">The column's position in the table, one-based.</param>
public sealed record ColumnInfo(
    string Name,
    string DataType,
    bool IsNullable,
    bool IsPrimaryKey,
    bool IsIdentity,
    string? DefaultValue,
    int Ordinal);

/// <summary>An index, with its columns in order.</summary>
public sealed record IndexInfo(string Name, IReadOnlyList<string> Columns, bool IsUnique, bool IsPrimaryKey);

/// <summary>A foreign key: the columns here, and the ones they point at.</summary>
public sealed record ForeignKeyInfo(
    string Name,
    IReadOnlyList<string> Columns,
    DbObjectName ReferencedTable,
    IReadOnlyList<string> ReferencedColumns);

/// <summary>Everything the explorer and the data view need to know about one table or view.</summary>
public sealed record TableDetails(
    DbObjectName Name,
    DbObjectKind Kind,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<IndexInfo> Indexes,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys)
{
    /// <summary>
    /// The columns that identify a row, in order. Empty for a view and for a
    /// table without a primary key: neither can be edited from the data view,
    /// because an update or a delete could not say which row it means.
    /// </summary>
    public IReadOnlyList<ColumnInfo> KeyColumns => [.. Columns.Where(c => c.IsPrimaryKey)];

    /// <summary>Whether the data view may change rows of this object.</summary>
    public bool IsEditable => Kind == DbObjectKind.Table && KeyColumns.Count > 0;
}

/// <summary>One result set: its columns and its rows.</summary>
/// <remarks>
/// Values are kept as the provider read them (null for SQL NULL, never
/// DBNull), so the grid can tell a NULL from an empty string and a number from
/// its text. <see cref="CellText"/> is the one place they become text.
/// </remarks>
public sealed record ResultSet(
    IReadOnlyList<ResultColumnInfo> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    bool Truncated);

/// <summary>A result column: its name and the engine's type name.</summary>
public sealed record ResultColumnInfo(string Name, string DataType);

/// <summary>Something the engine said while running a batch: a count, a PRINT, a notice, an error.</summary>
/// <param name="Line">The batch line the message is about, when the engine says, one-based.</param>
public sealed record QueryMessage(string Text, bool IsError, int? Line);

/// <summary>What running a batch produced.</summary>
public sealed record QueryBatchResult(
    IReadOnlyList<ResultSet> ResultSets,
    IReadOnlyList<QueryMessage> Messages,
    TimeSpan Took)
{
    public bool Failed => Messages.Any(m => m.IsError);
}

/// <summary>A sort the data view asks for: one column, one direction.</summary>
public sealed record SortSpec(string Column, bool Descending);

/// <summary>
/// One page of a table's rows.
/// </summary>
/// <param name="Filter">A WHERE condition as the user typed it, without the keyword, or null.</param>
public sealed record DataPageRequest(
    DbObjectName Table,
    int Offset,
    int Limit,
    IReadOnlyList<SortSpec> Sort,
    string? Filter);

/// <summary>One page of rows, with the table's details and, when cheap to know, the total.</summary>
public sealed record DataPage(TableDetails Table, IReadOnlyList<IReadOnlyList<object?>> Rows, long? TotalRows);

/// <summary>
/// A change to a table's rows, as the data view collected it before Save.
/// </summary>
/// <remarks>
/// Values are the text the user typed (null for NULL): the session converts
/// each to the column's type, because PostgreSQL refuses a text parameter for
/// an integer column, and a guess made by the grid would differ per engine.
/// Keys are the row's original key values, as read, so an edit to a key
/// column still finds its row.
/// </remarks>
public abstract record RowEdit;

public sealed record RowInsert(IReadOnlyDictionary<string, string?> Values) : RowEdit;

public sealed record RowUpdate(
    IReadOnlyDictionary<string, object?> Key,
    IReadOnlyDictionary<string, string?> Changes) : RowEdit;

public sealed record RowDelete(IReadOnlyDictionary<string, object?> Key) : RowEdit;

/// <summary>Turning a value read from a database into the text a grid shows.</summary>
public static class CellText
{
    /// <summary>The text for a cell; null stays null, so the grid can show NULL distinctly.</summary>
    public static string? Of(object? value) => value switch
    {
        null => null,
        byte[] bytes => "0x" + Convert.ToHexString(bytes.Length > 64 ? bytes[..64] : bytes) + (bytes.Length > 64 ? "…" : ""),
        DateTime date => date.ToString(date.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss.FFFFFFF",
            System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF zzz", System.Globalization.CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
