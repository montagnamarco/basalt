using Basalt.Core.Services;

namespace Basalt.Workspace.Testing;

/// <summary>
/// The tests of a solution's folder: the tests of every test project under it.
/// </summary>
/// <summary>
/// What a discovery of a folder found: the tests, and the projects that could not be discovered.
/// </summary>
/// <remarks>
/// A discovery with failures is not a complete picture of the solution, so the
/// window keeps what it showed rather than showing a list with holes in it.
/// </remarks>
public sealed record TestDiscoveryResult(IReadOnlyList<TestCase> Tests, IReadOnlyList<string> Failures)
{
    /// <summary>Whether every test project was discovered.</summary>
    public bool Succeeded => Failures.Count == 0;
}

public static class TestDiscovery
{
    /// <summary>
    /// Discovers the tests of every test project under the folder, one
    /// project after another.
    /// </summary>
    /// <remarks>
    /// A project that fails to discover is reported and skipped; the others
    /// still count, so one broken project does not empty the window. Each
    /// discovery builds its project, and the service holds the build gate
    /// for it, so these never overlap a build.
    ///
    /// The folder scan reads every project file under the folder, which costs
    /// time on a large solution, so it runs on the thread pool and never on the
    /// interface thread. A cancelled discovery stops before the next project.
    /// </remarks>
    /// <param name="findProjects">Replaces the folder scan, for the tests; the real one when null.</param>
    public static async Task<TestDiscoveryResult> DiscoverFolderAsync(
        string directory,
        ITestService service,
        Action<string> report,
        CancellationToken ct = default,
        Func<string, IEnumerable<string>>? findProjects = null)
    {
        var scan = findProjects ?? (folder => TestProjectDetector.FindTestProjects(folder));

        var projects = await Task.Run(() => scan(directory).ToList(), ct).ConfigureAwait(false);

        var found = new List<TestCase>();
        var failures = new List<string>();

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                found.AddRange(await service.DiscoverAsync(project, ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                failures.Add(ex.Message);
                report($"[test] {ex.Message}");
            }
        }

        return new TestDiscoveryResult(found, failures);
    }
}
