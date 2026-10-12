using System.Diagnostics;
using Basalt.Core.Localization;
using Basalt.Core.Services;

namespace Basalt.Workspace;

/// <summary>
/// Git integration implemented by invoking the "git" executable.
///
/// Compared to a native library such as libgit2, this choice avoids
/// platform-specific binaries and behaves identically on Windows, macOS and
/// Linux, at the price of requiring git to be installed.
/// </summary>
public sealed class GitSourceControlService : ISourceControlService
{
    /// <summary>Field separator: a control character never present in a commit's metadata.</summary>
    private const char FieldSeparator = '\u001f';

    private readonly string _repositoryPath;

    public GitSourceControlService(string repositoryPath) => _repositoryPath = repositoryPath;

    /// <summary>
    /// Starts a repository in a folder that has none, with its first commit.
    ///
    /// The first commit matters: a repository with no commits has no HEAD,
    /// and half of what a git client shows — a diff, a log, a branch to
    /// compare against — has nothing to work from until there is one.
    /// </summary>
    public static async Task<bool> InitializeAsync(
        string folder, string? firstCommitMessage = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(folder)) return false;

        var service = new GitSourceControlService(folder);

        // Already one: starting another inside it would make a repository
        // nested in a repository, which surprises everyone who meets it.
        if (await service.IsRepositoryAsync(folder, ct).ConfigureAwait(false)) return false;

        // "-b main" so the branch is not called whatever this machine's git
        // happens to default to.
        var started = await service.RunAsync(["init", "-b", "main"], folder, ct).ConfigureAwait(false);

        if (started.ExitCode != 0) return false;

        await WriteIgnoreFileAsync(folder, ct).ConfigureAwait(false);

        var staged = await service.RunAsync(["add", "."], folder, ct).ConfigureAwait(false);

        if (staged.ExitCode != 0) return false;

        var message = string.IsNullOrWhiteSpace(firstCommitMessage)
            ? "First commit"
            : firstCommitMessage!;

        var committed = await service.RunAsync(
            ["commit", "-m", message], folder, ct).ConfigureAwait(false);

