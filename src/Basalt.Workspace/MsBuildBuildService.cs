using System.Diagnostics;
using System.Text.RegularExpressions;
using Basalt.Core.Model;
using Basalt.Core.Services;
using Basalt.Workspace.Projects;

namespace Basalt.Workspace;

/// <summary>
/// Builds projects by invoking "dotnet build" as a separate process.
/// Compared to the in-process MSBuild API this isolates build failures from the
/// IDE process and avoids conflicts with the MSBuild assemblies already loaded
/// by Roslyn; the price is having to parse the textual output.
/// </summary>
public sealed partial class MsBuildBuildService : IBuildService
{
    /// <summary>
    /// Lines in the form "path(line,column): error CS0103: message".
    /// MSBuild prepends indentation and, in parallel builds, a "1>" prefix.
    /// </summary>
    [GeneratedRegex(
        @"^(?:\s*\d+>)?\s*(?<file>[^(\n]+?)\((?<line>\d+),(?<col>\d+)\):\s+(?<sev>error|warning)\s+(?<id>[A-Za-z]+[0-9]+):\s+(?<msg>.+?)(?:\s+\[[^\]]+\])?$",
        RegexOptions.Multiline)]
    private static partial Regex DiagnosticPattern { get; }

    /// <summary>
    /// Diagnostics without a position in a source file, for example NuGet or
    /// project errors. MSBuild names the project in front of the message
    /// ("App.vbproj : error NU1102: ...") or in brackets after it, and the
    /// brackets can name the solution instead, so both are captured.
    /// </summary>
    [GeneratedRegex(
        @"^(?:\s*\d+>)?\s*(?:(?<prefix>[^\n]+?)\s:\s)?(?<sev>error|warning)\s+(?<id>[A-Za-z]+[0-9]+):\s+(?<msg>.+?)(?:\s+\[(?<bracket>[^\]]+)\])?$",
        RegexOptions.Multiline)]
    private static partial Regex GlobalDiagnosticPattern { get; }

    public event EventHandler<string>? OutputReceived;

    /// <summary>The process the last build or restore started, for the tests.</summary>
    internal ProcessStartInfo? LastStartInfo { get; private set; }

    /// <summary>
    /// Where the properties that retarget a Visual Studio Windows desktop
    /// project onto Basalt.Forms come from. Share one session with the
    /// language service, the run and the test services so that all of them see
    /// the same answer for the opened solution.
    /// </summary>
    public WindowsDesktopCompatibilitySession Compatibility { get; set; } = new();

    // Restore must see the same properties as the build or it writes
    // project.assets.json for the original framework.
    // MSBuild workers can inherit the redirected output pipes. A reused worker
    // can keep those pipes open after the dotnet command exits, which leaves
    // WaitForExitAsync waiting for EOF even though the build has finished.
    public async Task<BuildResult> BuildAsync(string projectPath, string configuration = "Debug", CancellationToken ct = default)
        => await RunAsync(projectPath,
            // Minimal, as Visual Studio shows a build: errors, warnings, the
            // assembly produced and the outcome. "normal" filled the Output
            // panel with the compiler's whole command line and the compiler
            // server's pipe messages; the diagnostics are the same lines.
            ["build", projectPath, "-c", configuration, "--nologo", "-v", "minimal", "-nr:false",
             .. await Compatibility.GetArgumentsAsync(projectPath, ct).ConfigureAwait(false)], ct).ConfigureAwait(false);

    public async Task<BuildResult> RestoreAsync(string projectPath, CancellationToken ct = default)
        => await RunAsync(projectPath,
            ["restore", projectPath, "--nologo", "-nr:false",
             .. await Compatibility.GetArgumentsAsync(projectPath, ct).ConfigureAwait(false)], ct).ConfigureAwait(false);

