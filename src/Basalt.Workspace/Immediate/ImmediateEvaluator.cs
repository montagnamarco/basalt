using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;
using Basalt.Core.Localization;
using Microsoft.CodeAnalysis;

namespace Basalt.Workspace.Immediate;

/// <summary>What a design-time line is evaluated against: the built startup project.</summary>
/// <param name="AssemblyPath">The built assembly, or null when there is no project to refer to.</param>
/// <param name="ProjectPath">The project file, for its imports and Option settings.</param>
/// <param name="BuildError">Why the project could not be built, when it could not.</param>
public sealed record ImmediateTarget(string? AssemblyPath, string? ProjectPath, string? BuildError = null);

/// <summary>Something that evaluates a line with no debug session to ask.</summary>
public interface IImmediateEvaluator
{
    Task<ImmediateOutcome> EvaluateAsync(ImmediateInput input, CancellationToken ct = default);
}

/// <summary>
/// Evaluates a line of the Immediate window when nothing is being debugged:
/// compiles it into a tiny program that refers to the built project, and runs
/// that in a separate <c>dotnet</c> process with a deadline.
///
/// Never in the IDE's own process. The line may call into the user's code,
/// which can hang, exit or crash, and the IDE must survive all three.
/// </summary>
public sealed class ImmediateEvaluator : IImmediateEvaluator
{
    /// <summary>How long a line may run before its process is killed.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<MetadataReference>> RuntimeReferences = new();

    private readonly Func<CancellationToken, Task<ImmediateTarget>> _resolveTarget;

    public ImmediateEvaluator(Func<CancellationToken, Task<ImmediateTarget>> resolveTarget) =>
        _resolveTarget = resolveTarget;

    public TimeSpan Timeout { get; set; } = DefaultTimeout;

