using System.Text.RegularExpressions;

namespace Basalt.Workspace;

/// <summary>
/// Filesystem side of Solution Explorer's New File, New Folder, Rename and
/// Delete commands: validating a name and creating or removing entries.
///
/// Kept apart from the window and from Roslyn so what touches the
/// filesystem can be tested with a temporary directory and nothing else.
/// </summary>
public static class ExplorerFileOperations
{
    /// <summary>What is wrong with a name the user typed, or <see cref="None"/> when it is fine.</summary>
    public enum NameProblem
    {
        None,
        Empty,
        InvalidCharacters,
        AlreadyExists
    }

    /// <summary>
    /// Checks a name against the rules a file or folder name must follow,
    /// and against what is already in the directory.
    /// </summary>
    /// <param name="ignorePath">
    /// The entry being renamed, so renaming "Foo" to "foo" — the same entry
    /// under a different case — is not refused as already existing.
    /// </param>
    public static NameProblem Validate(string name, string directory, string? ignorePath = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return NameProblem.Empty;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return NameProblem.InvalidCharacters;

        var target = Path.Combine(directory, name);

        if (ignorePath is { Length: > 0 }
            && string.Equals(Path.GetFullPath(target), Path.GetFullPath(ignorePath), PathComparison))
            return NameProblem.None;

        if (File.Exists(target) || Directory.Exists(target)) return NameProblem.AlreadyExists;

        return NameProblem.None;
    }

    /// <summary>Creates an empty file and returns its full path.</summary>
    public static string CreateFile(string directory, string name, string content = "")
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Creates a folder and returns its full path.</summary>
    public static string CreateFolder(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Renames a file or folder on disk and returns the new path.
    ///
    /// A rename that only changes letter case needs its own path: on
    /// Windows and macOS the filesystem is case-insensitive, so moving a
    /// path onto itself under a different case would otherwise collide
    /// with itself. Moved through a name nothing else can hold first.
    /// </summary>
    public static string Rename(string oldPath, string newName, bool isDirectory)
    {
        var directory = Path.GetDirectoryName(oldPath)
            ?? throw new IOException($"'{oldPath}' has no containing folder.");
        var newPath = Path.Combine(directory, newName);

        if (string.Equals(oldPath, newPath, StringComparison.Ordinal)) return oldPath;

        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            var temp = Path.Combine(directory, $".basalt-rename-{Guid.NewGuid():N}");
            MoveOne(oldPath, temp, isDirectory);
            MoveOne(temp, newPath, isDirectory);
            return newPath;
        }

        if (isDirectory ? Directory.Exists(newPath) : File.Exists(newPath))
            throw new IOException($"'{newName}' already exists.");

        MoveOne(oldPath, newPath, isDirectory);
        return newPath;
    }

    private static void MoveOne(string from, string to, bool isDirectory)
    {
        if (isDirectory) Directory.Move(from, to);
        else File.Move(from, to);
    }

    /// <summary>
    /// The Designer.vb and .resx that go with a WinForms form's own .vb
    /// file, the way Visual Studio nests them underneath it in Solution
    /// Explorer — present only when they actually exist on disk, since most
    /// .vb files are not a form and have neither.
    /// </summary>
    public static IReadOnlyList<string> FormCompanions(string path)
    {
        if (!IsFormFile(path)) return [];

        var basePath = path[..^".vb".Length];

        return new[] { basePath + ".Designer.vb", basePath + ".resx" }
            .Where(File.Exists)
            .ToArray();
    }

