using System.Text;
using System.Text.RegularExpressions;

namespace Basalt.Data.Sessions;

/// <summary>
/// Reading and adjusting the SQL SQLite keeps for an index or a trigger, for the table rebuild.
/// </summary>
/// <remarks>
/// The catalogue (PRAGMA index_info) says nothing of a partial index's WHERE, of DESC or of a column's COLLATE, so a
/// rebuild that recreated indexes from it would lose them without a word. The stored statement has all of it. It is
/// reused as it is, and only the column names in the index's column list are changed for a rename. Anything the
/// adjustment cannot be sure of (an expression or a WHERE that mentions a renamed column) is refused, not guessed.
/// </remarks>
internal static partial class SqliteSchemaSql
{
    public enum Outcome
    {
        /// <summary>The statement can be run as it is.</summary>
        Unchanged,

        /// <summary>The column list was rewritten for the new names.</summary>
        Adjusted,

        /// <summary>A plain column of the index is dropped: the index goes with it.</summary>
        Dropped,

        /// <summary>The statement mentions a renamed or dropped column where it cannot be rewritten safely.</summary>
        Unsafe,
    }

    [GeneratedRegex(
        """^\s*CREATE\s+(?:UNIQUE\s+)?INDEX\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:"(?:[^"]|"")+"|\[[^\]]+\]|`[^`]+`|\S+)\s+ON\s+(?:"(?:[^"]|"")+"|\[[^\]]+\]|`[^`]+`|[^\s(]+)\s*\(""",
        RegexOptions.IgnoreCase)]
    private static partial Regex IndexHeader();

    [GeneratedRegex(
        """^\s*(?<id>"(?:[^"]|"")+"|\[[^\]]+\]|`[^`]+`|[A-Za-z_][A-Za-z0-9_$]*)(?<rest>(?:\s+COLLATE\s+[A-Za-z0-9_"]+)?(?:\s+(?:ASC|DESC))?)\s*$""",
        RegexOptions.IgnoreCase)]
    private static partial Regex PlainTerm();

    /// <summary>
    /// The index statement for the table after its columns were renamed or dropped.
    /// </summary>
    /// <param name="renamed">Stored column name to its new name; a column absent from it was dropped.</param>
    /// <param name="affected">The stored names of the columns that were renamed or dropped.</param>
    public static Outcome AdjustIndex(string sql, IReadOnlyDictionary<string, string> renamed, IReadOnlyCollection<string> affected, Func<string, string> quote, out string adjusted)
    {
        adjusted = sql.Trim().TrimEnd(';') + ";";

        if (affected.Count == 0) return Outcome.Unchanged;

        var header = IndexHeader().Match(sql);

        if (!header.Success) return Outcome.Unsafe;

        var open = header.Index + header.Length;
        var close = MatchingClose(sql, open - 1);

        if (close < 0) return Outcome.Unsafe;

        var terms = SplitTopLevel(sql[open..close]);
        var tail = sql[(close + 1)..];
        var rebuilt = new List<string>();
        var unsafeTerm = false;

        foreach (var term in terms)
        {
            var plain = PlainTerm().Match(term);

            if (!plain.Success)
            {
                // An expression: it can only stay if it names none of the columns that changed.
                if (Mentions(term, affected)) unsafeTerm = true;

                rebuilt.Add(term.Trim());
                continue;
            }

            var name = Unquote(plain.Groups["id"].Value);

            if (!renamed.TryGetValue(name, out var now)) return Outcome.Dropped;

            rebuilt.Add(quote(now) + plain.Groups["rest"].Value);
        }

        if (unsafeTerm || Mentions(tail, affected)) return Outcome.Unsafe;

        adjusted = sql[..open] + string.Join(", ", rebuilt) + ")" + tail.TrimEnd().TrimEnd(';') + ";";

        return Outcome.Adjusted;
    }

    /// <summary>Whether the SQL names any of the columns, as a word, in any quoting.</summary>
    public static bool Mentions(string sql, IEnumerable<string> columns) =>
        columns.Any(c => Regex.IsMatch(sql, "(?<![A-Za-z0-9_])" + Regex.Escape(c) + "(?![A-Za-z0-9_])", RegexOptions.IgnoreCase));

