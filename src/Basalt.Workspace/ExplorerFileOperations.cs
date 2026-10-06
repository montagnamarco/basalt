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
