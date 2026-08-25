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
public sealed record IdeDiagnostic(
    string Id,
    string Message,
    DiagnosticSeverity Severity,
    string? FilePath,
    int Line,
    int Column)
{
    public override string ToString() =>
        FilePath is null
            ? $"{Severity}: {Id}: {Message}"
            : $"{FilePath}({Line},{Column}): {Severity} {Id}: {Message}";
}
