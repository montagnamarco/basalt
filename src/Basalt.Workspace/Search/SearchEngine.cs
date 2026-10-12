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

/// <summary>One change a replacement makes: the span it takes and the text put in its place.</summary>
public sealed record TextReplacement(int Offset, int Length, string Text);

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

        // The matches are read here rather than in the loop: a pattern that
        // backtracks past its time limit throws while they are being found, and
        // that must end this file's search, not the whole solution's.
        List<Match> matches;
        try
        {
            matches = pattern.Matches(text).ToList();
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }

        foreach (var match in matches)
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
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
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
    /// The replacements a Replace All would make in a piece of text, in document
    /// order and without applying them. An editor applies them as one undoable
    /// edit; a file on disk is rewritten by <see cref="ReplaceInFileAsync"/>.
    /// </summary>
    public IReadOnlyList<TextReplacement> PlanReplacements(
        string text, string term, string replacement, SearchOptions options)
    {
        if (string.IsNullOrEmpty(term)) return [];

        try
        {
            var pattern = BuildPattern(term, options);
            var plan = new List<TextReplacement>();

            foreach (Match match in pattern.Matches(text))
            {
                if (match.Length == 0) continue;

                // Only a regular expression search reads $1 and friends, as in ReplaceInText.
                var replaced = options.UseRegex ? match.Result(replacement) : replacement;
                plan.Add(new TextReplacement(match.Index, match.Length, replaced));
            }

            return plan;
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return [];
        }
    }

    /// <summary>
    /// Why the term cannot be searched for as a regular expression, or null when
    /// it can. A literal term is always searchable; only a pattern can be invalid.
    /// </summary>
    public string? PatternError(string term, SearchOptions options)
    {
        if (!options.UseRegex || string.IsNullOrEmpty(term)) return null;

        try
        {
            _ = BuildPattern(term, options);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Searches every file under a directory.
    ///
    /// Results arrive per file rather than as one flat list, because that is
    /// how they are shown and because a caller can start displaying the first
    /// file before the search finishes.
    /// </summary>
    public IAsyncEnumerable<FileHits> SearchDirectoryAsync(
        string directory, string term, SearchOptions options, CancellationToken ct = default) =>
        SearchFilesAsync(directory, term, options, null, ct);

    /// <summary>
    /// Searches every file under a directory, reading each open file as its
    /// buffer holds it and every other file as the disk holds it.
    /// </summary>
    /// <remarks>
    /// A file the user has edited but not saved is searched as edited: that is
    /// the text on screen, and the text a replace changes. Results and
    /// replacements then agree with what the user sees.
    /// </remarks>
    /// <param name="openTexts">Buffer text keyed by full path; null searches the disk only.</param>
    public async IAsyncEnumerable<FileHits> SearchFilesAsync(
        string directory,
        string term,
        SearchOptions options,
        IReadOnlyDictionary<string, string>? openTexts,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(term) || !Directory.Exists(directory)) yield break;

        foreach (var file in EnumerateFiles(directory, options))
        {
            ct.ThrowIfCancellationRequested();

            string text;
            var buffered = openTexts is not null && openTexts.TryGetValue(Path.GetFullPath(file), out var buffer)
                ? buffer
                : null;
            if (buffered is not null)
                text = buffered;
            else try
            {
                text = (await ReadTextFileAsync(file, ct).ConfigureAwait(false)).Text;
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
            if (await ReplaceInFileAsync(file.FilePath, term, replacement, options, ct).ConfigureAwait(false))
                changed++;
        }

        return changed;
    }

    /// <summary>
    /// Replaces in one file on disk, writing it back in the encoding it was read
    /// in. Returns whether the file changed.
    /// </summary>
    public async Task<bool> ReplaceInFileAsync(
        string path, string term, string replacement, SearchOptions options, CancellationToken ct = default)
    {
        var source = await ReadTextFileAsync(path, ct).ConfigureAwait(false);
        var updated = ReplaceInText(source.Text, term, replacement, options);

        if (string.Equals(source.Text, updated, StringComparison.Ordinal)) return false;

        await File.WriteAllBytesAsync(path, source.Encode(updated), ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// A file's text, with what it takes to write the text back unchanged: the
    /// byte order mark it came with and the encoding its bytes are read in.
    /// </summary>
    private sealed record TextFile(string Text, byte[] Preamble, Encoding TextEncoding)
    {
        public byte[] Encode(string text)
        {
            var body = TextEncoding.GetBytes(text);
            if (Preamble.Length == 0) return body;

            var bytes = new byte[Preamble.Length + body.Length];
            Preamble.CopyTo(bytes, 0);
            body.CopyTo(bytes, Preamble.Length);
            return bytes;
        }
    }

    // Strict, so bytes that are not UTF-8 are noticed rather than replaced with
    // U+FFFD, which would write a different file back over the user's.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static async Task<TextFile> ReadTextFileAsync(string path, CancellationToken ct) =>
        DecodeText(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false));

    /// <summary>
    /// Decodes a file the way the editor opens one: a byte order mark decides
    /// UTF-8 or UTF-16, otherwise the bytes are UTF-8 when they are valid as it.
    /// </summary>
    /// <remarks>
    /// Anything else, such as a Windows-1252 file with an accented letter in a
    /// comment, is read as Latin-1. That maps every byte to one character and
    /// back, so Replace All changes the text it matches and writes every other
    /// byte back as it was. Before, the accented byte became U+FFFD on the way
    /// out and the file was corrupted by a replace that did not touch it.
    /// </remarks>
    private static TextFile DecodeText(byte[] bytes)
    {
        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF) && TryDecodeUtf8(bytes, 3, out var marked))
            return new TextFile(marked, [0xEF, 0xBB, 0xBF], Utf8);

        if (HasPrefix(bytes, 0xFF, 0xFE))
            return new TextFile(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), [0xFF, 0xFE], Encoding.Unicode);

        if (HasPrefix(bytes, 0xFE, 0xFF))
            return new TextFile(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), [0xFE, 0xFF], Encoding.BigEndianUnicode);

        if (TryDecodeUtf8(bytes, 0, out var plain))
            return new TextFile(plain, [], Utf8);

        return new TextFile(Encoding.Latin1.GetString(bytes), [], Encoding.Latin1);
    }

    private static bool HasPrefix(byte[] bytes, params byte[] prefix) =>
        bytes.Length >= prefix.Length && bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private static bool TryDecodeUtf8(byte[] bytes, int start, out string text)
    {
        try
        {
            text = StrictUtf8.GetString(bytes, start, bytes.Length - start);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = "";
            return false;
        }
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
        // The boundaries go around the whole pattern, in a group. Beside only the
        // first and last branch, "Greet|Shout" became \bGreet|Shout\b and matched
        // inside "Greeting".
        if (options.WholeWord)
        {
            var leading = body.Length > 0 && IsWordCharacter(term[0]) ? @"\b" : "";
            var trailing = body.Length > 0 && IsWordCharacter(term[^1]) ? @"\b" : "";
            body = leading + "(?:" + body + ")" + trailing;
        }

        var flags = RegexOptions.Multiline;
        if (!options.MatchCase) flags |= RegexOptions.IgnoreCase;

        return new Regex(body, flags, TimeSpan.FromSeconds(2));
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

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
