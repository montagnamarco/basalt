namespace Basalt.Workspace;

/// <summary>
/// The one answer to "which MSBuild properties does this solution get", shared
/// by every service that touches the projects (restore, build, clean, run,
/// test and Roslyn's MSBuildWorkspace).
///
/// The answer is about the opened solution, not about the project a command
/// happens to name: a solution of a plain console project and an unrelated
/// WinForms project is retargeted as a whole (that is what Roslyn loaded), and
/// asking only about the console project would build it into <c>bin/Debug</c>
/// while the rest of the IDE looks in <c>bin/basalt</c>.
///
/// Nothing is cached. Every question re-reads the solution and project files
/// on the thread pool (a few XML reads), so a project edited after opening
/// (to <c>net8.0-windows</c> or <c>UseWindowsForms</c>, by hand or through the
/// project properties dialog) is retargeted by the very next build, run or
/// test without reopening, and a failed reading is simply retried by the
/// next call instead of being remembered.
/// </summary>
public sealed class WindowsDesktopCompatibilitySession
{
    private readonly Func<string, WindowsDesktopRetargeting> _resolve;
    private readonly object _gate = new();
    private string? _opened;

    public WindowsDesktopCompatibilitySession() : this(WindowsDesktopProjectCompatibility.Resolve) { }

    internal WindowsDesktopCompatibilitySession(Func<string, WindowsDesktopRetargeting> resolve) =>
        _resolve = resolve;

    /// <summary>
    /// Makes the solution or project at <paramref name="solutionOrProjectPath"/>
    /// the one every later question is about, until the next call. The caller
    /// that loads the solution makes this call together with switching its own
    /// workspace, so that the two never name different solutions.
    /// </summary>
    public void Open(string solutionOrProjectPath)
    {
        lock (_gate) _opened = solutionOrProjectPath;
    }

    /// <summary>
    /// The decision for <paramref name="solutionOrProjectPath"/> as it is on
    /// disk now, without making it the opened one. The reading runs on the
    /// thread pool.
    /// </summary>
    public Task<WindowsDesktopRetargeting> ResolveAsync(string solutionOrProjectPath, CancellationToken ct = default) =>
        Task.Run(() => _resolve(solutionOrProjectPath), ct).WaitAsync(ct);

    /// <summary>
    /// The decision for work on <paramref name="path"/>: the opened solution's,
    /// whatever the path (a project of the solution takes the solution's
    /// properties), or, when nothing was opened, the path's own. Read afresh
    /// each time.
    /// </summary>
    public Task<WindowsDesktopRetargeting> GetAsync(string path, CancellationToken ct = default)
    {
        string subject;
        lock (_gate) subject = _opened ?? path;
        return ResolveAsync(subject, ct);
    }

    /// <summary>The properties as dotnet CLI arguments, for a command about <paramref name="path"/>.</summary>
    public async Task<IReadOnlyList<string>> GetArgumentsAsync(string path, CancellationToken ct = default) =>
        (await GetAsync(path, ct).ConfigureAwait(false)).Arguments;
}
