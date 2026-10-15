using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Basalt.Shell.Database;

/// <summary>
/// The last queries run on each connection, newest first, kept in a file beside the IDE settings.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is stored.</b> The query text is stored as it was typed, including any password written in a
/// literal, in a plain JSON file with the permissions of the settings file beside it. Statements that
/// set a password (CREATE or ALTER LOGIN, CREATE or ALTER USER, CREATE or ALTER ROLE ... PASSWORD, SET
/// PASSWORD) are not recorded at all, but a password inside any other statement is not found, so the file
/// should be treated like the settings file. A query longer than <see cref="MaximumQueryLength"/> is not
/// recorded, and the file is kept under <see cref="MaximumFileBytes"/> by dropping the oldest queries.
/// </para>
/// <para>
/// <b>Several windows.</b> A file of its own rather than the settings: the history changes with every run, and
/// the settings are written whole. Two windows (two instances) may share the file, so nothing is cached: every
/// change re-reads the file inside the gate for that path, adds to what it finds, and writes it through a
/// unique temporary name before renaming it over the old one. The gate is shared by every instance in the
/// process, so two windows keep both entries. Across processes the last writer wins, as with the settings file,
/// but the file is never half-written. Every public method hops to the pool first, so no file work starts on the
/// interface thread. A history that cannot be read or written is empty or unsaved, never an error in the query
/// that was run.
/// </para>
/// </remarks>
public sealed class QueryHistory
{
    /// <summary>How many queries are kept for a connection.</summary>
    public const int Capacity = 50;

    /// <summary>The longest query that is recorded, in characters. A pasted script of megabytes is not history.</summary>
    public const int MaximumQueryLength = 64 * 1024;

    /// <summary>The largest the file is allowed to be; the oldest queries go first.</summary>
    public const int MaximumFileBytes = 2 * 1024 * 1024;

