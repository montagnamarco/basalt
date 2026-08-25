namespace Basalt.Core.Services;

public sealed record PackageInfo(string Id, string Version, string? Description, string? Authors);

/// <summary>Management of a project's NuGet packages.</summary>
public interface IPackageService
{
    Task<IReadOnlyList<PackageInfo>> SearchAsync(string query, bool includePrerelease, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetVersionsAsync(string packageId, bool includePrerelease, CancellationToken ct = default);
    Task<IReadOnlyList<PackageInfo>> GetInstalledAsync(string projectPath, CancellationToken ct = default);
    Task InstallAsync(string projectPath, string packageId, string version, CancellationToken ct = default);
    Task UninstallAsync(string projectPath, string packageId, CancellationToken ct = default);
}
