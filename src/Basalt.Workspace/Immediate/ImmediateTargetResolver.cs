using Basalt.Core.Localization;
using Basalt.Core.Model;
using Basalt.Core.Services;

namespace Basalt.Workspace.Immediate;

/// <summary>
/// Decides which built assembly a design-time line is evaluated against, and
/// builds the startup project when that assembly is out of date.
///
/// The assembly found is remembered per project and configuration: switching
/// the startup project or Debug/Release must never hand back the other one's
/// output. (The target framework needs no part in the key: it is a property
/// of the project file, so changing it makes the file newer than the assembly
/// and the next line rebuilds.)
/// </summary>
public sealed class ImmediateTargetResolver
{
    private readonly Func<string?> _startupProject;
    private readonly Func<string> _configuration;
    private readonly Func<bool> _isBusy;
    private readonly Func<string, string, CancellationToken, Task<BuildResult>> _build;

    private readonly Dictionary<(string Project, string Configuration), string> _assemblies = [];
    private readonly BuildGate _gate;

    /// <param name="buildGate">
    /// Held for the length of any build of the startup project, by this class
    /// and by every other build, run, publish and test run: two builds of one
    /// project writing one output folder corrupt each other.
    /// </param>
    /// <param name="isBusy">True while the IDE is building or running the program: a build now would fight it.</param>
    public ImmediateTargetResolver(
        Func<string?> startupProject,
        Func<string> configuration,
        Func<bool> isBusy,
        Func<string, string, CancellationToken, Task<BuildResult>> build,
        BuildGate? buildGate = null)
    {
        _gate = buildGate ?? new BuildGate();
        _startupProject = startupProject;
        _configuration = configuration;
        _isBusy = isBusy;
        _build = build;
    }

    public async Task<ImmediateTarget> ResolveAsync(CancellationToken ct)
    {
        var projectPath = _startupProject();

        if (projectPath is null) return new ImmediateTarget(null, null);

        var configuration = _configuration();
        var key = (Path.GetFullPath(projectPath), configuration);

        if (Known(key) is { } current && BuiltAssemblyFreshness.IsCurrent(current, projectPath))
            return new ImmediateTarget(current, projectPath);

        // A build the toolbar or a run has under way owns the output folder;
        // building beside it fails on locked files or clobbers its result.
        if (_isBusy())
            return new ImmediateTarget(null, projectPath, Localizer.Get(StringKeys.ImmediateBuildBusy));

        using var held = await _gate
            .AcquireAsync(Localizer.Get(StringKeys.GateImmediate), ct: ct)
            .ConfigureAwait(false);

        {
            // Asked again now that the gate is ours: a run may have started
            // while this line waited for it.
            if (_isBusy())
                return new ImmediateTarget(null, projectPath, Localizer.Get(StringKeys.ImmediateBuildBusy));

            // Another line may have built it while this one waited.
            if (Known(key) is { } built && BuiltAssemblyFreshness.IsCurrent(built, projectPath))
                return new ImmediateTarget(built, projectPath);

            // Same service as the toolbar's Build, so the Windows-desktop
            // compatibility properties are the same. Its lines reach the
            // Output panel, which is where a build is expected to talk; the
            // Problems panel and the busy flag are left alone, because a line
            // typed in a console is not a build the user asked to watch.
            var result = await _build(projectPath, configuration, ct).ConfigureAwait(false);

            if (!result.Succeeded || result.OutputAssemblyPath is null)
            {
                var errors = result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Take(5)
                    .Select(d => $"{d.Id}: {d.Message}");

                return new ImmediateTarget(null, projectPath, string.Join('\n', errors));
            }

            lock (_assemblies) _assemblies[key] = result.OutputAssemblyPath;

            return new ImmediateTarget(result.OutputAssemblyPath, projectPath);
        }
    }

    private string? Known((string, string) key)
    {
        lock (_assemblies) return _assemblies.GetValueOrDefault(key);
    }
}
