using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Basalt.Workspace.Projects;

/// <summary>
/// The projects a solution lists, read from the solution file itself.
///
/// A solution is not the folder around it. Visual Studio's Outer.sln in one
/// folder can list a project in a sibling folder and leave out one that sits
/// beside it, so the folder tree and the solution disagree. Both formats are
/// read: the classic .sln, whose project lines carry a type GUID, and .slnx,
/// whose Project elements may sit inside Folder groups, nested to any depth.
/// </summary>
public static partial class SolutionFile
{
    /// <summary>
    /// The type GUID of a solution folder. Folders group projects in the
    /// solution file; they are not projects and have no project file of their own.
    /// </summary>
    private const string SolutionFolderTypeId = "2150E333-8FDC-42A3-9474-1A3956D46DE8";

    /// <summary>How paths compare on this system: ignoring case on Windows and macOS.</summary>
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>
    /// The parsed entries of each solution file, keyed by its full path and
    /// reused while the file keeps the same write time and length. Opening a
    /// solution and refreshing its tree both ask for the list; neither should
    /// parse the file again unless it changed.
    /// </summary>
    private static readonly ConcurrentDictionary<string, CachedListing> Cache = new(PathComparer);

    private sealed record CachedListing(DateTime WriteTimeUtc, long Length, IReadOnlyList<string> Entries);

    public static bool IsSolutionFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".sln" or ".slnx";

    /// <summary>
    /// The project files a solution or project path stands for, as full paths
    /// in the order the solution lists them. For a project path, that project.
    /// Entries whose file does not exist are left out, and each file is listed once.
    /// </summary>
    public static IReadOnlyList<string> ProjectsOf(string solutionOrProjectPath)
    {
        var full = Path.GetFullPath(solutionOrProjectPath);
        var listed = Path.GetExtension(full).ToLowerInvariant() switch
        {
            ".sln" or ".slnx" => ListedEntries(full),
            _ => [full],
        };

        var seen = new HashSet<string>(PathComparer);
        var projects = new List<string>();
        foreach (var project in listed)
        {
            if (!IsProjectFile(project) || !File.Exists(project)) continue;
            if (seen.Add(project)) projects.Add(project);
        }
        return projects;
    }

    /// <summary>Whether a path names an MSBuild project file (.vbproj, .csproj and the rest).</summary>
    public static bool IsProjectFile(string path) =>
        Path.GetExtension(path).EndsWith("proj", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The entries of a solution file as full paths, reread only when the file
    /// changed. A file that cannot be read or parsed lists nothing.
    /// </summary>
    private static IReadOnlyList<string> ListedEntries(string solution)
    {
        DateTime writeTime;
        long length;
        try
        {
            var info = new FileInfo(solution);
            writeTime = info.LastWriteTimeUtc;
            length = info.Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        if (Cache.TryGetValue(solution, out var cached)
            && cached.WriteTimeUtc == writeTime && cached.Length == length)
            return cached.Entries;

        var entries = Path.GetExtension(solution).Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ReadSlnx(solution)
            : ReadSln(solution);

        Cache[solution] = new CachedListing(writeTime, length, entries);
        return entries;
    }

    private static IReadOnlyList<string> ReadSln(string solution)
    {
        string text;
        try { text = File.ReadAllText(solution); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }

        var directory = Path.GetDirectoryName(solution)!;
        var entries = new List<string>();
        foreach (Match match in SolutionProjectLine().Matches(text))
        {
            if (string.Equals(match.Groups["type"].Value, SolutionFolderTypeId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryResolve(directory, match.Groups["path"].Value) is { } resolved) entries.Add(resolved);
        }
        return entries;
    }

    private static IReadOnlyList<string> ReadSlnx(string solution)
    {
        XDocument document;
        try { document = XDocument.Load(solution); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return [];
        }

        // Descendants covers Project elements inside Folder groups, nested to any depth.
        var directory = Path.GetDirectoryName(solution)!;
        var entries = new List<string>();
        foreach (var project in document.Descendants().Where(e => e.Name.LocalName == "Project"))
        {
            var relative = (string?)project.Attribute("Path");
            if (!string.IsNullOrWhiteSpace(relative) && TryResolve(directory, relative) is { } resolved)
                entries.Add(resolved);
        }
        return entries;
    }

    /// <summary>
    /// Resolves one entry of a solution file, or null when the entry cannot be
    /// a file path at all. A web-site entry such as http://localhost:50123 or
    /// an entry with a character no path may hold is one bad line, not a
    /// reason to refuse the whole solution: the others still open.
    /// </summary>
    private static string? TryResolve(string directory, string relative)
    {
        try
        {
            return Resolve(directory, relative);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Solution files written on Windows use backslashes, which are ordinary
    /// characters in a file name on macOS and Linux. They are separators here.
    /// </summary>
    private static string Resolve(string directory, string relative) =>
        Path.GetFullPath(Path.Combine(directory, relative.Trim().Replace('\\', Path.DirectorySeparatorChar)));

    // Project line of a classic solution: Project("{type}") = "name", "path", "{guid}".
    [GeneratedRegex("""^\s*Project\("\{(?<type>[^}]+)\}"\)\s*=\s*"[^"]*"\s*,\s*"(?<path>[^"]+)"\s*,""", RegexOptions.Multiline)]
    private static partial Regex SolutionProjectLine();
}
