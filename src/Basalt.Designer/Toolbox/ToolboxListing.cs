namespace Basalt.Designer.Toolbox;

/// <summary>A category heading in a toolbox listing, with the state its row needs.</summary>
/// <param name="Name">The category, as the catalogue names it.</param>
/// <param name="Count">How many entries the category holds under the current filter.</param>
/// <param name="Collapsed">Whether its entries are hidden. Never true while a filter is active.</param>
public sealed record ToolboxHeading(string Name, int Count, bool Collapsed);

/// <summary>
/// Turns a toolbox catalogue into the flat run of rows the panel shows:
/// a heading, then the entries of its category, filtered and collapsed.
///
/// Kept apart from the view because the rules are the part worth testing and
/// the same for both toolboxes. A filter that typed "check" must find
/// "Check Box" and "CheckedListBox" and show their categories open, whatever
/// the user had folded before; a category folded by hand must stay folded
/// when nothing is typed.
/// </summary>
public static class ToolboxListing
{
    /// <summary>
    /// The rows for <paramref name="items"/>: <see cref="ToolboxHeading"/> and the items themselves.
    /// </summary>
    /// <param name="items">Everything on offer, in catalogue order.</param>
    /// <param name="category">The category an item belongs to.</param>
    /// <param name="searchText">The names a filter is matched against (display name, element name).</param>
    /// <param name="filter">What the user typed; blank shows everything.</param>
    /// <param name="collapsed">Categories the user folded. Ignored while filtering.</param>
    public static List<object> Build<T>(
        IEnumerable<T> items,
        Func<T, string> category,
        Func<T, IEnumerable<string>> searchText,
        string? filter,
        IReadOnlySet<string> collapsed) where T : notnull
    {
        var query = Normalise(filter);
        var filtering = query.Length > 0;
        var rows = new List<object>();

        foreach (var group in items
                     .Where(item => !filtering || searchText(item).Any(text => Normalise(text).Contains(query, StringComparison.Ordinal)))
                     .GroupBy(category))
        {
            var entries = group.ToList();
            var fold = !filtering && collapsed.Contains(group.Key);

            rows.Add(new ToolboxHeading(group.Key, entries.Count, fold));

            if (!fold) rows.AddRange(entries.Cast<object>());
        }

        return rows;
    }

    /// <summary>
    /// Lower case with spaces and punctuation dropped, so "check box",
    /// "CheckBox" and "check-box" are one word.
    /// </summary>
    /// <remarks>
    /// The catalogue spells "Check Box" with a space and the markup spells it
    /// without; people type either, and a filter that only knows one of them
    /// looks broken for the other.
    /// </remarks>
    private static string Normalise(string? text) =>
        string.Concat((text ?? "").Where(char.IsLetterOrDigit)).ToLowerInvariant();
}
