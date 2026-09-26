using System.Diagnostics;
using System.Text.RegularExpressions;
using Basalt.Core.Model;
using Basalt.Core.Services;

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

    /// <summary>Diagnostics without a location, for example MSBuild or NuGet errors.</summary>
    [GeneratedRegex(
        @"^(?:\s*\d+>)?\s*(?:[^:\n]+\s:\s)?(?<sev>error|warning)\s+(?<id>[A-Za-z]+[0-9]+):\s+(?<msg>.+?)(?:\s+\[[^\]]+\])?$",
        RegexOptions.Multiline)]
    private static partial Regex GlobalDiagnosticPattern { get; }

    public event EventHandler<string>? OutputReceived;

    public Task<BuildResult> BuildAsync(string projectPath, string configuration = "Debug", CancellationToken ct = default)
        => RunAsync(projectPath, ["build", projectPath, "-c", configuration, "--nologo", "-v", "normal"], ct);

    public Task<BuildResult> RestoreAsync(string projectPath, CancellationToken ct = default)
        => RunAsync(projectPath, ["restore", projectPath, "--nologo"], ct);

    private async Task<BuildResult> RunAsync(string projectPath, string[] arguments, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var output = new List<string>();

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath)),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
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
            if (seen.Add(diagnostic.ToString())) results.Add(diagnostic);
        }

        foreach (Match m in GlobalDiagnosticPattern.Matches(output))
        {
            // Lines carrying a location have already been collected above.
            if (DiagnosticPattern.IsMatch(m.Value)) continue;

            var diagnostic = new IdeDiagnostic(
                m.Groups["id"].Value,
                m.Groups["msg"].Value.Trim(),
                m.Groups["sev"].Value == "error" ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                null, 0, 0);

            if (seen.Add(diagnostic.ToString())) results.Add(diagnostic);
        }

        return results;
    }

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
