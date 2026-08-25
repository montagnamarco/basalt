using System.Text;
using System.Text.RegularExpressions;

namespace Basalt.Workspace.Search;

/// <summary>How a search interprets what it was given.</summary>
public sealed record SearchOptions
{
    public bool MatchCase { get; init; }
    public bool WholeWord { get; init; }
    public bool UseRegex { get; init; }

    /// <summary>Files to look in, as glob-like suffixes such as ".vb".</summary>
    public IReadOnlyList<string> IncludeExtensions { get; init; } = [];

    /// <summary>Directory names never searched.</summary>
    public IReadOnlyList<string> ExcludeDirectories { get; init; } =
        ["bin", "obj", ".git", ".vs", "node_modules"];
}

/// <summary>One place a search term was found.</summary>
public sealed record SearchHit(
    string FilePath,
    int Line,
    int Column,
    int Offset,
    int Length,
    string LineText);

/// <summary>Hits within one file, kept together for display.</summary>
public sealed record FileHits(string FilePath, IReadOnlyList<SearchHit> Hits);

/// <summary>
/// Finding and replacing text, in one document or across a solution.
///
/// The engine works on strings and paths, with no editor behind it, so a search
/// can be run and checked without an IDE.
/// </summary>
public sealed class SearchEngine
{
    /// <summary>Hits in a single piece of text.</summary>
    public IReadOnlyList<SearchHit> FindInText(
        string text, string term, SearchOptions options, string filePath = "")
    {
        if (string.IsNullOrEmpty(term)) return [];

        Regex pattern;
        try
        {
            pattern = BuildPattern(term, options);
        }
        catch (ArgumentException)
        {
            // A regular expression the user is still typing is not an error to
            // report, it is simply a search that matches nothing yet.
            return [];
        }

        var hits = new List<SearchHit>();
        var lineStarts = BuildLineIndex(text);

        foreach (Match match in pattern.Matches(text))
        {
            // Zero-width matches would loop forever over the same position.
            if (match.Length == 0) continue;

            var (line, column) = PositionOf(match.Index, lineStarts);

            hits.Add(new SearchHit(
                filePath,
                line,
                column,
                match.Index,
                match.Length,
                LineAt(text, lineStarts, line)));
        }

        return hits;
    }

    /// <summary>Replaces every hit in a piece of text.</summary>
    public string ReplaceInText(
        string text, string term, string replacement, SearchOptions options)
    {
        if (string.IsNullOrEmpty(term)) return text;

        try
        {
            var pattern = BuildPattern(term, options);

            // A literal search must not treat $1 in the replacement as a group
            // reference: only a regular expression search does.
            return options.UseRegex
                ? pattern.Replace(text, replacement)
                : pattern.Replace(text, replacement.Replace("$", "$$"));
        }
        catch (ArgumentException)
        {
            return text;
        }
    }

    /// <summary>Replaces a single hit, leaving the rest of the text alone.</summary>
    public string ReplaceHit(string text, SearchHit hit, string replacement)
    {
        if (hit.Offset < 0 || hit.Offset + hit.Length > text.Length) return text;

        return string.Concat(
            text.AsSpan(0, hit.Offset),
            replacement,
            text.AsSpan(hit.Offset + hit.Length));
    }

    /// <summary>
    /// Searches every file under a directory.
    ///
    /// Results arrive per file rather than as one flat list, because that is
    /// how they are shown and because a caller can start displaying the first
    /// file before the search finishes.
    /// </summary>
    public async IAsyncEnumerable<FileHits> SearchDirectoryAsync(
        string directory,
        string term,
        SearchOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(term) || !Directory.Exists(directory)) yield break;

        foreach (var file in EnumerateFiles(directory, options))
        {
            ct.ThrowIfCancellationRequested();

            string text;
            try
            {
                text = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file being written or out of reach is skipped rather than
                // stopping the whole search.
                continue;
            }

            // Binary content produces meaningless hits and unreadable previews.
            if (LooksBinary(text)) continue;

            var hits = FindInText(text, term, options, file);
            if (hits.Count > 0) yield return new FileHits(file, hits);
        }
    }

    /// <summary>
    /// Replaces across a directory, returning how many files changed.
    /// </summary>
    public async Task<int> ReplaceInDirectoryAsync(
        string directory,
        string term,
        string replacement,
        SearchOptions options,
        CancellationToken ct = default)
    {
        var changed = 0;

        await foreach (var file in SearchDirectoryAsync(directory, term, options, ct)
                           .ConfigureAwait(false))
        {
            var text = await File.ReadAllTextAsync(file.FilePath, ct).ConfigureAwait(false);
            var updated = ReplaceInText(text, term, replacement, options);

            if (string.Equals(text, updated, StringComparison.Ordinal)) continue;

            await File.WriteAllTextAsync(file.FilePath, updated, ct).ConfigureAwait(false);
            changed++;
        }

        return changed;
    }

    private IEnumerable<string> EnumerateFiles(string directory, SearchOptions options)
    {
        var excluded = new HashSet<string>(options.ExcludeDirectories, StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(directory);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (Directory.Exists(entry))
                {
                    if (!excluded.Contains(Path.GetFileName(entry))) pending.Push(entry);
                    continue;
                }

                if (options.IncludeExtensions.Count > 0 &&
                    !options.IncludeExtensions.Contains(
                        Path.GetExtension(entry), StringComparer.OrdinalIgnoreCase))
                    continue;

                yield return entry;
            }
        }
    }

    private static Regex BuildPattern(string term, SearchOptions options)
    {
        var body = options.UseRegex ? term : Regex.Escape(term);

        // Word boundaries only make sense next to word characters; adding them
        // around punctuation would match nothing.
        if (options.WholeWord)
        {
            if (body.Length > 0 && char.IsLetterOrDigit(term[0])) body = @"\b" + body;
            if (body.Length > 0 && char.IsLetterOrDigit(term[^1])) body += @"\b";
        }

        var flags = RegexOptions.Multiline;
        if (!options.MatchCase) flags |= RegexOptions.IgnoreCase;

        return new Regex(body, flags, TimeSpan.FromSeconds(2));
    }

    private static List<int> BuildLineIndex(string text)
    {
        var starts = new List<int> { 0 };

        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n') starts.Add(i + 1);

        return starts;
    }

    private static (int Line, int Column) PositionOf(int offset, List<int> lineStarts)
    {
        // Binary search: a linear scan would make a search over a large file
        // quadratic in the number of hits.
        var index = lineStarts.BinarySearch(offset);
        if (index < 0) index = ~index - 1;

        return (index + 1, offset - lineStarts[index] + 1);
    }

    private static string LineAt(string text, List<int> lineStarts, int line)
    {
        var start = lineStarts[line - 1];
        var end = line < lineStarts.Count ? lineStarts[line] - 1 : text.Length;

        if (end > start && text[end - 1] == '\r') end--;

        return text[start..Math.Max(start, end)];
    }

    /// <summary>
    /// Whether content looks binary, judged by a null byte near the start.
    /// </summary>
    private static bool LooksBinary(string text)
    {
        var limit = Math.Min(text.Length, 8000);

        for (var i = 0; i < limit; i++)
            if (text[i] == '\0') return true;

        return false;
    }
}