    public async Task<ImmediateOutcome> EvaluateAsync(ImmediateInput input, CancellationToken ct = default)
    {
        if (input.Kind == ImmediateKind.Empty) return new ImmediateOutcome(ImmediateOutcomeKind.Empty, "");

        var target = await _resolveTarget(ct).ConfigureAwait(false);

        if (target.BuildError is { } buildError)
        {
            return new ImmediateOutcome(
                ImmediateOutcomeKind.BuildFailed,
                Localizer.Get(StringKeys.ImmediateBuildFailed, buildError));
        }

        // The runtime the project targets, which is both what the line is
        // compiled against and what it runs on. Without one installed there
        // is nothing to do, whatever the IDE itself is running on.
        var runtime = SharedFrameworkLocator.Find(SharedFrameworkLocator.MajorOf(target.AssemblyPath, target.ProjectPath));

        if (runtime is null)
            return new ImmediateOutcome(ImmediateOutcomeKind.RuntimeError, Localizer.Get(StringKeys.ImmediateNoRuntime));

        // Compiling is CPU work; the caller may be the interface thread.
        var compiled = await Task.Run(() => Compile(input, target, runtime), ct).ConfigureAwait(false);

        if (compiled.Errors.Count > 0)
            return new ImmediateOutcome(ImmediateOutcomeKind.CompileError, string.Join('\n', compiled.Errors));

        var folder = Path.Combine(Path.GetTempPath(), "basalt-immediate-" + Guid.NewGuid().ToString("N"));

        try
        {
            CreatePrivateFolder(folder);

            var program = Path.Combine(folder, "BasaltImmediate.dll");
            await File.WriteAllBytesAsync(program, compiled.Image!, ct).ConfigureAwait(false);

            var runtimeConfig = Path.Combine(folder, "BasaltImmediate.runtimeconfig.json");
            await File.WriteAllTextAsync(runtimeConfig, RuntimeConfigFor(target.AssemblyPath, runtime), ct)
                .ConfigureAwait(false);

            var projectFolder = target.AssemblyPath is null ? null : Path.GetDirectoryName(target.AssemblyPath);

            var startInfo = DotnetProcess.CreateStartInfo(projectFolder ?? folder);
            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add("--runtimeconfig");
            startInfo.ArgumentList.Add(runtimeConfig);
            startInfo.ArgumentList.Add(program);

            if (projectFolder is not null) startInfo.Environment[ImmediateProgram.ProbeVariable] = projectFolder;

            // The user's environment may ask the host to roll forward onto a
            // different runtime than the one compiled against.
            startInfo.Environment.Remove("DOTNET_ROLL_FORWARD");
            startInfo.Environment.Remove("DOTNET_ROLL_FORWARD_ON_NO_CANDIDATE_FX");

            ChildProcessResult result;

            try
            {
                result = await ChildProcessRunner.RunAsync(startInfo, Timeout, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return new ImmediateOutcome(
                    ImmediateOutcomeKind.RuntimeError,
                    Localizer.Get(StringKeys.ImmediateStartFailed, ex.Message));
            }

            return Describe(result);
        }
        finally
        {
            TryDelete(folder);
        }
    }

    /// <summary>
    /// Makes the folder the program is written to, readable by this user only.
    ///
    /// On Linux the temporary folder is shared by every user, and a program
    /// another user could replace between being written and being run would
    /// run as this one. The name is random and the folder is not listable by
    /// anyone else. (Windows and macOS give each user a private temporary
    /// folder already.)
    /// </summary>
    private static void CreatePrivateFolder(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
            return;
        }

        Directory.CreateDirectory(
            folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private ImmediateOutcome Describe(ChildProcessResult result)
    {
        if (result.Truncated)
        {
            return new ImmediateOutcome(
                ImmediateOutcomeKind.RuntimeError,
                result.Output + "\n" + Localizer.Get(StringKeys.ImmediateOutputTruncated),
                result.ProcessId);
        }

        if (result.TimedOut)
        {
            var text = Localizer.Get(StringKeys.ImmediateTimedOut, (int)Timeout.TotalSeconds);

            return new ImmediateOutcome(
                ImmediateOutcomeKind.TimedOut,
                result.Output.Length > 0 ? result.Output + "\n" + text : text,
                result.ProcessId);
        }

        var note = result.OutputIncomplete ? "\n" + Localizer.Get(StringKeys.ImmediateOutputIncomplete) : "";

        if (result.ExitCode == 0)
            return new ImmediateOutcome(ImmediateOutcomeKind.Output, result.Output + note, result.ProcessId);

        // Anything the line printed before it threw is kept, and the
        // exception follows it.
        var message = result.Error.Length > 0
            ? result.Error
            : Localizer.Get(StringKeys.ImmediateExited, result.ExitCode ?? -1);

        return new ImmediateOutcome(
            ImmediateOutcomeKind.RuntimeError,
            (result.Output.Length > 0 ? result.Output + "\n" + message : message) + note,
            result.ProcessId);
    }

    private sealed record Compiled(IReadOnlyList<string> Errors, byte[]? Image);

    private static Compiled Compile(ImmediateInput input, ImmediateTarget target, InstalledRuntime runtime)
    {
        var options = ImmediateProjectOptions.Read(target.ProjectPath);

        var references = new List<MetadataReference>(RuntimeReferences.GetOrAdd(runtime.Folder, LoadRuntimeReferences));
        references.AddRange(ProjectReferences(target.AssemblyPath, runtime));

        var compilation = ImmediateProgram.CreateCompilation(input, options, references);

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);

        return emitted.Success
            ? new Compiled([], image.ToArray())
            : new Compiled(ImmediateProgram.ErrorsOf(emitted.Diagnostics), null);
    }

    /// <summary>
    /// The assemblies of the installed shared framework (never the IDE's own
    /// folder: see <see cref="SharedFrameworkLocator"/>).
    ///
    /// The line is compiled against implementation assemblies rather than
    /// reference assemblies, which compiles the same code for the purposes of
    /// a one-line evaluation and spares locating a targeting pack.
    /// </summary>
    private static IReadOnlyList<MetadataReference> LoadRuntimeReferences(string frameworkFolder)
    {
        var references = new List<MetadataReference>();

        foreach (var file in Directory.EnumerateFiles(frameworkFolder, "*.dll"))
        {
            if (IsManaged(file)) references.Add(MetadataReference.CreateFromFile(file));
        }

        return references;
    }

    /// <summary>The built assembly and the managed assemblies beside it.</summary>
    private static IEnumerable<MetadataReference> ProjectReferences(string? assemblyPath, InstalledRuntime runtime)
    {
        if (assemblyPath is null || !File.Exists(assemblyPath)) yield break;

        var frameworkNames = RuntimeReferences.GetOrAdd(runtime.Folder, LoadRuntimeReferences)
            .Select(r => Path.GetFileNameWithoutExtension(r.Display))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var folder = Path.GetDirectoryName(assemblyPath)!;

        foreach (var file in Directory.EnumerateFiles(folder, "*.dll"))
        {
            // The framework's own copy wins: a second one makes every type
            // from it ambiguous.
            if (!file.Equals(assemblyPath, StringComparison.OrdinalIgnoreCase)
                && frameworkNames.Contains(Path.GetFileNameWithoutExtension(file))) continue;

            if (IsManaged(file)) yield return MetadataReference.CreateFromFile(file);
        }
    }

    private static bool IsManaged(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            using var reader = new PEReader(stream);

            return reader.HasMetadata && reader.GetMetadataReader().IsAssembly;
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException
                                      or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// The runtime configuration the program starts under.
    ///
    /// The project's own when it has one, keeping its settings, but pinned to
    /// exactly the runtime the line was compiled against and allowed to roll
    /// forward only to a newer patch. A rollForward the project carries (or
    /// the environment, removed in the child's start) would let the host pick
    /// a different major than the references came from.
    /// </summary>
    internal static string RuntimeConfigFor(string? assemblyPath, InstalledRuntime runtime)
    {
        JsonObject? config = null;

        if (assemblyPath is not null)
        {
            var own = Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");

            try
            {
                if (File.Exists(own)) config = JsonNode.Parse(File.ReadAllText(own)) as JsonObject;
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
                config = null;
            }
        }

        config ??= new JsonObject { ["runtimeOptions"] = new JsonObject() };

        var options = config["runtimeOptions"] as JsonObject;

        if (options is null)
        {
            options = new JsonObject();
            config["runtimeOptions"] = options;
        }

        options["tfm"] = $"net{runtime.Major}.0";
        options["rollForward"] = "LatestPatch";

        var core = new JsonObject { ["name"] = "Microsoft.NETCore.App", ["version"] = runtime.Version };

        if (options["frameworks"] is JsonArray many)
        {
            // Keep any other framework the project asks for, pin the core one.
            var others = many.OfType<JsonObject>().Where(f => !SharedFrameworkLocator.IsCore(f)).ToList();
            var rebuilt = new JsonArray(core);

            foreach (var other in others) rebuilt.Add(JsonNode.Parse(other.ToJsonString()));

            options["frameworks"] = rebuilt;
            options.Remove("framework");
        }
        else
        {
            options["framework"] = core;
        }

        return config.ToJsonString();
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temporary folder; the system cleans it up eventually.
        }
    }
}
