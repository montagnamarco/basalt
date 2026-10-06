using System.Diagnostics;
using Basalt.Core.Localization;
using Basalt.Core.Model;

namespace Basalt.Workspace.Publishing;

/// <summary>What to make: one project, for each target, under one output folder.</summary>
/// <param name="OutputRoot">Each target goes to <c>OutputRoot/&lt;rid&gt;</c>.</param>
public sealed record PublishRequest(
    string ProjectPath,
    IReadOnlyList<PublishTarget> Targets,
    string OutputRoot,
    string Configuration = "Release")
{
    /// <summary>
    /// Under <c>bin</c> so that the Solution Explorer, search, the assistant's
    /// sandbox and the default item globs already leave it alone: a published
    /// single file inside the project folder would otherwise be listed,
    /// searched and compiled as content.
    /// </summary>
    public static string DefaultOutputRoot(string projectPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? ".", "bin", "Publish");

    /// <summary>
    /// The output folder as an absolute path: "~" is the home folder and a
    /// relative folder is relative to the project, not to wherever the IDE
    /// happened to be started.
    /// </summary>
    public string ResolvedOutputRoot
    {
        get
        {
            var root = OutputRoot.Trim();
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (root == "~") root = home;
            else if (root.StartsWith("~/", StringComparison.Ordinal) || root.StartsWith("~\\", StringComparison.Ordinal))
                root = Path.Combine(home, root[2..]);

            var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(ProjectPath)) ?? ".";
            return Path.GetFullPath(root, projectDirectory);
        }
    }

    public string OutputFolderFor(PublishTarget target) => Path.Combine(ResolvedOutputRoot, target.Rid);
}

/// <summary>What one target produced, or why it did not.</summary>
public sealed record PublishResult(
    PublishTarget Target,
    bool Succeeded,
    string OutputFolder,
    string? ArtifactPath,
    long SizeBytes,
    IReadOnlyList<IdeDiagnostic> Diagnostics);

