using Basalt.Core.Model;

namespace Basalt.Core.Services;

public sealed record BuildResult(
    bool Succeeded,
    IReadOnlyList<IdeDiagnostic> Diagnostics,
    string? OutputAssemblyPath,
    TimeSpan Duration);

/// <summary>Compilation of Visual Basic projects through MSBuild.</summary>
public interface IBuildService
{
    Task<BuildResult> BuildAsync(string projectPath, string configuration = "Debug", CancellationToken ct = default);
    Task<BuildResult> RestoreAsync(string projectPath, CancellationToken ct = default);
}
