namespace Basalt.Core.Model;

public enum DiagnosticSeverity
{
    Hidden,
    Info,
    Warning,
    Error
}

/// <summary>
/// UI-independent diagnostic. Lines and columns are 1-based because that is
/// how they are presented to the user.
/// </summary>
/// <param name="ProjectPath">
/// The project (or solution) file MSBuild reported a diagnostic against when
/// it has no position in a source file, such as a NuGet or project-level
/// error. Kept apart from <paramref name="FilePath"/> so that a diagnostic
/// without a source position still says what it is about.
/// </param>
public sealed record IdeDiagnostic(
    string Id,
    string Message,
    DiagnosticSeverity Severity,
    string? FilePath,
    int Line,
    int Column,
    string? ProjectPath = null)
{
    /// <summary>
    /// Where the diagnostic can be opened: its source file, or else the
    /// project file it was reported against. Null when neither is known.
    /// </summary>
    public string? LocationPath => FilePath ?? ProjectPath;

    public override string ToString() =>
        FilePath is null
            ? $"{Severity}: {Id}: {Message}"
            : $"{FilePath}({Line},{Column}): {Severity} {Id}: {Message}";
}