    /// <summary>One gate and one list of removed connections per file, shared by every instance on it.</summary>
    private sealed class Shared
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        /// <summary>
        /// Connections removed in this process. A run that was still being recorded when its connection was
        /// removed would otherwise write an entry nothing will ever list or delete. Ids are never reused.
        /// </summary>
        public HashSet<string> Forgotten { get; } = new(StringComparer.Ordinal);
    }

    private static readonly ConcurrentDictionary<string, Shared> Files = new(StringComparer.Ordinal);

    /// <summary>Statements that set a credential: not recorded.</summary>
    private static readonly Regex Credentials = new(
        @"(^|;)\s*(?:(?:CREATE|ALTER)\s+(?:LOGIN|USER)\b|(?:CREATE|ALTER)\s+ROLE\b[^;]*\bPASSWORD\b|SET\s+PASSWORD\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private readonly string _path;
    private readonly Shared _shared;

    public QueryHistory(string path)
    {
        _path = path;
        _shared = Files.GetOrAdd(Path.GetFullPath(path), _ => new Shared());
    }

    /// <summary>
    /// The history file that sits beside a settings file: <c>database-history.json</c> beside settings.json, and
    /// <c>name.database-history.json</c> beside any other name, so a test with a settings file of its own has a
    /// history of its own.
    /// </summary>
    public static string PathBeside(string settingsPath)
    {
        var directory = Path.GetDirectoryName(settingsPath) ?? "";
        var name = Path.GetFileName(settingsPath);

        return Path.Combine(directory, name.Equals("settings.json", StringComparison.OrdinalIgnoreCase)
            ? "database-history.json"
            : Path.GetFileNameWithoutExtension(name) + ".database-history.json");
    }

    /// <summary>Whether a query is kept: not empty, not too long, not a credential, and valid text.</summary>
    public static bool IsRecordable(string sql)
    {
        var text = sql.Trim();

        if (text.Length == 0 || text.Length > MaximumQueryLength) return false;
        if (!IsWellFormed(text)) return false;

        try
        {
            return !Credentials.IsMatch(StripLeadingComments(text));
        }
        catch (RegexMatchTimeoutException)
        {
            // A text the pattern cannot answer for in a second is not worth the risk of keeping.
            return false;
        }
    }

    /// <summary>The queries run on a connection, newest first.</summary>
    public Task<IReadOnlyList<string>> GetAsync(string connectionId) => Task.Run(() => GetCoreAsync(connectionId));

    private async Task<IReadOnlyList<string>> GetCoreAsync(string connectionId)
    {
        await _shared.Gate.WaitAsync().ConfigureAwait(false);

        try
        {
            var all = await LoadAsync().ConfigureAwait(false);

            return all.TryGetValue(connectionId, out var list) ? [.. list] : [];
        }
        finally
        {
            _shared.Gate.Release();
        }
    }

    /// <summary>
    /// Records a query as the newest. Running the same text again moves it to the top rather than listing
    /// it twice; only <see cref="Capacity"/> are kept. Never throws for a file problem.
    /// </summary>
    public Task AddAsync(string connectionId, string sql) => Task.Run(() => AddCoreAsync(connectionId, sql));

    private async Task AddCoreAsync(string connectionId, string sql)
    {
        if (!IsRecordable(sql)) return;

        var text = sql.Trim();

        await _shared.Gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_shared.Forgotten.Contains(connectionId)) return;

            // Read again, inside the gate: another window may have written since this one last looked.
            var all = await LoadAsync().ConfigureAwait(false);

            if (!all.TryGetValue(connectionId, out var list)) all[connectionId] = list = [];

            list.RemoveAll(existing => string.Equals(existing, text, StringComparison.Ordinal));
            list.Insert(0, text);

            if (list.Count > Capacity) list.RemoveRange(Capacity, list.Count - Capacity);

            await SaveAsync(all).ConfigureAwait(false);
        }
        finally
        {
            _shared.Gate.Release();
        }
    }

    /// <summary>Drops a connection's history, as removing the connection does, and refuses later records for it.</summary>
    public Task ForgetAsync(string connectionId) => Task.Run(() => ForgetCoreAsync(connectionId));

    private async Task ForgetCoreAsync(string connectionId)
    {
        await _shared.Gate.WaitAsync().ConfigureAwait(false);

        try
        {
            _shared.Forgotten.Add(connectionId);

            var all = await LoadAsync().ConfigureAwait(false);

            if (all.Remove(connectionId)) await SaveAsync(all).ConfigureAwait(false);
        }
        finally
        {
            _shared.Gate.Release();
        }
    }

    // Called with the gate held. Always reads the file: nothing is cached between calls.
    private async Task<Dictionary<string, List<string>>> LoadAsync()
    {
        try
        {
            if (File.Exists(_path))
            {
                await using var stream = File.OpenRead(_path);

                var read = await JsonSerializer.DeserializeAsync<Dictionary<string, List<string>>>(stream).ConfigureAwait(false);

                if (read is not null) return read;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // An unreadable file is a history that starts again.
        }

        return [];
    }

    // Called with the gate held. Written to a name of its own and moved over, so a crash leaves the old
    // file, not half a new one, and two processes never write the same temporary file.
    private async Task SaveAsync(Dictionary<string, List<string>> all)
    {
        string json;

        try
        {
            json = Serialize(all);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or EncoderFallbackException)
        {
            // Text the encoder refuses (a lone surrogate that came from an old file): drop those entries
            // instead of failing every later save with the same text still in the list.
            foreach (var list in all.Values) list.RemoveAll(q => !IsWellFormed(q));

            json = Serialize(all);
        }

        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) is { Length: > 0 } directory ? directory : ".");

            await File.WriteAllTextAsync(temporary, json).ConfigureAwait(false);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The history is only not kept this time.
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The JSON, with the oldest queries dropped until it fits <see cref="MaximumFileBytes"/>.</summary>
    private static string Serialize(Dictionary<string, List<string>> all)
    {
        var json = JsonSerializer.Serialize(all);

        while (Encoding.UTF8.GetByteCount(json) > MaximumFileBytes)
        {
            // The longest list gives up its oldest query: the busiest connection pays for the size.
            var longest = all.Values.MaxBy(l => l.Count);

            if (longest is null || longest.Count == 0) break;

            longest.RemoveAt(longest.Count - 1);
            json = JsonSerializer.Serialize(all);
        }

        return json;
    }

    private static bool IsWellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;

                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The text after any leading whitespace, <c>--</c> lines and <c>/* */</c> blocks, which do not change what a statement is.</summary>
    private static string StripLeadingComments(string text)
    {
        var rest = text.AsSpan();

        while (true)
        {
            rest = rest.TrimStart();

            if (rest.StartsWith("--"))
            {
                var end = rest.IndexOf('\n');
                if (end < 0) return "";

                rest = rest[(end + 1)..];
            }
            else if (rest.StartsWith("/*"))
            {
                var end = rest.IndexOf("*/");
                if (end < 0) return "";

                rest = rest[(end + 2)..];
            }
            else
            {
                return rest.ToString();
            }
        }
    }
}