    /// <summary>The name in a CREATE TRIGGER statement, or the start of the statement when it has none we can read.</summary>
    public static string TriggerName(string sql)
    {
        var match = Regex.Match(
            sql,
            """CREATE\s+(?:TEMP\s+|TEMPORARY\s+)?TRIGGER\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<id>"(?:[^"]|"")+"|\[[^\]]+\]|`[^`]+`|[^\s]+)""",
            RegexOptions.IgnoreCase);

        return match.Success ? Unquote(match.Groups["id"].Value) : sql[..Math.Min(40, sql.Length)];
    }

    public static string Unquote(string identifier)
    {
        if (identifier.Length >= 2)
        {
            if (identifier[0] == '"' && identifier[^1] == '"') return identifier[1..^1].Replace("\"\"", "\"");
            if (identifier[0] == '`' && identifier[^1] == '`') return identifier[1..^1].Replace("``", "`");
            if (identifier[0] == '[' && identifier[^1] == ']') return identifier[1..^1];
        }

        return identifier;
    }

    [GeneratedRegex(
        """REFERENCES\s+(?<parent>"(?:[^"]|"")+"|\[[^\]]+\]|`[^`]+`|[A-Za-z_][A-Za-z0-9_$]*)\s*(?:\([^)]*\))?(?<options>(?:\s+(?:ON\s+(?:DELETE|UPDATE)\s+(?:SET\s+NULL|SET\s+DEFAULT|CASCADE|RESTRICT|NO\s+ACTION)|MATCH\s+\S+|NOT\s+DEFERRABLE|DEFERRABLE|INITIALLY\s+(?:DEFERRED|IMMEDIATE)))*)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex ReferencesClause();

    [GeneratedRegex(@"(?<!NOT\s)DEFERRABLE(?:\s+INITIALLY\s+(?:DEFERRED|IMMEDIATE))?", RegexOptions.IgnoreCase)]
    private static partial Regex DeferrableOption();

    /// <summary>
    /// For each table a CREATE TABLE statement refers to, the DEFERRABLE clause of each REFERENCES to it, in the order
    /// written (null for a key that is not deferrable). PRAGMA foreign_key_list does not report deferrability.
    /// </summary>
    public static Dictionary<string, List<string?>> DeferrableByParent(string createTable)
    {
        var found = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in ReferencesClause().Matches(createTable))
        {
            var parent = Unquote(match.Groups["parent"].Value);
            var option = DeferrableOption().Match(match.Groups["options"].Value);

            if (!found.TryGetValue(parent, out var list)) found[parent] = list = [];

            list.Add(option.Success ? Regex.Replace(option.Value.Trim(), @"\s+", " ").ToUpperInvariant() : null);
        }

        return found;
    }

    private static int MatchingClose(string sql, int open)
    {
        var depth = 0;

        for (var i = open; i < sql.Length; i++)
        {
            i = SkipQuoted(sql, i);

            if (sql[i] == '(') depth++;
            else if (sql[i] == ')' && --depth == 0) return i;
        }

        return -1;
    }

    private static List<string> SplitTopLevel(string inner)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < inner.Length; i++)
        {
            i = SkipQuoted(inner, i);

            if (inner[i] == '(') depth++;
            else if (inner[i] == ')') depth--;
            else if (inner[i] == ',' && depth == 0)
            {
                parts.Add(inner[start..i]);
                start = i + 1;
            }
        }

        parts.Add(inner[start..]);

        return parts;
    }

    /// <summary>The index of the last character of a quoted run that starts at <paramref name="i"/>, or <paramref name="i"/> when none does.</summary>
    private static int SkipQuoted(string sql, int i)
    {
        var close = sql[i] switch { '\'' => '\'', '"' => '"', '`' => '`', '[' => ']', _ => '\0' };

        if (close == '\0') return i;

        for (var j = i + 1; j < sql.Length; j++)
        {
            if (sql[j] != close) continue;

            // A doubled quote is a quote inside the run.
            if (close != ']' && j + 1 < sql.Length && sql[j + 1] == close)
            {
                j++;
                continue;
            }

            return j;
        }

        return sql.Length - 1;
    }
}
