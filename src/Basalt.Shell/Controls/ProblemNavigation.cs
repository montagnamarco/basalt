using Basalt.Core.Model;

namespace Basalt.Shell.Controls;

/// <summary>
/// Where opening a Problems row goes.
/// </summary>
public static class ProblemNavigation
{
    /// <summary>
    /// The file to open for a diagnostic and the position to go to. A
    /// diagnostic with a source position goes to it. One without (a NuGet or
    /// project error) opens the project or solution file it was reported
    /// against, at line 0, which means no position. Null when no such file exists.
    /// </summary>
    public static (string Path, int Line, int Column)? TargetOf(IdeDiagnostic diagnostic)
    {
        var path = diagnostic.LocationPath;
        if (path is null || !File.Exists(path)) return null;

        return diagnostic.FilePath is null
            ? (path, 0, 0)
            : (path, diagnostic.Line, diagnostic.Column);
    }
}
