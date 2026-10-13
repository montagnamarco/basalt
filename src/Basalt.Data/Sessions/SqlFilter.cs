namespace Basalt.Data.Sessions;

/// <summary>
/// The WHERE clause and the page bounds every dialect and the row count share.
/// </summary>
public static class SqlFilter
{
    /// <summary>
    /// The WHERE clause for a filter the user typed, or nothing when there is none.
    /// </summary>
    /// <remarks>
    /// The filter is SQL the user writes, as a query is: it is not a value to
    /// escape. A filter may contain a closing parenthesis or a second statement,
    /// and the engine runs whatever it says. That is accepted for a developer tool,
    /// where the query window runs anything anyway, and the filter is never built
    /// from data the user did not type.
    ///
    /// The condition is wrapped in parentheses and the closing one starts a new
    /// line. A trailing <c>--</c> comment then ends at the newline, so it cannot
    /// swallow the ORDER BY, the LIMIT or the closing parenthesis. Appended raw,
    /// a filter ending in <c>--</c> dropped the paging, and the page came back
    /// unbounded; a filter with its own ORDER BY made a second one.
    /// </remarks>
    public static string Where(string? filter) =>
        string.IsNullOrWhiteSpace(filter) ? "" : " WHERE (" + filter.Trim() + "\n)";

    /// <summary>Refuses a page that no engine can answer: a negative offset, or a page of no rows.</summary>
    public static void CheckPage(int offset, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
    }
}
