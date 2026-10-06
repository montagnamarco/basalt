using System.Diagnostics;
using Basalt.Workspace.Immediate;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Tests;

/// <summary>
/// Design-time evaluation of Immediate window lines, through the real
/// compile-and-run path: a Visual Basic program compiled in this process and
/// run by a real <c>dotnet</c> child. Skipped where no <c>dotnet</c> host is
/// on the PATH.
/// </summary>
public sealed class ImmediateEvaluationTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "basalt-immediate-tests", Guid.NewGuid().ToString("N"));

    public ImmediateEvaluationTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static bool DotnetAvailable { get; } = ProbeDotnet();

    private static bool ProbeDotnet()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("dotnet", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            return process is not null && process.WaitForExit(30_000) && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static ImmediateEvaluator EvaluatorFor(ImmediateTarget target) => new(_ => Task.FromResult(target));

    private static Task<ImmediateOutcome> Run(ImmediateEvaluator evaluator, string line) =>
        evaluator.EvaluateAsync(ImmediateInput.Parse(line));

    [Fact]
    public async Task AQuestionMarkPrintsTheValueOfTheExpression()
    {
        Assert.SkipUnless(DotnetAvailable, "No dotnet host on the PATH to run the line with.");

        var outcome = await Run(EvaluatorFor(new ImmediateTarget(null, null)), "?1+2");

        Assert.Equal(ImmediateOutcomeKind.Output, outcome.Kind);
        Assert.Equal("3", outcome.Text.Trim());
    }

    [Fact]
    public async Task PrintBehavesLikeTheQuestionMarkAndStringsAreShownWithoutQuotes()
    {
        Assert.SkipUnless(DotnetAvailable, "No dotnet host on the PATH to run the line with.");

        var evaluator = EvaluatorFor(new ImmediateTarget(null, null));

        Assert.Equal("Hello, World", (await Run(evaluator, "Print \"Hello, \" & \"World\"")).Text.Trim());
        Assert.Equal("{1, 2, 3}", (await Run(evaluator, "?New Integer() {1, 2, 3}")).Text.Trim());
        Assert.Equal("Nothing", (await Run(evaluator, "?CType(Nothing, Object)")).Text.Trim());
    }

    [Fact]
    public async Task AStatementIsRunForItsEffect()
    {
        Assert.SkipUnless(DotnetAvailable, "No dotnet host on the PATH to run the line with.");

        var outcome = await Run(
            EvaluatorFor(new ImmediateTarget(null, null)),
            "Dim n = 4 : Console.WriteLine(\"n squared is \" & (n * n))");

        Assert.Equal(ImmediateOutcomeKind.Output, outcome.Kind);
        Assert.Equal("n squared is 16", outcome.Text.Trim());
    }

    [Fact]
    public async Task ACompileErrorIsShownWithItsDiagnosticAndNothingIsRun()
    {
        Assert.SkipUnless(DotnetAvailable, "No dotnet host on the PATH to run the line with.");

        var outcome = await Run(EvaluatorFor(new ImmediateTarget(null, null)), "?undefinedName + 1");

        Assert.Equal(ImmediateOutcomeKind.CompileError, outcome.Kind);
        Assert.Contains("BC30451", outcome.Text);
        Assert.Contains("undefinedName", outcome.Text);
        Assert.Null(outcome.ChildProcessId);
    }

    [Fact]
    public async Task ARuntimeExceptionIsShownAsText()
    {
        Assert.SkipUnless(DotnetAvailable, "No dotnet host on the PATH to run the line with.");

        var outcome = await Run(EvaluatorFor(new ImmediateTarget(null, null)), "?Integer.Parse(\"x\")");

        Assert.Equal(ImmediateOutcomeKind.RuntimeError, outcome.Kind);
        Assert.Contains("FormatException", outcome.Text);
    }

    [Fact]
    public async Task APrintedLineBeforeAnExceptionIsKept()
    {
        Assert.SkipUnless(DotnetAvailable, "No dotnet host on the PATH to run the line with.");

        var outcome = await Run(
            EvaluatorFor(new ImmediateTarget(null, null)),
            "Console.WriteLine(\"before\") : Throw New InvalidOperationException(\"boom\")");

        Assert.Equal(ImmediateOutcomeKind.RuntimeError, outcome.Kind);
        Assert.StartsWith("before", outcome.Text);
        Assert.Contains("InvalidOperationException: boom", outcome.Text);
    }

    [Fact]
    public async Task ALineThatRunsTooLongIsStoppedAndItsProcessIsGone()
    {
        Assert.SkipUnless(DotnetAvailable, "No dotnet host on the PATH to run the line with.");

        var evaluator = EvaluatorFor(new ImmediateTarget(null, null));
        evaluator.Timeout = TimeSpan.FromSeconds(3);

        var stopwatch = Stopwatch.StartNew();
        var outcome = await Run(evaluator, "System.Threading.Thread.Sleep(120000)");
        stopwatch.Stop();

        Assert.Equal(ImmediateOutcomeKind.TimedOut, outcome.Kind);
        Assert.Contains("3 seconds", outcome.Text);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), "The deadline did not end the wait.");

        // The point of the deadline: the child is dead, not merely abandoned.
        Assert.NotNull(outcome.ChildProcessId);
        Assert.True(IsGone(outcome.ChildProcessId!.Value), "The child process is still running after the timeout.");
    }

    private static bool IsGone(int processId)
    {
        var until = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < until)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited) return true;
            }
            catch (ArgumentException)
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return false;
    }

    [Fact]
    public async Task TheBuiltProjectCanBeReferencedWithItsRootNamespaceAndImports()
    {
        Assert.SkipUnless(DotnetAvailable, "No dotnet host on the PATH to run the line with.");

        var assembly = BuildLibrary(@"
Namespace Acme.Widgets
    Public Class Widget
        Public Shared Function Name() As String
            Return ""sprocket""
        End Function
    End Class
End Namespace");

        var project = Path.Combine(_folder, "Acme.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <RootNamespace>Acme</RootNamespace>
                <OptionStrict>On</OptionStrict>
              </PropertyGroup>
              <ItemGroup>
                <Import Include="Acme.Widgets" />
              </ItemGroup>
            </Project>
            """);

        var evaluator = EvaluatorFor(new ImmediateTarget(assembly, project));

        // Unqualified: only the project's own Import makes this resolve.
        var outcome = await Run(evaluator, "?Widget.Name()");

        Assert.True(outcome.Kind == ImmediateOutcomeKind.Output, outcome.Text);
        Assert.Equal("sprocket", outcome.Text.Trim());

        // Option Strict On from the project: late binding is an error.
        var strict = await Run(evaluator, "Dim o As Object = \"s\" : Console.WriteLine(o.Length)");

        Assert.Equal(ImmediateOutcomeKind.CompileError, strict.Kind);
        Assert.Contains("Option Strict On", strict.Text);
    }

    [Fact]
    public async Task AProjectThatDidNotBuildSaysSoInsteadOfEvaluating()
    {
        var evaluator = EvaluatorFor(new ImmediateTarget(null, "x.vbproj", "BC30451: 'q' is not declared."));

        var outcome = await Run(evaluator, "?1");

        Assert.Equal(ImmediateOutcomeKind.BuildFailed, outcome.Kind);
        Assert.Contains("BC30451", outcome.Text);
    }

    [Fact]
    public void ProjectOptionsAreReadFromTheProjectFileAndFallBackToDefaults()
    {
        var project = Path.Combine(_folder, "App.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <RootNamespace>My.App</RootNamespace>
                <OptionStrict>On</OptionStrict>
                <OptionExplicit>Off</OptionExplicit>
                <OptionCompare>Text</OptionCompare>
              </PropertyGroup>
              <ItemGroup>
                <Import Include="System.Text" />
                <Import Include="System.Text" />
                <Import Include="$(Elsewhere)" />
              </ItemGroup>
            </Project>
            """);

        var options = ImmediateProjectOptions.Read(project);

        Assert.Equal("My.App", options.RootNamespace);
        Assert.True(options.OptionStrict);
        Assert.False(options.OptionExplicit);
        Assert.True(options.OptionCompareText);
        Assert.Null(options.OptionInfer);
        Assert.Equal(["System.Text"], options.Imports);

        File.WriteAllText(project, "<not xml");
        Assert.Same(ImmediateProjectOptions.Default, ImmediateProjectOptions.Read(project));
        Assert.Same(ImmediateProjectOptions.Default, ImmediateProjectOptions.Read(null));
    }

    [Fact]
    public void ABuildIsStaleOnlyWhenASourceIsNewerThanTheAssembly()
    {
        var project = Path.Combine(_folder, "Fresh.vbproj");
        var source = Path.Combine(_folder, "Module1.vb");
        var assembly = Path.Combine(_folder, "bin", "Debug", "Fresh.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(assembly)!);

        File.WriteAllText(project, "<Project />");
        File.WriteAllText(source, "Module M\nEnd Module");
        File.WriteAllText(assembly, "x");

        var built = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(project, built.AddMinutes(-2));
        File.SetLastWriteTimeUtc(source, built.AddMinutes(-2));
        File.SetLastWriteTimeUtc(assembly, built);

        Assert.True(BuiltAssemblyFreshness.IsCurrent(assembly, project));

        File.SetLastWriteTimeUtc(source, built.AddMinutes(1));
        Assert.False(BuiltAssemblyFreshness.IsCurrent(assembly, project));

        // Build output is not a source, however new.
        File.SetLastWriteTimeUtc(source, built.AddMinutes(-2));
        File.WriteAllText(Path.Combine(_folder, "bin", "Debug", "Fresh.vb"), "");
        Assert.True(BuiltAssemblyFreshness.IsCurrent(assembly, project));

        Assert.False(BuiltAssemblyFreshness.IsCurrent(Path.Combine(_folder, "missing.dll"), project));
    }

    /// <summary>Compiles a small library, standing in for a built startup project.</summary>
    private string BuildLibrary(string source)
    {
        var references = Directory
            .EnumerateFiles(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            .Where(file =>
            {
                try { using var s = File.OpenRead(file); using var pe = new System.Reflection.PortableExecutable.PEReader(s); return pe.HasMetadata; }
                catch (BadImageFormatException) { return false; }
            })
            .Select(file => (MetadataReference)MetadataReference.CreateFromFile(file));

        var compilation = VisualBasicCompilation.Create(
            "Acme",
            [VisualBasicSyntaxTree.ParseText(source)],
            references,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(_folder, "bin", "Acme.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var result = compilation.Emit(path);
        Assert.True(result.Success, string.Join('\n', result.Diagnostics));

        return path;
    }

    [Fact]
    public async Task CancellingKillsTheRunningChildAndLeavesNothingBehind()
    {
        Assert.SkipUnless(DotnetAvailable, "No dotnet host on the PATH to run the line with.");

        var evaluator = EvaluatorFor(new ImmediateTarget(null, null));
        evaluator.Timeout = TimeSpan.FromMinutes(5);
        var before = ChildProcessRegistry.ProcessIds;
        using var cancel = new CancellationTokenSource();

        var running = evaluator.EvaluateAsync(ImmediateInput.Parse("System.Threading.Thread.Sleep(300000)"), cancel.Token);

        var started = DateTime.UtcNow.AddSeconds(60);
        int[] child = [];

        while (DateTime.UtcNow < started && child.Length == 0)
        {
            child = [.. ChildProcessRegistry.ProcessIds.Except(before)];
            await Task.Delay(50);
        }

        Assert.NotEmpty(child);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);

        Assert.True(IsGone(child[0]), "The child is still running after the line was cancelled.");
        Assert.DoesNotContain(child[0], ChildProcessRegistry.ProcessIds);
    }

    [Fact]
    public async Task EndlessOutputWithNoNewlineIsCutAtTheLimitAndTheChildStopped()
    {
        Assert.SkipUnless(DotnetAvailable, "No dotnet host on the PATH to run the line with.");

        var evaluator = EvaluatorFor(new ImmediateTarget(null, null));
        evaluator.Timeout = TimeSpan.FromSeconds(60);

        var stopwatch = Stopwatch.StartNew();
        var outcome = await Run(evaluator, "Do : Console.Write(\"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\") : Loop");

        Assert.NotEqual(ImmediateOutcomeKind.TimedOut, outcome.Kind);
        Assert.Contains("truncated", outcome.Text);
        Assert.True(outcome.Text.Length <= ChildProcessRunner.CaptureLimit + 200, $"Kept {outcome.Text.Length} characters.");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(45), "The cap did not end the line.");
        Assert.True(IsGone(outcome.ChildProcessId!.Value), "The child is still running after the cap.");
    }

    private string RuntimeListing(params string[] versions)
    {
        var root = Path.Combine(_folder, "shared", "Microsoft.NETCore.App");

        foreach (var version in versions) Directory.CreateDirectory(Path.Combine(root, version));

        return string.Join('\n', versions.Select(v => $"Microsoft.NETCore.App {v} [{root}]"));
    }

    [Fact]
    public void AReleaseIsPreferredToAPreviewOfTheSameOrANewerMajor()
    {
        var listing = RuntimeListing("8.0.11", "10.0.2", "10.0.3-preview.1", "11.0.0-preview.1", "9.0.0");

        // No project: the newest release, not the 11 preview.
        Assert.Equal("10.0.2", SharedFrameworkLocator.Choose(listing, null)!.Version);

        // The project's own major, and a release of it over its own preview.
        Assert.Equal("10.0.2", SharedFrameworkLocator.Choose(listing, 10)!.Version);
        Assert.Equal("9.0.0", SharedFrameworkLocator.Choose(listing, 9)!.Version);
        Assert.Equal("8.0.11", SharedFrameworkLocator.Choose(listing, 8)!.Version);

        // A preview only when no release of that major exists.
        Assert.Equal("11.0.0-preview.1", SharedFrameworkLocator.Choose(listing, 11)!.Version);

        // A major that is not installed falls back to the newest release.
        Assert.Equal("10.0.2", SharedFrameworkLocator.Choose(listing, 7)!.Version);

        Assert.Null(SharedFrameworkLocator.Choose("nothing useful", 8));
        Assert.Null(SharedFrameworkLocator.Choose(
            $"Microsoft.NETCore.App 12.0.0 [{Path.Combine(_folder, "absent")}]", null));
    }

    [Fact]
    public void TheProjectsOwnMajorIsReadFromItsRuntimeConfigThenItsProjectFile()
    {
        var bin = Path.Combine(_folder, "proj", "bin");
        Directory.CreateDirectory(bin);
        var assembly = Path.Combine(bin, "App.dll");
        var project = Path.Combine(_folder, "proj", "App.vbproj");

        File.WriteAllText(project, "<Project><PropertyGroup><TargetFramework>net9.0-windows</TargetFramework></PropertyGroup></Project>");
        Assert.Equal(9, SharedFrameworkLocator.MajorOf(assembly, project));

        File.WriteAllText(project, "<Project><PropertyGroup><TargetFrameworks>netstandard2.0;net8.0</TargetFrameworks></PropertyGroup></Project>");
        Assert.Equal(8, SharedFrameworkLocator.MajorOf(null, project));

        File.WriteAllText(Path.Combine(bin, "App.runtimeconfig.json"),
            "{\"runtimeOptions\":{\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"}}}");
        Assert.Equal(10, SharedFrameworkLocator.MajorOf(assembly, project));

        Assert.Null(SharedFrameworkLocator.MajorOf(null, null));
    }

    [Fact]
    public void TheChildsRuntimeConfigPinsTheRuntimeCompiledAgainstAndOverridesRollForward()
    {
        var bin = Path.Combine(_folder, "cfg");
        Directory.CreateDirectory(bin);
        var assembly = Path.Combine(bin, "App.dll");
        File.WriteAllText(Path.Combine(bin, "App.runtimeconfig.json"),
            "{\"runtimeOptions\":{\"tfm\":\"net8.0\",\"rollForward\":\"LatestMajor\","
          + "\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"8.0.0\"},"
          + "\"configProperties\":{\"System.GC.Server\":true}}}");

        var json = System.Text.Json.Nodes.JsonNode.Parse(
            ImmediateEvaluator.RuntimeConfigFor(assembly, new InstalledRuntime("x", "10.0.2")))!["runtimeOptions"]!;

        Assert.Equal("LatestPatch", json["rollForward"]!.GetValue<string>());
        Assert.Equal("10.0.2", json["framework"]!["version"]!.GetValue<string>());
        Assert.Equal("net10.0", json["tfm"]!.GetValue<string>());
        Assert.True(json["configProperties"]!["System.GC.Server"]!.GetValue<bool>());
    }

    [Fact]
    public async Task AProcessThatLeftAChildHoldingThePipesIsNotATimeout()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses a shell background job.");
        Assert.SkipUnless(File.Exists("/bin/sh"), "No /bin/sh.");

        var start = new ProcessStartInfo("/bin/sh");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("echo done; sleep 9 & exit 0");

        var result = await ChildProcessRunner.RunAsync(start, TimeSpan.FromSeconds(60));

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.OutputIncomplete);
        Assert.Equal("done", result.Output);
    }

    [Fact]
    public void TheRuntimeFoundHoldsTheCoreLibraryTheLineCompilesAgainst()
    {
        Assert.SkipUnless(DotnetAvailable, "No dotnet host on the PATH to run the line with.");

        Assert.NotNull(SharedFrameworkLocator.Folder);
        Assert.True(File.Exists(Path.Combine(SharedFrameworkLocator.Folder!, "System.Private.CoreLib.dll")));
    }

    [Fact]
    public async Task TheBuiltAssemblyIsRememberedPerProjectAndConfigurationAndNeverMixedUp()
    {
        var builds = new List<(string Project, string Configuration)>();
        var project = "A";
        var configuration = "Debug";

        string MakeProject(string name)
        {
            var folder = Path.Combine(_folder, name);
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, name + ".vbproj");
            File.WriteAllText(file, "<Project />");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-5));

            return file;
        }

        var a = MakeProject("A");
        var b = MakeProject("B");

        var resolver = new ImmediateTargetResolver(
            () => project == "A" ? a : b,
            () => configuration,
            () => false,
            (path, config, _) =>
            {
                builds.Add((path, config));
                var assembly = Path.Combine(Path.GetDirectoryName(path)!, "bin", config, Path.GetFileNameWithoutExtension(path) + ".dll");
                Directory.CreateDirectory(Path.GetDirectoryName(assembly)!);
                File.WriteAllText(assembly, "x");

                return Task.FromResult(new Basalt.Core.Services.BuildResult(true, [], assembly, TimeSpan.Zero));
            });

        var first = await resolver.ResolveAsync(default);
        Assert.EndsWith(Path.Combine("A", "bin", "Debug", "A.dll"), first.AssemblyPath);

        // Another startup project must not be handed A's assembly.
        project = "B";
        var second = await resolver.ResolveAsync(default);
        Assert.EndsWith(Path.Combine("B", "bin", "Debug", "B.dll"), second.AssemblyPath);

        // Nor the other configuration's.
        project = "A";
        configuration = "Release";
        var release = await resolver.ResolveAsync(default);
        Assert.EndsWith(Path.Combine("A", "bin", "Release", "A.dll"), release.AssemblyPath);

        // Back to where it was: current, so no new build.
        configuration = "Debug";
        var again = await resolver.ResolveAsync(default);
        Assert.Equal(first.AssemblyPath, again.AssemblyPath);
        Assert.Equal(3, builds.Count);
    }

    [Fact]
    public async Task ABuildOrRunInProgressRefusesALineThatNeedsABuild()
    {
        var project = Path.Combine(_folder, "Busy.vbproj");
        File.WriteAllText(project, "<Project />");
        var built = false;

        var resolver = new ImmediateTargetResolver(
            () => project, () => "Debug", () => true,
            (_, _, _) =>
            {
                built = true;
                return Task.FromResult(new Basalt.Core.Services.BuildResult(true, [], "x.dll", TimeSpan.Zero));
            });

        var target = await resolver.ResolveAsync(default);

        Assert.False(built);
        Assert.Contains("in progress", target.BuildError);
    }

    [Fact]
    public void ABuildIsStaleWhenAReferencedProjectsSourceIsNewer()
    {
        var app = Path.Combine(_folder, "App");
        var lib = Path.Combine(_folder, "Lib");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(lib);

        var appProject = Path.Combine(app, "App.vbproj");
        File.WriteAllText(appProject, "<Project><ItemGroup><ProjectReference Include=\"..\\Lib\\Lib.vbproj\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(lib, "Lib.vbproj"), "<Project />");
        var source = Path.Combine(lib, "Thing.vb");
        File.WriteAllText(source, "Module T\nEnd Module");
        var assembly = Path.Combine(app, "App.dll");
        File.WriteAllText(assembly, "x");

        var built = DateTime.UtcNow;
        foreach (var file in new[] { appProject, Path.Combine(lib, "Lib.vbproj"), source })
            File.SetLastWriteTimeUtc(file, built.AddMinutes(-3));
        File.SetLastWriteTimeUtc(assembly, built);

        Assert.True(BuiltAssemblyFreshness.IsCurrent(assembly, appProject));

        File.SetLastWriteTimeUtc(source, built.AddMinutes(1));
        Assert.False(BuiltAssemblyFreshness.IsCurrent(assembly, appProject));
    }
}