    private async Task<BuildResult> RunAsync(string projectPath, string[] arguments, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var output = new List<string>();

        var startInfo = DotnetProcess.CreateStartInfo(Path.GetDirectoryName(Path.GetFullPath(projectPath)));
        LastStartInfo = startInfo;
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        void Collect(string? line)
        {
            if (line is null) return;
            lock (output) output.Add(line);
            OutputReceived?.Invoke(this, line);
        }

        process.OutputDataReceived += (_, e) => Collect(e.Data);
        process.ErrorDataReceived += (_, e) => Collect(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation must actually stop the build, not merely stop
            // waiting for it.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            throw;
        }

        stopwatch.Stop();

        string combined;
        lock (output) combined = string.Join('\n', output);

        return new BuildResult(
            Succeeded: process.ExitCode == 0,
            Diagnostics: ParseDiagnostics(combined),
            OutputAssemblyPath: process.ExitCode == 0 ? FindOutputAssembly(combined) : null,
            Duration: stopwatch.Elapsed);
    }

    /// <summary>
    /// Extracts the diagnostics from MSBuild's textual output. Public because
    /// the output format is the most fragile part of the service and must be
    /// covered by tests against real output.
    /// </summary>
    public static List<IdeDiagnostic> ParseDiagnostics(string output)
    {
        var results = new List<IdeDiagnostic>();
        var seen = new HashSet<string>();

        // MSBuild on Windows ends its lines with "\r\n". In multiline mode "$"
        // matches only before "\n", so the "\r" defeated the optional
        // "[project]" suffix, the message swallowed it, and the same error
        // repeated by two targets was listed twice.
        output = output.ReplaceLineEndings("\n");

        foreach (Match m in DiagnosticPattern.Matches(output))
        {
            var diagnostic = new IdeDiagnostic(
                m.Groups["id"].Value,
                m.Groups["msg"].Value.Trim(),
                m.Groups["sev"].Value == "error" ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                m.Groups["file"].Value.Trim(),
                int.Parse(m.Groups["line"].Value),
                int.Parse(m.Groups["col"].Value));

            // MSBuild repeats the same diagnostic for every target that propagates it.
            if (seen.Add(DuplicateKey(diagnostic))) results.Add(diagnostic);
        }

        foreach (Match m in GlobalDiagnosticPattern.Matches(output))
        {
            // Lines carrying a location have already been collected above.
            if (DiagnosticPattern.IsMatch(m.Value)) continue;

            var diagnostic = new IdeDiagnostic(
                m.Groups["id"].Value,
                m.Groups["msg"].Value.Trim(),
                m.Groups["sev"].Value == "error" ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                null, 0, 0,
                ProjectPathOf(m.Groups["prefix"].Value, m.Groups["bracket"].Value));

            if (seen.Add(DuplicateKey(diagnostic))) results.Add(diagnostic);
        }

        return results;
    }

    /// <summary>
    /// What makes two diagnostics the same one. ToString leaves out the project
    /// of a position-less diagnostic, so the same NU1102 or BC30738 reported by
    /// two projects would collapse into one row; the project is part of the key.
    /// </summary>
    private static string DuplicateKey(IdeDiagnostic diagnostic) =>
        $"{diagnostic}|{diagnostic.ProjectPath}";

    /// <summary>
    /// The project or solution file a position-less diagnostic is about: the
    /// one in front of the message when MSBuild gives it, else the bracketed
    /// one when that is a project. A bracketed solution alone says nothing
    /// about which project failed, so it does not stand in for one.
    /// </summary>
    private static string? ProjectPathOf(string prefix, string bracket)
    {
        var front = prefix.Trim();
        if (IsBuildFile(front)) return front;

        var behind = bracket.Trim();
        return IsBuildFile(behind) ? behind : null;
    }

    private static bool IsBuildFile(string path) =>
        path.Length > 0 && (SolutionFile.IsProjectFile(path) || SolutionFile.IsSolutionFile(path));

    /// <summary>Extracts the produced assembly path from the "Project -> path.dll" line.</summary>
    private static string? FindOutputAssembly(string output)
    {
        foreach (var line in output.Split('\n').Reverse())
        {
            var arrow = line.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow < 0) continue;

            var candidate = line[(arrow + 4)..].Trim();
            if (candidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
