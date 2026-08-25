namespace Basalt.Core.Settings;

/// <summary>
/// The solutions and files opened lately.
///
/// Kept with the settings rather than in a file of their own: they are part
/// of how the IDE is set up for this person, and one file is one thing to
/// find, back up and delete.
/// </summary>
public sealed class RecentSettings
{
    /// <summary>
    /// How many are remembered.
    ///
    /// Ten is enough to reach yesterday's work and short enough to read
    /// without scanning.
    /// </summary>
    public const int Limit = 10;

    public List<string> Solutions { get; set; } = [];

    public List<string> Files { get; set; } = [];

    /// <summary>Puts a solution at the top of the list.</summary>
    public void RememberSolution(string path) => Remember(Solutions, path);

    /// <summary>Puts a file at the top of the list.</summary>
    public void RememberFile(string path) => Remember(Files, path);

    /// <summary>
    /// The solutions that are still there.
    ///
    /// Checked when read rather than when written: a file is far more often
    /// moved while the IDE is closed than while it is open.
    /// </summary>
    public IReadOnlyList<string> ExistingSolutions() => Existing(Solutions);

    public IReadOnlyList<string> ExistingFiles() => Existing(Files);

    /// <summary>Takes out what is no longer on disk.</summary>
    public bool Prune()
    {
        var before = Solutions.Count + Files.Count;

        Solutions.RemoveAll(p => !File.Exists(p));
        Files.RemoveAll(p => !File.Exists(p));

        return Solutions.Count + Files.Count != before;
    }

    private static void Remember(List<string> list, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var full = Full(path);

        // Opened again, so it moves to the top rather than appearing twice.
        list.RemoveAll(p => string.Equals(Full(p), full, Comparison));

        list.Insert(0, full);

        if (list.Count > Limit) list.RemoveRange(Limit, list.Count - Limit);
    }

    private static IReadOnlyList<string> Existing(List<string> list) =>
        [.. list.Where(File.Exists)];

    private static string Full(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                      or PathTooLongException)
        {
            // Not a path this machine can make sense of; kept as written so
            // the entry can still be shown and removed.
            return path;
        }
    }

    /// <summary>
    /// How two paths are told apart.
    ///
    /// Case matters on Linux and not on macOS or Windows, and getting this
    /// wrong shows the same solution twice.
    /// </summary>
    private static StringComparison Comparison =>
        OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
}
