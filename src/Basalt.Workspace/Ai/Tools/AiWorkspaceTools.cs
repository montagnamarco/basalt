using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Basalt.Workspace.Search;

namespace Basalt.Workspace.Ai;

/// <summary>
/// The tools an assistant uses to look at the solution and propose changes.
///
/// All but one only read. <c>propose_edits</c> writes nothing either: it
/// builds an <see cref="AiChangeSet"/> the user reviews, which is how an
/// assistant's change reaches the editor without ever bypassing the user.
///
/// Handlers are run off the interface thread by the tool loop; anything that
/// needs the editors goes through <see cref="IAiWorkspaceHost"/>.
/// </summary>
public sealed class AiWorkspaceTools
{
    /// <summary>Largest file a tool will read or propose to replace.</summary>
    internal const long MaxFileBytes = 2 * 1024 * 1024;

    private const int DefaultLines = 2000;
    private const int MaxListed = 500;
    private const int MaxHits = 100;
    private const int MaxDiagnostics = 100;
    private const int MaxFilesPerProposal = 20;

    private readonly Func<IAiWorkspaceHost?> _hostProvider;
    private IAiWorkspaceHost? _host;
    private AiWorkspaceSandbox? _sandbox;
    private string? _rootText;

    // What the assistant has been shown of each file. A change is refused
    // when the file is no longer what the assistant read.
    private readonly Dictionary<string, string> _seen = new(PathIdentity.Comparer);

    private readonly object _gate = new();

    public AiWorkspaceTools(IAiWorkspaceHost host) : this(() => host)
    {
    }

    /// <summary>
    /// Tools for whichever solution is open when they are called.
    ///
    /// The set of tools never changes, whether or not a solution is open: the
    /// tool list is part of what the model's conversation is cached and
    /// signed against, so a tool without a solution answers with an error
    /// instead of disappearing.
    /// </summary>
    public AiWorkspaceTools(Func<IAiWorkspaceHost?> hostProvider) => _hostProvider = hostProvider;

    private IAiWorkspaceHost Host => Context().Host;

    private AiWorkspaceSandbox Sandbox => Context().Sandbox;

    private (IAiWorkspaceHost Host, AiWorkspaceSandbox Sandbox) Context()
    {
        var host = _hostProvider()
            ?? throw new AiToolException("No solution is open, so there are no files to look at.");

        lock (_gate)
        {
            if (!ReferenceEquals(host, _host) || _sandbox is null
                || !string.Equals(_rootText, host.RootDirectory, StringComparison.Ordinal))
            {
                _host = host;
                _rootText = host.RootDirectory;
                _sandbox = new AiWorkspaceSandbox(host.RootDirectory);

                // What was read belongs to the solution it was read in.
                _seen.Clear();
            }

            return (_host, _sandbox);
        }
    }

    /// <summary>Raised when the assistant proposes a change set.</summary>
    public event EventHandler<AiChangeSet>? ChangeProposed;

    /// <summary>The tools, sorted by name: a stable list keeps the prompt cache valid.</summary>
    public IReadOnlyList<AiTool> Create() =>
    [
        .. new[] { FindSymbol(), GetDiagnostics(), ListFiles(), ProposeEdits(), ReadFile(), SearchText() }
            .OrderBy(t => t.Name, StringComparer.Ordinal)
    ];

    // list_files

