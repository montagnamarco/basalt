using Basalt.Core.Model;

namespace Basalt.Workspace.Completion;

/// <summary>How well a candidate matched what was typed.</summary>
public sealed record CompletionMatch(CompletionItem Item, int Score, IReadOnlyList<int> MatchedIndices)
{
    public bool IsMatch => Score > 0;
}

/// <summary>
/// Narrows and orders completions as the user types.
///
/// Ordering is by how well each candidate matches rather than alphabetically:
/// having typed "wl", "WriteLine" should come before "WaitForExitAsyncLater"
/// even though the latter sorts first. Visual Basic is case-insensitive, so
/// matching is too, but a match that also agrees on case ranks higher.
/// </summary>
public static class CompletionMatcher
{
    private const int ExactBonus = 1000;
    private const int PrefixBonus = 500;
    private const int InitialsBonus = 300;
    private const int CaseBonus = 40;
    private const int ConsecutiveBonus = 15;
    private const int WordStartBonus = 25;
    private const int LeadingPenalty = 2;

    /// <summary>Keeps the candidates that match, best first.</summary>
    public static IReadOnlyList<CompletionMatch> Filter(
        IReadOnlyList<CompletionItem> items, string query)
    {
        if (query.Length == 0)
        {
            return [.. items
                .OrderBy(i => i.DisplayText, StringComparer.OrdinalIgnoreCase)
                .Select(i => new CompletionMatch(i, 1, []))];
        }

        return [.. items
            .Select(item => Match(item, query))
            .Where(match => match.IsMatch)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Item.DisplayText.Length)
            .ThenBy(match => match.Item.DisplayText, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Scores one candidate against what was typed.</summary>
    public static CompletionMatch Match(CompletionItem item, string query)
    {
        var text = FilterTextOrDisplay(item);

        if (query.Length == 0) return new CompletionMatch(item, 1, []);
        if (query.Length > text.Length) return new CompletionMatch(item, 0, []);

        if (text.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            var exact = ExactBonus + (text.Equals(query, StringComparison.Ordinal) ? CaseBonus : 0);
            return new CompletionMatch(item, exact, [.. Enumerable.Range(0, text.Length)]);
        }

        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            var prefix = PrefixBonus
                       + (text.StartsWith(query, StringComparison.Ordinal) ? CaseBonus : 0);

            return new CompletionMatch(item, prefix, [.. Enumerable.Range(0, query.Length)]);
        }

        // "wl" should find "WriteLine": the capitals are how a reader picks a
        // long name out of a list.
        if (MatchInitials(text, query) is { Count: > 0 } initials)
            return new CompletionMatch(item, InitialsBonus + ConsecutiveBonus * initials.Count, initials);

        return MatchSubsequence(item, text, query);
    }

    /// <summary>Matches the query against the capitals of a name.</summary>
    private static List<int> MatchInitials(string text, string query)
    {
        var initials = new List<int>();
        var next = 0;

        for (var i = 0; i < text.Length && next < query.Length; i++)
        {
            if (!IsWordStart(text, i)) continue;

            if (char.ToLowerInvariant(text[i]) != char.ToLowerInvariant(query[next])) continue;

            initials.Add(i);
            next++;
        }

        return next == query.Length ? initials : [];
    }

    /// <summary>
    /// Matches the query as letters appearing in order, not necessarily
    /// together.
    ///
    /// This is the loosest match, so it scores lowest; runs of adjacent
    /// letters and matches at word starts pull a candidate back up.
    /// </summary>
    private static CompletionMatch MatchSubsequence(
        CompletionItem item, string text, string query)
    {
        var indices = new List<int>(query.Length);
        var score = 0;
        var next = 0;
        var previous = -2;

        for (var i = 0; i < text.Length && next < query.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) != char.ToLowerInvariant(query[next])) continue;

            if (i == previous + 1) score += ConsecutiveBonus;
            if (IsWordStart(text, i)) score += WordStartBonus;
            if (text[i] == query[next]) score += CaseBonus / 4;

            indices.Add(i);
            previous = i;
            next++;
        }

        if (next < query.Length) return new CompletionMatch(item, 0, []);

        // A match starting deep inside a long name is a weaker one.
        score = Math.Max(1, score - indices[0] * LeadingPenalty);

        return new CompletionMatch(item, score, indices);
    }

    /// <summary>Whether a position begins a word, by capital or separator.</summary>
    private static bool IsWordStart(string text, int index)
    {
        if (index == 0) return true;

        var previous = text[index - 1];

        if (previous is '_' or '.') return true;

        return char.IsUpper(text[index]) && !char.IsUpper(previous);
    }

    private static string FilterTextOrDisplay(CompletionItem item) =>
        item.InsertionText.Length > 0 ? item.InsertionText : item.DisplayText;
}
