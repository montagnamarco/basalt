namespace Basalt.Workspace.Ai;

/// <summary>Where a proposed change to one file stands.</summary>
public enum AiChangeState { Pending, Applying, Accepted, Rejected, Failed }

/// <summary>
/// One file an assistant proposes to change.
///
/// The text it would replace is kept: accepting checks the file still holds it,
/// so a proposal made against what the assistant read cannot overwrite what
/// the user has typed since.
/// </summary>
public sealed class AiFileChange(string filePath, string relativePath, string originalText, string newText)
{
    public string FilePath { get; } = filePath;

    /// <summary>The path as the assistant and the user know it: relative to the solution folder.</summary>
    public string RelativePath { get; } = relativePath;

    public string OriginalText { get; } = originalText;

    public string NewText { get; } = newText;

    /// <summary>A file that does not exist yet.</summary>
    public bool IsNewFile { get; init; }

    /// <summary>
    /// Pending, then either Rejected or Applying; Applying ends as Accepted or
    /// Failed. Nothing else is allowed, so a second click on Accept (or Accept
    /// all after Accept) cannot apply the same file twice.
    /// </summary>
    public AiChangeState State { get; private set; } = AiChangeState.Pending;

    /// <summary>Why applying failed, when it did.</summary>
    public string? Problem { get; private set; }

    public string UnifiedDiff => TextDiff.Unified(RelativePath, OriginalText, NewText);

    internal bool Move(AiChangeState from, AiChangeState to, string? problem = null)
    {
        lock (this)
        {
            if (State != from) return false;

            State = to;
            Problem = problem;
            return true;
        }
    }
}

/// <summary>
/// Changes an assistant proposed, waiting for the user's decision.
///
/// Creating one writes nothing: not to disk and not to an editor buffer. That
/// is the whole point of it.
/// </summary>
public sealed class AiChangeSet(string summary, IReadOnlyList<AiFileChange> files)
{
    public string Summary { get; } = summary;

    public IReadOnlyList<AiFileChange> Files { get; } = files;

    /// <summary>Raised when a file's state changes.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<AiFileChange> Pending => [.. Files.Where(f => f.State == AiChangeState.Pending)];

    public bool MarkRejected(AiFileChange file) => Transition(file, AiChangeState.Pending, AiChangeState.Rejected);

    /// <summary>
    /// Claims a pending file for applying. Only one caller wins; the others
    /// are told no and must leave the file alone.
    /// </summary>
    public bool TryBeginApply(AiFileChange file) => Transition(file, AiChangeState.Pending, AiChangeState.Applying);

    public bool MarkAccepted(AiFileChange file) => Transition(file, AiChangeState.Applying, AiChangeState.Accepted);

    public bool MarkFailed(AiFileChange file, string problem) =>
        Transition(file, AiChangeState.Applying, AiChangeState.Failed, problem);

    private bool Transition(AiFileChange file, AiChangeState from, AiChangeState to, string? problem = null)
    {
        if (!file.Move(from, to, problem)) return false;

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
