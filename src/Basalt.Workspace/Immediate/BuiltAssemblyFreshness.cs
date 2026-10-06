namespace Basalt.Workspace.Immediate;

/// <summary>
/// Whether a built assembly is at least as new as the sources it came from.
///
/// The Immediate window builds the startup project before evaluating, but a
/// build costs seconds and most lines come right after another with no edit
/// in between; this is how it tells the two cases apart without asking MSBuild.
///
/// Chosen over always asking MSBuild: even an up-to-date incremental build
/// starts dotnet and evaluates the project, seconds that every line of a quick
/// session would pay. The price is that the check must know what a build
/// reads, so it covers the project, the projects it references (transitively)
/// and the Directory.Build.* files above it. Anything it misses is a stale
/// answer until the next real build, which the toolbar's Build always fixes.
/// </summary>
public static class BuiltAssemblyFreshness
{
    private static readonly string[] SourcePatterns =
        ["*.vb", "*.cs", "*.vbhtml", "*.vbrazor", "*.vbpage", "*.resx", "*.vbproj", "*.csproj", "*.props", "*.targets"];

    public static bool IsCurrent(string? assemblyPath, string? projectPath)
    {
        if (assemblyPath is null || !File.Exists(assemblyPath)) return false;
        if (projectPath is null || !File.Exists(projectPath)) return true;

        var built = File.GetLastWriteTimeUtc(assemblyPath);

        foreach (var project in ProjectClosure(projectPath))
        {
            var folder = Path.GetDirectoryName(project)!;

            if (NewestSourceAfter(folder, built)) return false;

            if (BuildPropsAbove(folder).Any(file => File.GetLastWriteTimeUtc(file) > built)) return false;
        }

        return true;
    }

    private static readonly string[] SharedBuildFiles =
        ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json"];

    private static IEnumerable<string> BuildPropsAbove(string folder)
    {
        for (var directory = new DirectoryInfo(folder); directory is not null; directory = directory.Parent)
        {
            foreach (var name in SharedBuildFiles)
            {
                var file = Path.Combine(directory.FullName, name);
                if (File.Exists(file)) yield return file;
            }
        }
    }

    /// <summary>The project and every project it references, directly or not.</summary>
    internal static IReadOnlyList<string> ProjectClosure(string projectPath)
    {
        var seen = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(projectPath));

        while (pending.Count > 0)
        {
            var project = pending.Pop();

            if (seen.Contains(project, StringComparer.OrdinalIgnoreCase) || !File.Exists(project)) continue;

            seen.Add(project);

            try
            {
                var root = System.Xml.Linq.XDocument.Load(project).Root;

                var references = root?.Descendants()
                    .Where(e => e.Name.LocalName == "ProjectReference")
                    .Select(e => e.Attribute("Include")?.Value)
                    .OfType<string>()
                    .Where(path => !path.Contains('$'));

                foreach (var reference in references ?? [])
                {
                    var normalised = reference.Replace('\\', Path.DirectorySeparatorChar);
                    pending.Push(Path.GetFullPath(normalised, Path.GetDirectoryName(project)!));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // An unreadable project is still checked for its own sources.
            }
        }

        return seen;
    }

    private static bool NewestSourceAfter(string folder, DateTime built)
    {
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true };

            foreach (var pattern in SourcePatterns)
            {
                foreach (var file in Directory.EnumerateFiles(folder, pattern, options))
                {
                    if (File.GetLastWriteTimeUtc(file) > built) return true;
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(folder, "*", options))
            {
                // Build output and package caches are not sources: counting
                // them would make every build look stale the moment it ends.
                var name = Path.GetFileName(directory);

                if (name is "bin" or "obj" or "node_modules" or ".git" or ".vs") continue;

                if (NewestSourceAfter(directory, built)) return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cannot tell; a build is the safe answer.
            return true;
        }

        return false;
    }
}
