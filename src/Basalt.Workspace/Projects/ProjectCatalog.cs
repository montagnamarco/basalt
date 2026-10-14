using System.Text.RegularExpressions;

namespace Basalt.Workspace.Projects;

/// <summary>What an opened solution or project offers to run, read in one pass.</summary>
/// <param name="Projects">The projects, in the order the solution lists them.</param>
/// <param name="Runnable">The projects that produce something to start, a subset of <paramref name="Projects"/>.</param>
/// <param name="Startup">The project Build and Run use, or null when there is none.</param>
public sealed record ProjectCatalogEntries(
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> Runnable,
    string? Startup);

/// <summary>
/// Which projects of an opened solution can run, and which one starts.
///
/// Both answers come from the solution file (see <see cref="SolutionFile"/>),
/// never from the folders around it: a project on disk that the solution does
/// not list is not offered for running, and a project it lists is offered
/// wherever it sits.
///
/// The folder scan is the fallback, and only that: it is used when the
/// solution file lists no project that exists on disk, or when the file cannot
/// be read or parsed. A project opened alone is always read from its folder,
/// as it always was. The project files are read once per call, so the result
/// is computed off the interface thread with <see cref="Read"/>.
/// </summary>
public static partial class ProjectCatalog
{
    /// <summary>
    /// Reads the projects, the runnable ones and the startup project of a
    /// solution or project path, reading each project file once.
    /// </summary>
    public static ProjectCatalogEntries Read(string solutionOrProjectPath)
    {
        var projects = ProjectsOf(solutionOrProjectPath);
        var texts = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var project in projects) texts[project] = ReadText(project);

        var runnable = projects.Where(p => texts[p] is { } text && IsRunnable(text)).ToList();

        if (!SolutionFile.IsSolutionFile(solutionOrProjectPath))
        {
            // A project opened alone starts itself, even when it is a library.
            var full = Path.GetFullPath(solutionOrProjectPath);
            var own = Path.GetExtension(full) is ".csproj" or ".vbproj" ? full : null;
            return new ProjectCatalogEntries(projects, runnable, own);
        }

        var startup = StartupOf(solutionOrProjectPath, projects, texts);
        return new ProjectCatalogEntries(projects, runnable, startup);
    }

    /// <summary>The projects of the solution, or the folder scan when the solution offers none.</summary>
    public static IReadOnlyList<string> RunnableProjectsOf(string solutionOrProjectPath) =>
        Read(solutionOrProjectPath).Runnable;

    /// <summary>
    /// The project Build and Run use. A project opened alone is its own
    /// startup project even when it is a library. For a solution: the only
    /// executable, or among several the one named after the solution, or the
    /// shallowest. With no executable at all, the first project listed.
    /// </summary>
    public static string? StartupProjectOf(string solutionOrProjectPath) =>
        Read(solutionOrProjectPath).Startup;

    private static string? StartupOf(
        string solutionPath, IReadOnlyList<string> projects, IReadOnlyDictionary<string, string?> texts)
    {
        var candidates = projects
            .Where(p => Path.GetExtension(p) is ".csproj" or ".vbproj")
            // Build output holds copies of a project file, and one picked from
            // there builds into a folder that is deleted on the next clean.
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        // An executable project, and among several the one whose name matches
        // the solution's: a solution called Basalt with a Basalt.Shell and a
        // Basalt.Razor.Vb.LanguageServer has an obvious answer, and taking
        // whichever the solution listed first gets it wrong about half the time.
        var executables = candidates.Where(p => texts[p] is { } text && IsExecutable(text)).ToList();

        if (executables.Count == 0) return candidates.FirstOrDefault();
        if (executables.Count == 1) return executables[0];

        var solutionName = Path.GetFileNameWithoutExtension(solutionPath);

        return executables.FirstOrDefault(p =>
                   Path.GetFileNameWithoutExtension(p)
                       .Equals(solutionName, StringComparison.OrdinalIgnoreCase))
            // Then the shallowest: a program sits beside its solution far more
            // often than a tool three folders down does.
            ?? executables.OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar))
                          .ThenBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                          .First();
    }

    /// <summary>
    /// The projects a solution file lists, or the folder scan when it lists
    /// none that exist, or cannot be read or parsed. A project path gives the
    /// folder scan of its own folder.
    /// </summary>
    public static IReadOnlyList<string> ProjectsOf(string solutionOrProjectPath)
    {
        if (SolutionFile.IsSolutionFile(solutionOrProjectPath))
        {
            var listed = SolutionFile.ProjectsOf(solutionOrProjectPath);
            if (listed.Count > 0) return listed;
        }

        return FolderProjects(solutionOrProjectPath);
    }

    private static IReadOnlyList<string> FolderProjects(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is null) return [];

        try
        {
            return
            [
                .. Directory.EnumerateFiles(directory, "*.*proj", SearchOption.AllDirectories)
                    .Where(p => Path.GetExtension(p) is ".csproj" or ".vbproj")
                    .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                             && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    .Order(StringComparer.Ordinal)
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? ReadText(string projectPath)
    {
        try
        {
            return File.ReadAllText(projectPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Whether a project file builds something that can be started.</summary>
    /// <remarks>
    /// Read from the project file rather than guessed from its name: a library
    /// called "Runner" is still a library. The Web SDK infers an executable
    /// without saying OutputType.
    /// </remarks>
    private static bool IsRunnable(string text) =>
        text.Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase)
        || text.Contains("<OutputType>WinExe</OutputType>", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Sdk=\"Microsoft.NET.Sdk.Web\"", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the project's OutputType is an executable. Read from the
    /// property rather than by searching the text: the word appears inside
    /// "exercising" in a comment, which once made a test project the one to run.
    /// </summary>
    private static bool IsExecutable(string text)
    {
        var match = OutputTypeElement().Match(text);

        return match.Success
            && match.Groups[1].Value.EndsWith("Exe", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"<OutputType>\s*(\w+)\s*</OutputType>", RegexOptions.IgnoreCase)]
    private static partial Regex OutputTypeElement();
}
