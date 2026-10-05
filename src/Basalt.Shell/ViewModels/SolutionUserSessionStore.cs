using System.Text.Json;
using Basalt.Core.Settings;

namespace Basalt.Shell.ViewModels;

/// <summary>Stores one solution's UI state beside the solution in its .user file.</summary>
internal static class SolutionUserSessionStore
{
    private const string Marker = "BasaltSolutionSession";

    private sealed class UserFile
    {
        public string Format { get; set; } = Marker;
        public int Version { get; set; } = 1;
        public SolutionSession? Session { get; set; }
    }

    // Microsoft.Common.CurrentVersion.targets imports
    // "$(MSBuildProjectFullPath).user" while building a project, so a session
    // file named exactly "<project>.user" breaks every build of that project
    // with MSB4025. A solution file is never imported this way — .sln/.slnx
    // are not MSBuild project XML, so MSBuild never looks for a ".user" beside
    // them — which keeps the historical name safe there and nowhere else.
    // For a project the file is "<project>.basalt.user": MSBuild imports only
    // the exact "<project>.user", so this is never read as MSBuild XML, and it
    // still ends in ".user" so the Solution Explorer hides it and the usual
    // "*.user" gitignore rule covers it.
    private const string ProjectSessionSuffix = ".basalt.user";

    internal static string PathFor(string solutionPath) =>
        IsSolutionFile(solutionPath) ? solutionPath + ".user" : solutionPath + ProjectSessionSuffix;

    private static bool IsSolutionFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    internal static SolutionSession? Read(string solutionPath) =>
        ReadFrom(PathFor(solutionPath), solutionPath);

    private static SolutionSession? ReadFrom(string path, string solutionPath)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var file = JsonSerializer.Deserialize<UserFile>(File.ReadAllText(path));
            if (file is not { Format: Marker, Version: 1, Session: { } session })
                return null;

            return string.Equals(session.SolutionPath, solutionPath, StringComparison.Ordinal)
                ? session
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or foreign .user file must not prevent the solution opening.
            return null;
        }
    }

    internal static bool Write(SolutionSession session)
    {
        if (session.SolutionPath.Length == 0 || !File.Exists(session.SolutionPath))
            return false;

        var path = PathFor(session.SolutionPath);
        if (File.Exists(path) && Read(session.SolutionPath) is null)
            return false;

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var file = new UserFile { Session = session };
            File.WriteAllText(temporary, JsonSerializer.Serialize(file, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
