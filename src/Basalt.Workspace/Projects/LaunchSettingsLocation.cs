using System.Xml;

namespace Basalt.Workspace.Projects;

/// <summary>
/// The folder that holds a project's <c>launchSettings.json</c>.
///
/// <c>dotnet run</c> reads the file from the project's AppDesignerFolder, the
/// MSBuild property, rather than from a fixed place. A Visual Basic project
/// defaults to <c>My Project</c>; a C# project to <c>Properties</c>. Writing
/// the file anywhere else means the settings are silently ignored: the
/// application starts in Production on the default port.
/// </summary>
public static class LaunchSettingsLocation
{
    public const string FileName = "launchSettings.json";

    /// <summary>The folder the older IDE wrote Visual Basic settings into.</summary>
    private const string LegacyFolder = "Properties";

    /// <summary>The folder the file is read from and written to.</summary>
    public static string DirectoryOf(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        return Path.Combine(projectDirectory, AppDesignerFolderOf(projectPath));
    }

    /// <summary>The file the project's settings are read from and saved to.</summary>
    public static string PathOf(string projectPath) =>
        Path.Combine(DirectoryOf(projectPath), FileName);

    /// <summary>
    /// The file the older IDE wrote for a Visual Basic project, when it is
    /// somewhere other than <see cref="PathOf"/>; null when they are the same.
    /// </summary>
    public static string? LegacyPathOf(string projectPath)
    {
        var legacy = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, LegacyFolder, FileName);
        return string.Equals(legacy, PathOf(projectPath), StringComparison.Ordinal) ? null : legacy;
    }

    /// <summary>
    /// Moves a Visual Basic project's launch settings from the folder the older
    /// IDE wrote them to (Properties/) to where dotnet run reads them. Done when
    /// a solution opens and when a project is run, so the settings take effect
    /// without the user opening the properties window. Nothing is overwritten:
    /// when the new file already exists, the older one is left where it is.
    /// </summary>
    public static void MigrateLegacy(string projectPath)
    {
        var legacy = LegacyPathOf(projectPath);
        var current = PathOf(projectPath);
        if (legacy is null || !File.Exists(legacy) || File.Exists(current)) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(current)!);
            File.Move(legacy, current);

            var legacyFolder = Path.GetDirectoryName(legacy)!;
            if (Directory.Exists(legacyFolder) && !Directory.EnumerateFileSystemEntries(legacyFolder).Any())
                Directory.Delete(legacyFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left in place: the settings are still read from there until the user saves them.
        }
    }

    /// <summary>
    /// The project's AppDesignerFolder: what the project sets, or the SDK's
    /// default for its language. A project file that cannot be read gets the
    /// default, so the settings still have a place.
    /// </summary>
    public static string AppDesignerFolderOf(string projectPath)
    {
        string? declared = null;
        try
        {
            declared = VbProjectFile.Load(projectPath).GetProperty("AppDesignerFolder");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            // Unreadable here is reported when the project is built; the default is the best guess.
        }

        if (!string.IsNullOrWhiteSpace(declared)) return declared.Trim().Replace('\\', Path.DirectorySeparatorChar);

        return Path.GetExtension(projectPath).Equals(".vbproj", StringComparison.OrdinalIgnoreCase)
            ? "My Project"
            : LegacyFolder;
    }
}