    private AiTool ListFiles() => new(
        "list_files",
        "List files in the solution folder, with paths relative to it. Call this to discover the "
      + "layout of the project before reading anything. 'glob' filters by path: '*' matches within "
      + "a folder, '**' across folders, '?' one character (for example '**/*.vb' or 'src/*/Program.vb'). "
      + "Build output, .git and files that may hold secrets are never listed.",
        AiSchema.Object([("glob", AiSchema.Property("string", "Path pattern; default lists everything."))]),
        (input, ct) =>
        {
            var pattern = input["glob"]?.GetValue<string>();
            Regex? filter = string.IsNullOrWhiteSpace(pattern) ? null : GlobToRegex(pattern);

            var found = new List<string>();
            var truncated = false;

            try
            {
                foreach (var file in EnumerateFiles(Sandbox.Root))
                {
                    ct.ThrowIfCancellationRequested();

                    var relative = Sandbox.Relative(file);

                    if (filter is not null && !filter.IsMatch(relative)) continue;

                    if (found.Count >= MaxListed) { truncated = true; break; }

                    found.Add(relative);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                throw new AiToolException("The glob took too long to match; simplify it.");
            }

            found.Sort(StringComparer.Ordinal);

            if (found.Count == 0) return Task.FromResult(new AiToolResult("No files match."));

            var text = string.Join('\n', found);
            if (truncated) text += $"\n(more than {MaxListed} files; narrow the glob)";

            return Task.FromResult(new AiToolResult(text));
        });

    // read_file

    private AiTool ReadFile() => new(
        "read_file",
        "Read a file from the solution folder, with line numbers ('  12| text'). The text is what is in "
      + "the editor when the file is open, saved or not. Pass start_line and end_line (1-based, "
      + "inclusive) to read part of a large file. Read a file before proposing to replace it.",
        AiSchema.Object(
            [("path", AiSchema.Property("string", "Path relative to the solution folder.")),
             ("start_line", AiSchema.Property("integer", "First line to return, 1-based.")),
             ("end_line", AiSchema.Property("integer", "Last line to return, inclusive."))],
            "path"),
        async (input, ct) =>
        {
            var full = Sandbox.Resolve(input["path"]!.GetValue<string>());
            var text = await ReadCurrentAsync(full, ct).ConfigureAwait(false)
                       ?? throw new AiToolException($"'{Sandbox.Relative(full)}' does not exist.");

            // Remembered whole, whatever part is returned: that is what a
            // later proposal is checked against.
            lock (_gate) _seen[full] = text;

            var lines = text.Replace("\r\n", "\n").Split('\n');
            var start = Math.Max(1, Integer(input, "start_line") ?? 1);
            var end = Math.Min(lines.Length, Integer(input, "end_line") ?? start + DefaultLines - 1);

            if (start > lines.Length)
                throw new AiToolException($"The file has {lines.Length} lines.");

            if (end < start) throw new AiToolException("end_line is before start_line.");

            var result = new StringBuilder();

            for (var i = start; i <= end; i++)
                result.Append($"{i,6}| ").Append(lines[i - 1]).Append('\n');

            if (end < lines.Length)
                result.Append($"(lines {end + 1}-{lines.Length} not shown; ask with start_line)\n");

            return new AiToolResult(result.ToString());
        });

    // search_text

    private AiTool SearchText() => new(
        "search_text",
        "Search the text of the solution's files (open editors included) for a string or, with "
      + "regex true, a regular expression. Returns 'path:line: text' for each match, up to "
      + $"{MaxHits}. 'glob' limits the files searched, as in list_files.",
        AiSchema.Object(
            [("query", AiSchema.Property("string", "What to look for.")),
             ("regex", AiSchema.Property("boolean", "Treat the query as a regular expression.")),
             ("case_sensitive", AiSchema.Property("boolean", "Match case; default false.")),
             ("glob", AiSchema.Property("string", "Only search files whose path matches."))],
            "query"),
        async (input, ct) =>
        {
            var query = input["query"]!.GetValue<string>();
            if (query.Length == 0) throw new AiToolException("The query is empty.");

            var options = new SearchOptions
            {
                UseRegex = input["regex"]?.GetValue<bool>() ?? false,
                MatchCase = input["case_sensitive"]?.GetValue<bool>() ?? false
            };

            if (options.UseRegex)
            {
                try { _ = new Regex(query); }
                catch (ArgumentException ex) { throw new AiToolException($"Invalid regular expression: {ex.Message}"); }
            }

            var pattern = input["glob"]?.GetValue<string>();
            Regex? filter = string.IsNullOrWhiteSpace(pattern) ? null : GlobToRegex(pattern);

            var buffers = await BuffersAsync(ct).ConfigureAwait(false);
            var engine = new SearchEngine();
            var result = new StringBuilder();
            var count = 0;

            try
            {
                foreach (var file in EnumerateFiles(Sandbox.Root))
                {
                    ct.ThrowIfCancellationRequested();

                    var relative = Sandbox.Relative(file);
                    if (filter is not null && !filter.IsMatch(relative)) continue;

                    var text = buffers.TryGetValue(file, out var open)
                        ? open
                        : await TryReadDiskAsync(file, ct).ConfigureAwait(false);

                    if (text is null) continue;

                    foreach (var hit in engine.FindInText(text, query, options, file))
                    {
                        if (count++ >= MaxHits)
                        {
                            result.Append($"(stopped at {MaxHits} matches; refine the query)\n");
                            return new AiToolResult(result.ToString());
                        }

                        result.Append($"{relative}:{hit.Line}: {Clip(hit.LineText.Trim())}\n");
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                throw new AiToolException("The regular expression took too long; simplify it.");
            }

            return new AiToolResult(count == 0 ? "No matches." : result.ToString());
        });

    // get_diagnostics

    private AiTool GetDiagnostics() => new(
        "get_diagnostics",
        "Get the compiler errors and warnings the IDE currently shows, for one file or, when "
      + "no path is given, the whole solution. Call this after proposing a change is accepted, or to "
      + "find what is broken.",
        AiSchema.Object([("path", AiSchema.Property("string", "A file; omit for the whole solution."))]),
        async (input, ct) =>
        {
            string? full = input["path"] is { } p && p.GetValue<string>().Length > 0
                ? Sandbox.Resolve(p.GetValue<string>())
                : null;

            var all = await Host.GetDiagnosticsAsync(full, ct).ConfigureAwait(false);

            // The host's list is not trusted to stay in the folder.
            var inside = all
                .Where(d => d.FilePath is null || IsInsideRoot(d.FilePath))
                .Take(MaxDiagnostics)
                .ToList();

            if (inside.Count == 0) return new AiToolResult("No diagnostics.");

            var text = string.Join('\n', inside.Select(d =>
                d.FilePath is null
                    ? $"{d.Severity} {d.Id}: {d.Message}"
                    : $"{Sandbox.Relative(d.FilePath)}({d.Line},{d.Column}): {d.Severity} {d.Id}: {d.Message}"));

            if (all.Count > inside.Count) text += $"\n(showing {inside.Count} of {all.Count})";

            return new AiToolResult(text);
        });

    // find_symbol

    private AiTool FindSymbol() => new(
        "find_symbol",
        "Find where a type, method, property or other declaration is defined, by name, using the "
      + "language service (it understands the code, unlike search_text). Returns 'path:line:column' "
      + "for each declaration; then read_file around it.",
        AiSchema.Object([("name", AiSchema.Property("string", "The symbol's name, or part of it."))], "name"),
        async (input, ct) =>
        {
            var name = input["name"]!.GetValue<string>().Trim();
            if (name.Length == 0) throw new AiToolException("The name is empty.");

            var symbols = await Host.FindSymbolsAsync(name, ct).ConfigureAwait(false);

            var inside = symbols
                .Where(s => IsInsideRoot(s.FilePath))
                .Take(MaxHits)
                .ToList();

            if (inside.Count == 0) return new AiToolResult($"No symbol named '{name}' was found.");

            return new AiToolResult(string.Join('\n', inside.Select(s =>
                $"{s.Kind} {s.Name}  {Sandbox.Relative(s.FilePath)}:{s.Line}:{s.Column}")));
        });

    // propose_edits

    private AiTool ProposeEdits() => new(
        "propose_edits",
        "Propose changes to files. Nothing is written: the user sees a diff for each file and "
      + "accepts or rejects it. Each edit names a path and either replaces one exact piece of text "
      + "(old_text must occur exactly once, so include enough surrounding lines; new_text replaces it) "
      + "or gives the complete new content of the file (content; to create a file, or to replace one "
      + "you have read). Several edits to one file are applied in order. The file must not have "
      + "changed since you read it.",
        new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["summary"] = AiSchema.Property("string", "One sentence saying what the change does."),
                ["edits"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "The edits to propose.",
                    ["items"] = AiSchema.Object(
                        [("path", AiSchema.Property("string", "Path relative to the solution folder.")),
                         ("old_text", AiSchema.Property("string", "Exact text to replace; occurs once in the file.")),
                         ("new_text", AiSchema.Property("string", "What replaces old_text.")),
                         ("content", AiSchema.Property("string", "The whole new content of the file."))],
                        "path")
                }
            },
            ["required"] = new JsonArray("edits")
        },
        ProposeAsync);

    private async Task<AiToolResult> ProposeAsync(JsonObject input, CancellationToken ct)
    {
        var edits = input["edits"]!.AsArray();

        if (edits.Count == 0) throw new AiToolException("There are no edits.");

        // The text each file will have, built up edit by edit.
        var working = new Dictionary<string, (string Original, string Text, bool IsNew)>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        var order = new List<string>();

        foreach (var node in edits)
        {
            ct.ThrowIfCancellationRequested();

            var edit = node!.AsObject();
            var relativePath = edit["path"]!.GetValue<string>();
            var full = Sandbox.ResolveForWrite(relativePath);
            var content = edit["content"]?.GetValue<string>();
            var oldText = edit["old_text"]?.GetValue<string>();
            var newText = edit["new_text"]?.GetValue<string>();

            if (content is null && (oldText is null || newText is null))
                throw new AiToolException($"{relativePath}: give old_text with new_text, or content.");

            if (content is not null && (oldText is not null || newText is not null))
                throw new AiToolException($"{relativePath}: use either content or old_text/new_text, not both.");

            if (!working.TryGetValue(full, out var state))
            {
                if (working.Count >= MaxFilesPerProposal)
                    throw new AiToolException($"At most {MaxFilesPerProposal} files per proposal.");

                var current = await ReadCurrentAsync(full, ct).ConfigureAwait(false);

                if (current is not null)
                {
                    string? read;
                    lock (_gate) _seen.TryGetValue(full, out read);

                    if (read is null && content is not null)
                        throw new AiToolException(
                            $"{relativePath}: read the file before replacing it.");

                    if (read is not null && read != current)
                        throw new AiToolException(
                            $"{relativePath} changed since you read it. Read it again and propose the change again.");
                }

                state = (current ?? "", current ?? "", current is null);
                order.Add(full);
            }

            if (content is not null)
            {
                if (Encoding.UTF8.GetByteCount(content) > MaxFileBytes)
                    throw new AiToolException($"{relativePath}: the content is too large.");

                // The file keeps its own line endings: a whole-file rewrite
                // must not turn a CRLF file into an LF one and show every line
                // as changed. A byte-order mark is not in the text at all
                // (reading strips it) and is the document's to keep on save.
                if (!state.IsNew)
                {
                    if (state.Text.Contains("\r\n") && !content.Contains('\r'))
                        content = content.Replace("\n", "\r\n");

                }

                state = state with { Text = content };
            }
            else
            {
                if (state.IsNew && state.Text.Length == 0)
                    throw new AiToolException($"{relativePath} does not exist; give its content to create it.");

                if (oldText!.Length == 0) throw new AiToolException($"{relativePath}: old_text is empty.");

                // A file written with Windows line endings is matched by text
                // written with Unix ones, and edited in its own convention.
                if (state.Text.Contains("\r\n") && !oldText.Contains('\r'))
                {
                    oldText = oldText.Replace("\n", "\r\n");
                    newText = newText!.Replace("\n", "\r\n");
                }

                var occurrences = CountOccurrences(state.Text, oldText);

                if (occurrences == 0)
                    throw new AiToolException(
                        $"{relativePath}: old_text was not found. Read the file again and copy the text exactly.");

                if (occurrences > 1)
                    throw new AiToolException(
                        $"{relativePath}: old_text occurs {occurrences} times; include more surrounding text so it occurs once.");

                var at = state.Text.IndexOf(oldText, StringComparison.Ordinal);
                state = state with { Text = string.Concat(state.Text.AsSpan(0, at), newText, state.Text.AsSpan(at + oldText.Length)) };
            }

            working[full] = state;
        }

        var files = new List<AiFileChange>();

        foreach (var full in order)
        {
            var (original, text, isNew) = working[full];

            if (text == original && !isNew) continue;

            files.Add(new AiFileChange(full, Sandbox.Relative(full), original, text) { IsNewFile = isNew });
        }

        if (files.Count == 0) throw new AiToolException("The edits change nothing.");

        var summary = input["summary"]?.GetValue<string>() ?? "";
        var set = new AiChangeSet(summary, files);

        ChangeProposed?.Invoke(this, set);

        return new AiToolResult(
            $"Proposed changes to {files.Count} file(s): {string.Join(", ", files.Select(f => f.RelativePath))}. "
          + "Nothing has been written; the user will review and accept or reject each file.",
            Proposal: set);
    }

    // helpers

    private bool IsInsideRoot(string path)
    {
        try
        {
            _ = Sandbox.Resolve(path);
            return true;
        }
        catch (AiToolException)
        {
            return false;
        }
    }

    /// <summary>The open editors' text, keyed by canonical path so every lookup agrees on the spelling.</summary>
    private async Task<Dictionary<string, string>> BuffersAsync(CancellationToken ct)
    {
        var open = await Host.SnapshotOpenBuffersAsync(ct).ConfigureAwait(false);
        var buffers = new Dictionary<string, string>(PathIdentity.Comparer);

        foreach (var (path, text) in open)
        {
            try
            {
                buffers[AiWorkspaceSandbox.RealPath(Path.GetFullPath(path))] = text;
            }
            catch (AiToolException)
            {
                // A buffer whose path cannot be resolved is not one a tool names.
            }
        }

        return buffers;
    }

    /// <summary>The text of a file as the user sees it: the open editor, else the disk; null if absent.</summary>
    private async Task<string?> ReadCurrentAsync(string fullPath, CancellationToken ct)
    {
        var buffers = await BuffersAsync(ct).ConfigureAwait(false);

        if (buffers.TryGetValue(fullPath, out var open))
        {
            if (Encoding.UTF8.GetByteCount(open) > MaxFileBytes)
                throw new AiToolException($"'{Sandbox.Relative(fullPath)}' is larger than {MaxFileBytes / 1024 / 1024} MB.");

            return open;
        }

        if (Directory.Exists(fullPath)) throw new AiToolException($"'{Sandbox.Relative(fullPath)}' is a folder.");

        if (!File.Exists(fullPath)) return null;

        return await TryReadDiskAsync(fullPath, ct, throwIfUnreadable: true).ConfigureAwait(false);
    }

    private static async Task<string?> TryReadDiskAsync(string path, CancellationToken ct, bool throwIfUnreadable = false)
    {
        try
        {
            var info = new FileInfo(path);

            if (info.Length > MaxFileBytes)
                return throwIfUnreadable ? throw new AiToolException($"'{info.Name}' is larger than {MaxFileBytes / 1024 / 1024} MB.") : null;

            var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

            // Binary content is of no use to read and unreadable to show.
            if (text.Contains('\0'))
                return throwIfUnreadable ? throw new AiToolException($"'{info.Name}' is a binary file.") : null;

            return text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return throwIfUnreadable ? throw new AiToolException($"'{Path.GetFileName(path)}' cannot be read: {ex.Message}") : null;
        }
    }

    /// <summary>Files under a folder, skipping build output, symbolic links and secrets.</summary>
    private IEnumerable<string> EnumerateFiles(string directory)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };

        var pending = new Stack<string>();
        pending.Push(directory);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            foreach (var entry in Directory.EnumerateFileSystemEntries(current, "*", options))
            {
                var name = Path.GetFileName(entry);

                if (Directory.Exists(entry))
                {
                    if (!AiWorkspaceSandbox.ExcludedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase)
                        && !AiWorkspaceSandbox.IsSecret(entry))
                        pending.Push(entry);

                    continue;
                }

                if (!AiWorkspaceSandbox.IsSecret(entry)) yield return entry;
            }
        }
    }

    private static Regex GlobToRegex(string glob)
    {
        var text = new StringBuilder("^");

        for (var i = 0; i < glob.Length; i++)
        {
            switch (glob[i])
            {
                case '*' when i + 1 < glob.Length && glob[i + 1] == '*':
                    // "**/" also matches no folder at all.
                    if (i + 2 < glob.Length && glob[i + 2] == '/')
                    {
                        text.Append("(?:.*/)?");
                        i += 2;
                    }
                    else
                    {
                        text.Append(".*");
                        i++;
                    }

                    break;
                case '*': text.Append("[^/]*"); break;
                case '?': text.Append("[^/]"); break;
                case '\\': text.Append('/'); break;
                default: text.Append(Regex.Escape(glob[i].ToString())); break;
            }
        }

        text.Append('$');

        return new Regex(text.ToString(),
            OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : RegexOptions.None,
            TimeSpan.FromSeconds(2));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var at = 0;

        while ((at = text.IndexOf(value, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += value.Length;
        }

        return count;
    }

    /// <summary>An integer argument; the model may write 5.0 for 5.</summary>
    private static int? Integer(JsonObject input, string name) =>
        AiSchema.TryNumber(input[name]) is { } number
            ? (int)Math.Clamp(number, int.MinValue, int.MaxValue)
            : null;

    private static string Clip(string line) => AiText.Truncate(line, 240, "…");
}
