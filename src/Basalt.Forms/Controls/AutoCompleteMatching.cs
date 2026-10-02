namespace System.Windows.Forms;

/// <summary>
/// What TextBox and ComboBox share for AutoCompleteMode/AutoCompleteSource:
/// finding which candidates start with the text typed so far.
/// </summary>
/// <remarks>
/// Only <see cref="AutoCompleteSource.CustomSource"/> and (for ComboBox)
/// <see cref="AutoCompleteSource.ListItems"/> are implemented. FileSystem,
/// HistoryList and RecentlyUsedList ask the shell and the running
/// process's own history, neither of which this implementation has a
/// source for; they are a documented gap, not silently wrong (no
/// candidates means no suggestion, exactly as an empty source would).
/// </remarks>
internal static class AutoCompleteMatching
{
    /// <summary>Every candidate that starts with <paramref name="text"/>, case-insensitively in the current culture, without duplicates, in source order.</summary>
    public static List<string> Match(IEnumerable<string> candidates, string text)
    {
        var matches = new List<string>();
        if (string.IsNullOrEmpty(text)) return matches;

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            if (!candidate.StartsWith(text, StringComparison.CurrentCultureIgnoreCase)) continue;
            if (matches.Contains(candidate, StringComparer.CurrentCultureIgnoreCase)) continue;

            matches.Add(candidate);
        }

        return matches;
    }

    /// <summary>The candidate strings AutoCompleteSource asks for: CustomSource's collection, ListItems' own item text, or none for a source this implementation does not have data for.</summary>
    public static IEnumerable<string> CandidatesFor(AutoCompleteSource source, AutoCompleteStringCollection? customSource, Func<IEnumerable<string>>? listItems) => source switch
    {
        AutoCompleteSource.CustomSource when customSource is not null => customSource.Cast<string>(),
        AutoCompleteSource.ListItems when listItems is not null => listItems(),
        _ => [],
    };
}