    /// <summary>
    /// A form's own code file: a .vb file that is not itself the generated
    /// Designer.vb half of the pair.
    /// </summary>
    private static bool IsFormFile(string path) =>
        path.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)
        && !path.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Renames a file together with the <see cref="FormCompanions"/> found
    /// for it, so a form's Designer.vb and .resx follow its own .vb file
    /// under the new name instead of being stranded under the old one.
    ///
    /// Returns every (old, new) path actually moved, the given file first.
    /// If a companion's rename fails — its computed target already exists,
    /// most likely — whatever already moved is moved back, so a failure
    /// partway through does not leave the form split across two names.
    /// </summary>
    public static IReadOnlyList<(string OldPath, string NewPath)> RenameFile(string oldPath, string newName)
    {
        var companionsBefore = FormCompanions(oldPath);
        var moves = new List<(string OldPath, string NewPath)>();

        try
        {
            var newPath = Rename(oldPath, newName, isDirectory: false);
            moves.Add((oldPath, newPath));

            if (companionsBefore.Count > 0 && IsFormFile(newPath))
            {
                var newBase = newPath[..^".vb".Length];

                foreach (var companion in companionsBefore)
                {
                    var companionNewPath = companion.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase)
                        ? newBase + ".Designer.vb"
                        : newBase + ".resx";

                    moves.Add((companion, Rename(companion, Path.GetFileName(companionNewPath), isDirectory: false)));
                }
            }
        }
        catch
        {
            // Through Rename, not File.Move: undoing a case-only rename has
            // the same collision with itself that doing it had.
            foreach (var (old, @new) in Enumerable.Reverse(moves))
                if (File.Exists(@new)) Rename(@new, Path.GetFileName(old), isDirectory: false);

            throw;
        }

        return moves;
    }

    /// <summary>
    /// Rewrites the items of the project that holds the moved files, so
    /// entries naming them explicitly follow the rename: the
    /// <c>&lt;Compile Update="Form1.Designer.vb"&gt;&lt;DependentUpon&gt;Form1.vb&lt;/DependentUpon&gt;</c>
    /// pair Visual Studio writes for every form, which nests the designer
    /// file under the form in its Solution Explorer. The SDK finds the files
    /// by its globs either way; a stale entry only breaks that nesting, and
    /// only once the project is opened in Visual Studio again.
    /// </summary>
    /// <remarks>
    /// Edited as text, not as an XML document, so the rest of the file —
    /// comments, attribute order, indentation — is left exactly as it was.
    /// Returns the project file when it changed, otherwise null.
    /// </remarks>
    public static string? RewriteProjectItems(IReadOnlyList<(string OldPath, string NewPath)> moves)
    {
        if (moves.Count == 0 || ProjectContaining(moves[0].OldPath) is not { } project) return null;

        var projectDirectory = Path.GetDirectoryName(project)!;
        var text = File.ReadAllText(project);
        var rewritten = text;

        foreach (var (oldPath, newPath) in moves)
        {
            var oldItem = Path.GetRelativePath(projectDirectory, oldPath).Replace('/', '\\');
            var newItem = Path.GetRelativePath(projectDirectory, newPath).Replace('/', '\\');

            foreach (var attribute in new[] { "Include", "Update", "Remove" })
            {
                rewritten = Regex.Replace(rewritten,
                    $@"\b{attribute}=""{Regex.Escape(oldItem)}""",
                    $"{attribute}=\"{newItem.Replace("$", "$$")}\"",
                    RegexOptions.IgnoreCase);
            }

            // DependentUpon names its parent relative to the item's own folder.
            rewritten = Regex.Replace(rewritten,
                $@"<DependentUpon>{Regex.Escape(Path.GetFileName(oldPath))}</DependentUpon>",
                $"<DependentUpon>{Path.GetFileName(newPath).Replace("$", "$$")}</DependentUpon>",
                RegexOptions.IgnoreCase);
        }

        if (rewritten == text) return null;

        File.WriteAllText(project, rewritten);
        return project;
    }

    /// <summary>The nearest .vbproj or .csproj at or above the file's folder.</summary>
    private static string? ProjectContaining(string filePath)
    {
        for (var directory = Path.GetDirectoryName(filePath); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            var project = Directory.EnumerateFiles(directory, "*.vbproj")
                .Concat(Directory.EnumerateFiles(directory, "*.csproj"))
                .FirstOrDefault();

            if (project is not null) return project;
        }

        return null;
    }

    /// <summary>Deletes a file, or an entire folder and everything in it.</summary>
    public static void Delete(string path, bool isDirectory)
    {
        if (isDirectory) Directory.Delete(path, recursive: true);
        else File.Delete(path);
    }

    /// <summary>
    /// Where a path that used to be under <paramref name="oldFolder"/> now
    /// is, after that folder was renamed or moved to
    /// <paramref name="newFolder"/>.
    /// </summary>
    public static string Retarget(string path, string oldFolder, string newFolder)
    {
        if (string.Equals(path, oldFolder, StringComparison.Ordinal)) return newFolder;

        var relative = Path.GetRelativePath(oldFolder, path);
        return Path.Combine(newFolder, relative);
    }

    /// <summary>On Windows and macOS paths are case-insensitive, on Linux they are not.</summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
