namespace Basalt.Workspace.Refactoring;

/// <summary>One file a refactoring would change.</summary>
public sealed record FileChangePreview(
    string FilePath,
    string OriginalText,
    string NewText)
{
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// The lines that differ, as line number and new text.
    ///
    /// Enough to show what a refactoring will do without asking the user to
    /// read two whole files side by side.
    /// </summary>
    public IReadOnlyList<(int Line, string Before, string After)> ChangedLines
    {
        get
        {
            var before = OriginalText.Replace("\r\n", "\n").Split('\n');
            var after = NewText.Replace("\r\n", "\n").Split('\n');

            var changes = new List<(int, string, string)>();

            for (var i = 0; i < Math.Max(before.Length, after.Length); i++)
            {
                var oldLine = i < before.Length ? before[i] : "";
                var newLine = i < after.Length ? after[i] : "";

                if (oldLine != newLine) changes.Add((i + 1, oldLine, newLine));
            }

            return changes;
        }
    }
}

/// <summary>
/// What a refactoring would do, before it is done.
///
/// Every refactoring produces one of these rather than editing straight away:
/// a change across several files is one the user should see before it happens,
/// because undoing it means undoing each file.
/// </summary>
public sealed record RefactoringPreview(
    string Title,
    IReadOnlyList<FileChangePreview> Changes)
{
    /// <summary>Why it cannot be done, when it cannot.</summary>
    public string? Problem { get; init; }

    public bool CanApply => Problem is null && Changes.Count > 0;

    /// <summary>Nothing to do, with the reason.</summary>
    public static RefactoringPreview Refused(string title, string problem) =>
        new(title, []) { Problem = problem };
}