/// <summary>
/// "File > Make Project.exe" for the modern world: one self-contained,
/// single-file program per platform, from <c>dotnet publish</c>.
///
/// Runs as a separate process like the build, so a failing publish cannot
/// take the IDE with it, and off the interface thread: output lines arrive on
/// a worker and the caller marshals them. Cancelling kills the process tree.
/// </summary>
public sealed class PublishService
{
    private static readonly System.Text.RegularExpressions.Regex NuGetUnreachable =
        new(@"\bNU(1100|1101|1301)\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    public event EventHandler<string>? OutputReceived;

    /// <summary>
    /// The properties that retarget a Visual Studio Windows desktop project
    /// onto Basalt.Forms, the very ones the build uses, so that such a project
    /// publishes off Windows too. Share the IDE's session.
    /// </summary>
    public WindowsDesktopCompatibilitySession Compatibility { get; set; } = new();

    /// <summary>The arguments each target's process was given, for the tests.</summary>
    internal List<IReadOnlyList<string>> StartedArguments { get; } = [];

    /// <summary>
    /// How the process of a target is made from its working folder and the
    /// <c>dotnet</c> arguments; the tests swap it for a command that needs
    /// neither the network nor the SDK.
    /// </summary>
    internal Func<string, IReadOnlyList<string>, ProcessStartInfo> CreateStartInfo { get; set; } =
        (directory, arguments) =>
        {
            var startInfo = DotnetProcess.CreateStartInfo(directory);
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            return startInfo;
        };

    /// <summary>The <c>dotnet</c> arguments for one target.</summary>
    public static IReadOnlyList<string> BuildArguments(
        string projectPath, PublishTarget target, string outputFolder, string configuration,
        IReadOnlyList<string> compatibilityArguments) =>
    [
        "publish", projectPath,
        "-c", configuration,
        "-r", target.Rid,
        "--self-contained",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-o", outputFolder,
        "--nologo", "-nr:false",
        .. compatibilityArguments
    ];

    /// <summary>
    /// Publishes every target in turn. A failing target does not stop the
    /// others; the results say which failed. Throws
    /// <see cref="OperationCanceledException"/> when cancelled.
    /// </summary>
    public async Task<IReadOnlyList<PublishResult>> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        var results = new List<PublishResult>();
        var project = await Task.Run(() => PublishProjectInfo.Read(request.ProjectPath), ct).ConfigureAwait(false);
        if (project.AssemblyNameUnresolved)
            Report(Localizer.Get(StringKeys.MakeExeAssemblyNameFallback, project.AssemblyName));
        var compatibility = await Compatibility.GetArgumentsAsync(request.ProjectPath, ct).ConfigureAwait(false);

        foreach (var target in request.Targets)
        {
            ct.ThrowIfCancellationRequested();
            Report(Localizer.Get(StringKeys.MakeExePublishing, project.AssemblyName, target.Rid));

            results.Add(await PublishOneAsync(request, project, target, compatibility, ct).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<PublishResult> PublishOneAsync(
        PublishRequest request, PublishProjectInfo project, PublishTarget target,
        IReadOnlyList<string> compatibility, CancellationToken ct)
    {
        var folder = request.OutputFolderFor(target);
        var lines = new List<string>();

        var arguments = BuildArguments(request.ProjectPath, target, folder, request.Configuration, compatibility);
        lock (StartedArguments) StartedArguments.Add(arguments);
        var startInfo = CreateStartInfo(project.ProjectDirectory, arguments);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        void Collect(string? line)
        {
            if (line is null) return;
            lock (lines) lines.Add(line);
            Report(line);
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
            // Stop the publish itself, not just the waiting for it.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            throw;
        }

        string combined;
        lock (lines) combined = string.Join('\n', lines);
        IReadOnlyList<IdeDiagnostic> diagnostics = MsBuildBuildService.ParseDiagnostics(combined);

        if (process.ExitCode != 0)
        {
            // A failure the parser did not recognise must still reach the
            // Problems panel, or the status bar says "failed" with no reason.
            if (!diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                string[] tail;
                lock (lines) tail = [.. lines.Where(l => l.Trim().Length > 0).TakeLast(5).Select(l => l.Trim())];
                diagnostics = [.. diagnostics, new IdeDiagnostic("BASALT0003",
                    Localizer.Get(StringKeys.MakeExeFailedExit, process.ExitCode, string.Join(" | ", tail)),
                    DiagnosticSeverity.Error, request.ProjectPath, 0, 0)];
            }

            if (NuGetUnreachable.IsMatch(combined)) Report(Localizer.Get(StringKeys.MakeExeNuGetHint));

            return new PublishResult(target, false, folder, null, 0, diagnostics);
        }

        try
        {
            var artifact = await Task.Run(() => Finish(project, target, folder), CancellationToken.None).ConfigureAwait(false);
            if (artifact is null)
            {
                var missing = new IdeDiagnostic("BASALT0001",
                    Localizer.Get(StringKeys.MakeExeMissingOutput, target.ExecutableFileName(project.AssemblyName), folder),
                    DiagnosticSeverity.Error, request.ProjectPath, 0, 0);
                Report(missing.Message);
                return new PublishResult(target, false, folder, null, 0, [.. diagnostics, missing]);
            }

            return new PublishResult(target, true, folder, artifact, SizeOf(artifact), diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var failure = new IdeDiagnostic("BASALT0002", exception.Message, DiagnosticSeverity.Error,
                request.ProjectPath, 0, 0);
            Report(exception.Message);
            return new PublishResult(target, false, folder, null, 0, [.. diagnostics, failure]);
        }
    }

    /// <summary>
    /// What a successful publish still needs: the macOS bundle of a graphical
    /// program, the executable bit of a Unix program. Returns what to show the
    /// user, or null when the program file is not there.
    /// </summary>
    private string? Finish(PublishProjectInfo project, PublishTarget target, string folder)
    {
        var file = target.ExecutableFileName(project.AssemblyName);
        var path = Path.Combine(folder, file);
        if (!File.Exists(path)) return null;

        // A publish run from Windows cannot set the bit (the file system has
        // none); the program then needs "chmod +x" on the target machine.
        if (target.Os != PublishOs.Windows) MacAppBundle.MakeExecutable(path);

        if (target.Os != PublishOs.MacOS || !project.IsGraphical) return path;

        // Said on every host: even where the SDK ad-hoc signs the file, the
        // bundle is not signed with a developer identity or notarized.
        Report(Localizer.Get(StringKeys.MakeExeUnsignedBundle));

        if (project.ApplicationIcon is { } icon && !icon.EndsWith(".icns", StringComparison.OrdinalIgnoreCase))
            Report(Localizer.Get(StringKeys.MakeExeIconNotIcns, icon));

        return MacAppBundle.Create(folder, project, file);
    }

    private static long SizeOf(string path) =>
        Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
            : new FileInfo(path).Length;

    /// <summary>"12.3 MB" style size for the Output panel and the status bar.</summary>
    public static string FormatSize(long bytes) =>
        bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} MB"
        : bytes >= 1L << 10 ? $"{bytes / (double)(1L << 10):0.0} KB"
        : $"{bytes} B";

    private void Report(string line) => OutputReceived?.Invoke(this, line);
}
