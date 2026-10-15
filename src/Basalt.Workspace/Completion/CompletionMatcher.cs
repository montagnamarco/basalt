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
/// A candidate matches when it starts with what was typed, ignoring case, or
/// when what was typed spells its capitals ("wl" finds WriteLine). Nothing
/// else matches: a list that also offered names with the letters scattered
/// through them would let a commit character take a name nobody meant.
///
/// Ordering is by how well each candidate matches, then by the language
/// service's preselection and rank, then by length and alphabetically. Exact
/// matches come before case-insensitive prefixes, and those before capitals.
/// </summary>
public static class CompletionMatcher
{
    private const int ExactBonus = 1000;
    private const int PrefixBonus = 500;
    private const int InitialsBonus = 300;
    private const int CaseBonus = 40;
    private const int PerLetterBonus = 15;

    /// <summary>
    /// Keeps the candidates that match what was typed, best first, with one
    /// row per name: overloads of a method are one row, and so is a keyword
    /// with the type of the same name; the same name from two namespaces is
    /// two rows.
    /// </summary>
    /// <remarks>
    /// The whole candidate set is filtered here, and the caller caps what it
    /// shows afterwards. A cap taken before filtering, in alphabetical order,
    /// is what hid the keywords and the locals and left "AnsiStringMarshaller"
    /// as the only match for "String".
    /// </remarks>
    public static IReadOnlyList<CompletionMatch> Filter(
        IReadOnlyList<CompletionItem> items, string query)
    {
        var matches = query.Length == 0
            ? items.Select(item => new CompletionMatch(item, 1, [])).ToList()
            : items.Select(item => Match(item, query)).Where(match => match.IsMatch).ToList();

        // Among one name the keyword comes first ("String" the type keyword,
        // not System.String the class), then the kind and the text, so which of
        // two equal entries survives the one-row-per-name cut never depends on
        // the order the language service happened to return them in.
        var ordered = query.Length == 0
            ? matches
                .OrderByDescending(match => match.Item.IsPreselected)
                .ThenByDescending(match => match.Item.Rank)
                .ThenBy(match => match.Item.DisplayText, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(match => match.Item.Kind == CompletionKind.Keyword)
                .ThenBy(match => match.Item.Kind)
                .ThenBy(match => match.Item.InsertionText, StringComparer.Ordinal)
            : matches
                .OrderByDescending(match => match.Score)
                .ThenByDescending(match => match.Item.IsPreselected)
                .ThenByDescending(match => match.Item.Rank)
                .ThenBy(match => match.Item.DisplayText.Length)
                .ThenBy(match => match.Item.DisplayText, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(match => match.Item.Kind == CompletionKind.Keyword)
                .ThenBy(match => match.Item.Kind)
                .ThenBy(match => match.Item.InsertionText, StringComparer.Ordinal);

        var rows = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);

        return [.. ordered.Where(match => KeepRow(rows, match.Item))];
    }

    /// <summary>
    /// Whether an entry opens a row of its own, given the containers of the
    /// names already kept under its name.
    /// </summary>
    private static bool KeepRow(Dictionary<string, List<string?>> rows, CompletionItem item)
    {
        if (!rows.TryGetValue(item.DisplayText, out var containers))
        {
            rows[item.DisplayText] = [item.Container];
            return true;
        }

        // A name with no container, a keyword or a local, is the same name as
        // the one already kept whatever its container: the row is shared.
        if (item.Container is null || containers.Any(kept =>
                kept is null || string.Equals(kept, item.Container, StringComparison.OrdinalIgnoreCase)))
            return false;

        containers.Add(item.Container);
        return true;
    }

    /// <summary>Scores one candidate against what was typed; zero when it does not match.</summary>
    public static CompletionMatch Match(CompletionItem item, string query)
    {
        var text = MatchText(item);
        var typed = ComparableQuery(query, text);

        if (typed.Length == 0) return new CompletionMatch(item, 1, []);
        if (typed.Length > text.Length) return new CompletionMatch(item, 0, []);

        if (text.Equals(typed, StringComparison.OrdinalIgnoreCase))
        {
            var exact = ExactBonus + (text.Equals(typed, StringComparison.Ordinal) ? CaseBonus : 0);
            return new CompletionMatch(item, exact, [.. Enumerable.Range(0, text.Length)]);
        }

        if (text.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
        {
            var prefix = PrefixBonus
                       + (text.StartsWith(typed, StringComparison.Ordinal) ? CaseBonus : 0);

            return new CompletionMatch(item, prefix, [.. Enumerable.Range(0, typed.Length)]);
        }

        // "wl" should find "WriteLine": the capitals are how a reader picks a
        // long name out of a list.
        if (MatchInitials(text, typed) is { Count: > 0 } initials)
            return new CompletionMatch(item, InitialsBonus + PerLetterBonus * initials.Count, initials);

        return new CompletionMatch(item, 0, []);
    }

    /// <summary>
    /// Whether Enter or Tab may take the highlighted entry, given what was
    /// typed.
    /// </summary>
    /// <remarks>
    /// With something typed, the entry must match it: a Tab after "Str" takes
    /// "String", a Tab after "Strq" does not. With nothing typed, the list is
    /// only a suggestion: only an entry the language service preselected, or
    /// one the user moved the highlight to with the arrow keys, may be taken.
    /// The first entry of an unchosen list is never taken by a key.
    /// </remarks>
    public static bool MayCommit(CompletionItem item, string typed, bool chosen) =>
        typed.Length == 0 ? item.IsPreselected || chosen : Match(item, typed).IsMatch;

    /// <summary>
    /// Whether a key that ends a word, a space or punctuation, may take the
    /// highlighted entry.
    /// </summary>
    /// <param name="typed">The name written since the list opened.</param>
    /// <param name="entriesMatching">How many entries the list holds for <paramref name="typed"/>.</param>
    /// <param name="chosen">Whether the user moved the highlight to this entry.</param>
    /// <param name="character">
    /// The character typed. "(", "." and "," are commit characters, as in Visual
    /// Studio: the highlighted entry is taken when it begins with what was typed,
    /// so "Console.Wr(" writes Write. A space is not one, and takes an entry
    /// only as the rules below say.
    /// </param>
    /// <remarks>
    /// Stricter than Enter: "st" followed by a space is a word still being
    /// written, and must not become "Static" because it was the first of
    /// several names. The entry is taken only when the typed text is the entry
    /// itself, when the user chose it, or when it is the one name the typed
    /// text can mean. With nothing typed, the rule of <see cref="MayCommit"/>
    /// applies. In suggestion mode a name is being declared, so only the name
    /// written in full is taken.
    /// </remarks>
    public static bool MayCommitOnPunctuation(
        CompletionItem item, string typed, int entriesMatching, bool chosen, char? character = null)
    {
        if (typed.Length == 0) return item.IsPreselected || chosen;

        if (!Match(item, typed).IsMatch) return false;

        if (item.SoftSelection) return IsWhole(item, typed);

        if (chosen || IsWhole(item, typed)) return true;

        if (character is '(' or '.' or ',' && IsPrefix(item, typed)) return true;

        return entriesMatching == 1;
    }

    /// <summary>The text the list matches what was typed against.</summary>
    private static string MatchText(CompletionItem item) =>
        !string.IsNullOrEmpty(item.FilterText) ? item.FilterText
        : item.InsertionText.Length > 0 ? item.InsertionText
        : item.DisplayText;

    /// <summary>
    /// What was typed, as the text is compared with it. A Razor directive
    /// attribute is typed with its "@", which its filter text leaves out:
    /// "@oncl" finds "onclick" the way "oncl" does.
    /// </summary>
    private static string ComparableQuery(string typed, string text) =>
        typed.Length > 1 && typed[0] == '@' && !text.StartsWith('@') ? typed[1..] : typed;

    private static bool IsWhole(CompletionItem item, string typed)
    {
        var text = MatchText(item);

        return string.Equals(text, ComparableQuery(typed, text), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrefix(CompletionItem item, string typed)
    {
        var text = MatchText(item);

        return text.StartsWith(ComparableQuery(typed, text), StringComparison.OrdinalIgnoreCase);
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
    /// Whether a letter begins a word, by capital or separator.
    /// </summary>
    /// <remarks>
    /// Only a letter can begin one. A digit after an underscore is not a word
    /// start: "2" was matching the "256" of "SHA3_256", and a comma typed after
    /// it committed that name in the middle of an argument list.
    /// </remarks>
    private static bool IsWordStart(string text, int index)
    {
        if (!char.IsLetter(text[index])) return false;
        if (index == 0) return true;

        var previous = text[index - 1];

        // The separators of the names the markup and style providers offer:
        // "bc" finds background-color as it finds Write_Line.
        if (previous is '_' or '.' or '-') return true;

        return char.IsUpper(text[index]) && !char.IsUpper(previous);
    }
}