        return committed.ExitCode == 0;
    }

    /// <summary>
    /// Writes a .gitignore for a .NET project, unless there is one already.
    ///
    /// Without it the first commit carries bin and obj, which is megabytes of
    /// build output and a merge conflict on every pull.
    /// </summary>
    private static async Task WriteIgnoreFileAsync(string folder, CancellationToken ct)
    {
        var path = Path.Combine(folder, ".gitignore");

        // Someone else's: theirs wins.
        if (File.Exists(path)) return;

        await File.WriteAllTextAsync(path, """
            # Build output
            bin/
            obj/

            # The IDE's own files
            .vs/
            .idea/
            *.user

            # What a publish and a package restore leave behind
            publish/
            *.nupkg

            # macOS
            .DS_Store
            """, ct).ConfigureAwait(false);
    }

    public async Task<bool> IsRepositoryAsync(string path, CancellationToken ct = default)
    {
        var result = await RunAsync(["rev-parse", "--is-inside-work-tree"], path, ct).ConfigureAwait(false);
        return result.ExitCode == 0 && result.Output.Trim() == "true";
    }

    /// <summary>
    /// Current branch, or null if HEAD is detached.
    ///
    /// "branch --show-current" is used instead of "rev-parse HEAD" because the
    /// latter fails on a freshly initialized repository, where the branch exists
    /// but has no commits yet.
    /// </summary>
    public async Task<string?> GetCurrentBranchAsync(CancellationToken ct = default)
    {
        var result = await RunAsync(["branch", "--show-current"], ct: ct).ConfigureAwait(false);
        if (result.ExitCode != 0) return null;

        var branch = result.Output.Trim();
        return branch.Length == 0 ? null : branch;
    }

    public async Task<IReadOnlyList<FileChange>> GetStatusAsync(CancellationToken ct = default)
    {
        // The porcelain v1 format is stable and designed to be parsed.
        var result = await RunAsync(["status", "--porcelain=v1", "-z"], ct: ct).ConfigureAwait(false);
        if (result.ExitCode != 0) return [];

        var changes = new List<FileChange>();

        // With -z the records are NUL-separated: no ambiguity for file names
        // that contain spaces or quotes.
        var records = result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < records.Length; index++)
        {
            var entry = records[index];
            if (entry.Length < 4) continue;

            var indexStatus = entry[0];
            var workTreeStatus = entry[1];
            var path = entry[3..];

            if (indexStatus != ' ' && indexStatus != '?')
                changes.Add(new FileChange(path, MapStatus(indexStatus), Staged: true));

            if (workTreeStatus != ' ')
                changes.Add(new FileChange(path, MapStatus(workTreeStatus), Staged: false));

            // A rename or a copy is followed by its original path as a record
            // of its own. That is not a change, and read as a status line it
            // turned into a bogus row for a file named after the tail of the
            // original path.
            // An unstaged rename (a new name added with "git add -N") reports
            // it in the work tree column, with the same trailing record.
            if (indexStatus is 'R' or 'C' || workTreeStatus is 'R' or 'C') index++;
        }

        return changes;
    }

    public async Task<string> GetDiffAsync(string filePath, bool staged, CancellationToken ct = default)
    {
        var rootPath = RootPathspec(filePath);

        string[] arguments = staged
            ? ["diff", "--cached", "--", rootPath]
            : ["diff", "--", rootPath];

        return (await RunAsync(arguments, ct: ct).ConfigureAwait(false)).Output;
    }

    /// <summary>
    /// A path as git reports it: relative to the top of the repository.
    ///
    /// The solution is not always at that top. In a solution inside a
    /// subfolder of its repository, a plain path is read relative to the
    /// folder git runs in, so "add" and "diff" were given a path that does
    /// not exist there and did nothing, with no error to show for it. The
    /// ":(top,literal)" magic anchors the path at the top, and "literal"
    /// keeps a name such as "Sheet[1].vb" from being read as a pattern.
    /// </summary>
    /// <remarks>
    /// A full path, such as the editor's file for its change margin, is left
    /// unanchored: git reads an absolute path inside the work tree as it is,
    /// and anchored at the top it named a file that does not exist.
    /// </remarks>
    private static string RootPathspec(string path) =>
        Path.IsPathRooted(path) ? ":(literal)" + path : ":(top,literal)" + path;

    /// <summary>
    /// What a commit changed, or what lies between two of them.
    ///
    /// With one commit: that commit against the one before it, which is what
    /// "what did this change?" means. With two: everything between them.
    /// </summary>
    public async Task<string> GetCommitDiffAsync(
        string commit, string? against = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(commit)) return "";

        string[] arguments = against is { Length: > 0 }
            ? ["diff", against, commit]

            // A commit and its parent. "show" rather than "diff HEAD~1"
            // because the first commit in a repository has no parent, and
            // show handles that on its own.
            : ["show", "--format=", commit];

        var result = await RunAsync(arguments, ct: ct).ConfigureAwait(false);

        return result.ExitCode == 0 ? result.Output : "";
    }

    /// <summary>The files a commit touched.</summary>
    public async Task<IReadOnlyList<string>> GetCommitFilesAsync(
        string commit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(commit)) return [];

        var result = await RunAsync(
            ["show", "--name-only", "--format=", commit], ct: ct).ConfigureAwait(false);

        if (result.ExitCode != 0) return [];

        return [.. result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)];
    }

    /// <summary>
    /// Stages files. A refusal is thrown, as a commit's is: the caller reports
    /// it in the output, where a silent refusal left the list unchanged with
    /// no reason shown.
    /// </summary>
    public Task StageAsync(IReadOnlyList<string> paths, CancellationToken ct = default) =>
        RunOrThrowAsync(["add", "--", .. paths.Select(RootPathspec)], ct);

    /// <summary>
    /// Takes files out of the index, leaving the working files as they are.
    /// </summary>
    public async Task UnstageAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        var rootPaths = paths.Select(RootPathspec).ToArray();

        // "restore --staged" copies HEAD back into the index, so it needs a
        // HEAD. A repository with no commit yet has none: git refused, the
        // refusal was discarded, and the file stayed staged. There, removing
        // the index entry is all that unstaging means.
        if (await HasCommitAsync(ct).ConfigureAwait(false))
            await RunOrThrowAsync(["restore", "--staged", "--", .. rootPaths], ct).ConfigureAwait(false);
        else
            await RunOrThrowAsync(["rm", "--cached", "--quiet", "--", .. rootPaths], ct).ConfigureAwait(false);
    }

    private async Task<bool> HasCommitAsync(CancellationToken ct)
    {
        var result = await RunAsync(["rev-parse", "--verify", "--quiet", "HEAD"], ct: ct)
            .ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    public async Task CommitAsync(
        string message, bool amend = false, CancellationToken ct = default)
    {
        string[] arguments = amend
            ? ["commit", "--amend", "-m", message]
            : ["commit", "-m", message];

        var result = await RunAsync(arguments, ct: ct).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(Describe(result.Error, result.Output));
    }

    /// <summary>
    /// The message of the last commit.
    ///
    /// Null in a repository with no commits yet, where there is nothing to
    /// amend.
    /// </summary>
    public async Task<string?> GetLastCommitMessageAsync(CancellationToken ct = default)
    {
        var result = await RunAsync(["log", "-1", "--pretty=format:%B"], ct: ct)
            .ConfigureAwait(false);

        if (result.ExitCode != 0) return null;

        var message = result.Output.TrimEnd('\n', '\r');
        return message.Length == 0 ? null : message;
    }

    /// <summary>
    /// The most recent commits, with what they descend from.
    ///
    /// "--all" is used so that commits on other branches appear too: a graph
    /// showing only the current branch would hide exactly the structure it
    /// exists to show. Parents and ref names come along in the same call,
    /// since asking per commit would mean one git invocation each.
    /// </summary>
    public async Task<IReadOnlyList<CommitInfo>> GetLogAsync(int maxCount, CancellationToken ct = default)
    {
        var format =
            $"--pretty=format:%H{FieldSeparator}%an{FieldSeparator}%aI{FieldSeparator}" +
            $"%P{FieldSeparator}%D{FieldSeparator}%s";

        var result = await RunAsync(
            ["log", "--all", "--date-order", $"-{maxCount}", format], ct: ct).ConfigureAwait(false);

        if (result.ExitCode != 0) return [];

        var commits = new List<CommitInfo>();

        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(FieldSeparator);
            if (parts.Length < 6) continue;

            if (!DateTimeOffset.TryParse(parts[2], out var when)) continue;

            commits.Add(new CommitInfo(parts[0], parts[1], parts[5], when)
            {
                Parents = parts[3].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                Refs = ParseRefs(parts[4])
            });
        }

        return commits;
    }

    /// <summary>
    /// Turns the "%D" decoration into plain names.
    ///
    /// Git writes it as "HEAD -> main, origin/main, tag: v1"; the arrow and the
    /// "tag:" prefix are presentation, not part of any name.
    /// </summary>
    private static string[] ParseRefs(string decoration)
    {
        if (decoration.Length == 0) return [];

        return decoration
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name =>
            {
                var arrow = name.IndexOf("-> ", StringComparison.Ordinal);
                if (arrow >= 0) name = name[(arrow + 3)..];

                return name.StartsWith("tag: ", StringComparison.Ordinal) ? name[5..] : name;
            })
            .Where(name => name.Length > 0)
            .ToArray();
    }

    /// <summary>
    /// Every branch, local and remote, with how far each has diverged.
    ///
    /// for-each-ref is used rather than "branch -vv" because its output is a
    /// format the caller chooses, instead of a display format that changes
    /// with git's configuration.
    /// </summary>
    public async Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(CancellationToken ct = default)
    {
        var format =
            $"--format=%(refname:short){FieldSeparator}%(HEAD){FieldSeparator}" +
            $"%(upstream:short){FieldSeparator}%(upstream:track)";

        var result = await RunAsync(
            ["for-each-ref", format, "refs/heads", "refs/remotes"], ct: ct).ConfigureAwait(false);

        if (result.ExitCode != 0) return [];

        var remotes = await GetRemoteNamesAsync(ct).ConfigureAwait(false);
        var branches = new List<BranchInfo>();

        foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(FieldSeparator);
            if (parts.Length < 4) continue;

            var name = parts[0];

            // The symbolic ref itself is not a branch anyone can check out.
            if (name.EndsWith("/HEAD", StringComparison.Ordinal)) continue;

            var slash = name.IndexOf('/');
            var isRemote = slash > 0 && remotes.Contains(name[..slash]);

            var (ahead, behind) = ParseTracking(parts[3]);

            branches.Add(new BranchInfo(
                name,
                IsCurrent: parts[1].Trim() == "*",
                IsRemote: isRemote,
                Upstream: parts[2].Length == 0 ? null : parts[2],
                Ahead: ahead,
                Behind: behind));
        }

        return branches;
    }

    /// <summary>
    /// The configured remote names, such as "origin".
    ///
    /// Needed to tell "origin/main" from a local branch called "feature/login":
    /// both contain a slash, and only the first segment distinguishes them.
    /// </summary>
    private async Task<HashSet<string>> GetRemoteNamesAsync(CancellationToken ct)
    {
        var result = await RunAsync(["remote"], ct: ct).ConfigureAwait(false);

        return result.ExitCode != 0
            ? []
            : [.. result.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    /// <summary>Reads git's "[ahead 2, behind 1]" tracking summary.</summary>
    private static (int Ahead, int Behind) ParseTracking(string track)
    {
        var ahead = 0;
        var behind = 0;

        foreach (var part in track.Trim('[', ']', ' ').Split(',', StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("ahead ", StringComparison.Ordinal))
                int.TryParse(part[6..], out ahead);
            else if (part.StartsWith("behind ", StringComparison.Ordinal))
                int.TryParse(part[7..], out behind);
        }

        return (ahead, behind);
    }

    public Task CreateBranchAsync(string name, bool checkout = true, CancellationToken ct = default) =>
        checkout
            ? RunOrThrowAsync(["checkout", "-b", name], ct)
            : RunOrThrowAsync(["branch", name], ct);

    public Task CheckoutAsync(string name, CancellationToken ct = default) =>
        RunOrThrowAsync(["checkout", name], ct);

    public Task DeleteBranchAsync(string name, bool force = false, CancellationToken ct = default) =>
        RunOrThrowAsync(["branch", force ? "-D" : "-d", name], ct);

    public Task MergeAsync(string branch, CancellationToken ct = default) =>
        RunOrThrowAsync(["merge", branch], ct);

    /// <summary>
    /// Throws away local changes to the given files.
    ///
    /// Both the index and the working tree are restored, so this undoes a
    /// staged change as well as an unstaged one.
    /// </summary>
    /// <summary>
    /// Undoes changes to a file, in the working tree and the index.
    ///
    /// A file nobody has ever staged is not in git's index or in any commit,
    /// so "restore" - which replays what is already there - has nothing to
    /// replay it from and simply refuses the path. The menu offers Discard on
    /// such a file all the same, and the only sensible meaning left for it is
    /// what Visual Studio's own Delete does to an untracked file: remove it.
    /// </summary>
    public async Task DiscardChangesAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        if (paths.Count == 0) return;

        var untracked = new List<string>();
        var tracked = new List<string>();

        foreach (var path in paths)
        {
            var status = await RunAsync(["status", "--porcelain=v1", "--", RootPathspec(path)], ct: ct)
                .ConfigureAwait(false);

            (status.Output.TrimStart().StartsWith("??", StringComparison.Ordinal)
                ? untracked
                : tracked).Add(path);
        }

        if (untracked.Count > 0)
            await RunOrThrowAsync(["clean", "-f", "--", .. untracked.Select(RootPathspec)], ct)
                .ConfigureAwait(false);

        if (tracked.Count > 0)
            await RunOrThrowAsync(
                ["restore", "--staged", "--worktree", "--", .. tracked.Select(RootPathspec)], ct)
                .ConfigureAwait(false);
    }

    public async Task<UpstreamState> GetUpstreamStateAsync(CancellationToken ct = default)
    {
        var upstream = await RunAsync(
            ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"],
            ct: ct).ConfigureAwait(false);

        // A branch that was never pushed has no upstream, and git says so by
        // failing. That is not an error to report: it is the ordinary state
        // of a branch made a minute ago.
        if (upstream.ExitCode != 0) return new UpstreamState(null, 0, 0);

        var name = upstream.Output.Trim();

        var counts = await RunAsync(
            ["rev-list", "--left-right", "--count", $"{name}...HEAD"],
            ct: ct).ConfigureAwait(false);

        if (counts.ExitCode != 0) return new UpstreamState(name, 0, 0);

        // Left is the upstream side and right is ours: --left-right counts
        // each side of the three-dot range, so behind comes first.
        var parts = counts.Output.Split(
            [' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2
            || !int.TryParse(parts[0], out var behind)
            || !int.TryParse(parts[1], out var ahead))
            return new UpstreamState(name, 0, 0);

        return new UpstreamState(name, ahead, behind);
    }

    public Task<string> FetchAsync(CancellationToken ct = default) =>
        RunForMessageAsync(["fetch", "--all", "--prune"], ct);

    public Task<string> PullAsync(CancellationToken ct = default) =>
        RunForMessageAsync(["pull", "--ff-only"], ct);

    public Task<string> PushAsync(CancellationToken ct = default) =>
        RunForMessageAsync(["push"], ct);

    private async Task RunOrThrowAsync(string[] arguments, CancellationToken ct)
    {
        var result = await RunAsync(arguments, ct: ct).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(Describe(result.Error, result.Output));
    }

    /// <summary>
    /// Runs a command that talks to a remote, returning what it reported.
    ///
    /// These print their progress on standard error even when they succeed, so
    /// the output is handed back for display rather than treated as a fault.
    /// </summary>
    private async Task<string> RunForMessageAsync(string[] arguments, CancellationToken ct)
    {
        var result = await RunAsync(arguments, ct: ct).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(Describe(result.Error, result.Output));

        var message = (result.Error + result.Output).Trim();
        return message.Length == 0 ? "Done." : message;
    }

    private static string Describe(string error, string output)
    {
        var message = error.Trim();
        if (message.Length == 0) message = output.Trim();

        return message.Length == 0 ? "The git command failed." : message;
    }

    private static FileChangeKind MapStatus(char status) => status switch
    {
        'A' => FileChangeKind.Added,
        'M' => FileChangeKind.Modified,
        'D' => FileChangeKind.Deleted,
        'R' => FileChangeKind.Renamed,
        'U' => FileChangeKind.Conflicted,
        '?' => FileChangeKind.Untracked,
        _ => FileChangeKind.Modified
    };

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string[] arguments, string? workingDirectory = null, CancellationToken ct = default)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory ?? _repositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(Localizer.Get(StringKeys.GitCannotStart));

        // Both streams are read in parallel: waiting for exit before draining
        // them can deadlock the process on large output.
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return (process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
    }
}
