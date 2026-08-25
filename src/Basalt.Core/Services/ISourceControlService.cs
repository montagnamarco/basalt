namespace Basalt.Core.Services;

public enum FileChangeKind { Added, Modified, Deleted, Renamed, Untracked, Conflicted }

public sealed record FileChange(string Path, FileChangeKind Kind, bool Staged);

/// <summary>
/// One commit.
///
/// <paramref name="Parents"/> is what makes the graph drawable: a commit with
/// two parents is a merge, and a commit no branch points at is on a side path.
/// </summary>
public sealed record CommitInfo(
    string Sha,
    string Author,
    string Message,
    DateTimeOffset When)
{
    public IReadOnlyList<string> Parents { get; init; } = [];

    /// <summary>Branch and tag names pointing at this commit.</summary>
    public IReadOnlyList<string> Refs { get; init; } = [];

    public bool IsMerge => Parents.Count > 1;

    /// <summary>The first line, which is what a log listing shows.</summary>
    public string Subject
    {
        get
        {
            var newline = Message.IndexOf('\n');
            return newline < 0 ? Message : Message[..newline];
        }
    }
}

/// <summary>A branch, and where it stands against the one it tracks.</summary>
public sealed record BranchInfo(
    string Name,
    bool IsCurrent,
    bool IsRemote,
    string? Upstream = null,
    int Ahead = 0,
    int Behind = 0);

/// <summary>Integration with git for the repository that contains the solution.</summary>
public interface ISourceControlService
{
    Task<bool> IsRepositoryAsync(string path, CancellationToken ct = default);
    Task<string?> GetCurrentBranchAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FileChange>> GetStatusAsync(CancellationToken ct = default);
    Task<string> GetDiffAsync(string filePath, bool staged, CancellationToken ct = default);
    Task StageAsync(IReadOnlyList<string> paths, CancellationToken ct = default);
    Task UnstageAsync(IReadOnlyList<string> paths, CancellationToken ct = default);
    /// <summary>
    /// Records a commit.
    ///
    /// <paramref name="amend"/> replaces the previous commit instead of adding
    /// one, which is how a message is corrected or a forgotten file included.
    /// </summary>
    Task CommitAsync(string message, bool amend = false, CancellationToken ct = default);

    /// <summary>The message of the last commit, for amending it.</summary>
    Task<string?> GetLastCommitMessageAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CommitInfo>> GetLogAsync(int maxCount, CancellationToken ct = default);

    Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(CancellationToken ct = default);
    Task CreateBranchAsync(string name, bool checkout = true, CancellationToken ct = default);
    Task CheckoutAsync(string name, CancellationToken ct = default);
    Task DeleteBranchAsync(string name, bool force = false, CancellationToken ct = default);
    Task MergeAsync(string branch, CancellationToken ct = default);

    /// <summary>Undoes changes to a file, in the working tree and the index.</summary>
    Task DiscardChangesAsync(IReadOnlyList<string> paths, CancellationToken ct = default);

    Task<string> FetchAsync(CancellationToken ct = default);
    Task<string> PullAsync(CancellationToken ct = default);
    Task<string> PushAsync(CancellationToken ct = default);
}
