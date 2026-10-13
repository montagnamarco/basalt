namespace Basalt.Core.Settings;

/// <summary>
/// What was on screen when a solution was last closed.
/// </summary>
/// <remarks>
/// Coming back to an IDE that has forgotten everything means finding the same
/// four files again before any work can start. The state is small — which
/// files were open, which one was in front, which folders were unfolded — and
/// restoring it is the difference between resuming and starting over.
///
/// Kept per solution rather than globally: two projects have nothing to do
/// with each other, and one set of open files would be wrong for both.
/// </remarks>
public sealed class SolutionSession
{
    /// <summary>The solution this belongs to.</summary>
    public string SolutionPath { get; set; } = "";

    /// <summary>The files that were open, in the order of their tabs.</summary>
    public List<string> OpenFiles { get; set; } = [];

    /// <summary>Tabs in order, including distinct code and designer views of one file.</summary>
    public List<OpenDocumentSession> OpenTabs { get; set; } = [];

    /// <summary>Which of them was in front.</summary>
    public string? ActiveFile { get; set; }

    /// <summary>Whether the frontmost tab is its designer view.</summary>
    public bool ActiveInDesigner { get; set; }

    /// <summary>Files that were open in the designer rather than as markup.</summary>
    public List<string> InDesigner { get; set; } = [];

    /// <summary>Folders unfolded in the solution explorer.</summary>
    public List<string> ExpandedFolders { get; set; } = [];

    /// <summary>
    /// Whether <see cref="ExpandedFolders"/> was read from the tree. Sessions
    /// saved before the tree's state was followed, or with no tree shown, have
    /// an empty list that means "unknown", not "all collapsed": they open with
    /// the explorer's own defaults, the projects unfolded.
    /// </summary>
    public bool ExpansionRecorded { get; set; }

    /// <summary>The selected solution explorer file or folder.</summary>
    public string? SelectedPath { get; set; }

    /// <summary>The runtime identifiers last chosen in "Make executable".</summary>
    public List<string> PublishRids { get; set; } = [];

    /// <summary>The output folder last chosen there; null means the project's bin/Publish folder.</summary>
    public string? PublishOutputRoot { get; set; }
}

public sealed class OpenDocumentSession
{
    public string Path { get; set; } = "";
    public bool InDesigner { get; set; }
}

/// <summary>
/// The remembered state of every solution that has been opened.
/// </summary>
/// <remarks>
/// A list rather than one entry, so moving between two projects keeps both:
/// remembering only the last one means the other is forgotten every time.
/// </remarks>
public sealed class SessionSettings
{
    /// <summary>How many solutions are remembered before the oldest is dropped.</summary>
    public const int Keep = 20;

    public List<SolutionSession> Solutions { get; set; } = [];

    /// <summary>What was open in the given solution, or nothing.</summary>
    public SolutionSession? For(string solutionPath) =>
        Solutions.FirstOrDefault(s =>
            string.Equals(s.SolutionPath, solutionPath, StringComparison.Ordinal));

    /// <summary>
    /// Records the state of a solution, replacing what was there.
    /// </summary>
    /// <remarks>
    /// Newest first, so the list stays useful when it is trimmed: the
    /// solutions worked on lately are the ones worth remembering.
    /// </remarks>
    public void Remember(SolutionSession session)
    {
        Solutions.RemoveAll(s =>
            string.Equals(s.SolutionPath, session.SolutionPath, StringComparison.Ordinal));

        Solutions.Insert(0, session);

        if (Solutions.Count > Keep) Solutions.RemoveRange(Keep, Solutions.Count - Keep);
    }
}
