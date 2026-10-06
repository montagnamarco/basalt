using System.Diagnostics;
using System.Text;
using Basalt.Core.Platform;
using Basalt.Core.Model;
using Basalt.Core.Services;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Every class that builds the Basalt projects in place runs one at a time.
///
/// A retargeted project references Basalt.Forms and its siblings from the
/// source tree and builds them there, into their own obj and bin. Two builds
/// of the same project at once race on those files. These tests also need
/// NuGet (Avalonia and the other packages of Basalt.Forms): offline they work
/// only with a warm package cache.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WindowsDesktopCollection
{
    public const string Name = "WindowsDesktop";
}

/// <summary>
/// Windows desktop projects written by Visual Studio, built unmodified on
/// macOS and Linux by retargeting them onto Basalt.Forms at build time.
///
/// The inputs are four real projects from https://github.com/dotnet/samples
/// (tests/Basalt.Tests/Fixtures/VisualStudio). Each is copied to a temporary
/// folder and built once for the whole class; the tests then read what the
/// build left behind. On Windows the real WinForms is used and there is
/// nothing to test.
/// </summary>
[Collection(WindowsDesktopCollection.Name)]
public sealed class WindowsDesktopCompatibilityTests : IClassFixture<WindowsDesktopSamples>
{
    private readonly WindowsDesktopSamples _samples;

    public WindowsDesktopCompatibilityTests(WindowsDesktopSamples samples) => _samples = samples;

    public static TheoryData<string> SampleNames => [.. WindowsDesktopSamples.Projects.Keys];

    public static TheoryData<string> DesignerFiles =>
        [.. WindowsDesktopSamples.FindDesignerFiles()];

    // The samples are built only where the Basalt sources are (see
    // WindowsDesktopSamples.InitializeAsync): without them every test that
    // reads a build skips instead of failing.
    private static void RequireUnix()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows builds the projects with the real WinForms.");
        WindowsDesktopSamples.RequireSources();
    }

    private static string RequireSources() => WindowsDesktopSamples.RequireSources();

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void SampleBuildsWithoutErrors(string name)
    {
        RequireUnix();
        var build = _samples.Built[name];
        Assert.True(build.Result.Succeeded, build.Output);
        Assert.DoesNotContain(build.Result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void BuildDoesNotModifyTheProjectFile(string name)
    {
        RequireUnix();
        var build = _samples.Built[name];
        Assert.Equal(build.ProjectBytesBefore, File.ReadAllBytes(build.ProjectPath));
        // The project still says what Visual Studio wrote.
        Assert.Contains("-windows", File.ReadAllText(build.ProjectPath), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void OutputGoesToBinBasaltAndNothingIsWrittenInTheBasaltSources(string name)
    {
        RequireUnix();
        var build = _samples.Built[name];
        var projectDirectory = Path.GetDirectoryName(build.ProjectPath)!;
        var output = Assert.IsType<string>(build.Result.OutputAssemblyPath);
        var expected = Path.GetFullPath(Path.Combine(projectDirectory, "bin", "basalt")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(expected, Path.GetFullPath(output));
        Assert.Contains($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}", output);
        Assert.True(Directory.Exists(Path.Combine(projectDirectory, "obj", "basalt")));

        // A Windows build of the same folder keeps its own bin\Debug and obj\Debug.
        Assert.False(Directory.Exists(Path.Combine(projectDirectory, "bin", "Debug")));
        Assert.False(Directory.Exists(Path.Combine(projectDirectory, "obj", "Debug")));

        var sourceRoot = RequireSources();
        foreach (var project in Directory.GetDirectories(sourceRoot))
        {
            Assert.False(Directory.Exists(Path.Combine(project, "obj", "basalt")), project);
            Assert.False(Directory.Exists(Path.Combine(project, "bin", "basalt")), project);
        }
    }

    [Theory]
    [MemberData(nameof(DesignerFiles))]
    public void DesignerFileRoundTripsByteForByte(string relativePath)
    {
        // Not Unix-only: the designer model is plain parsing.
        var path = Path.Combine(WindowsDesktopSamples.FixturesRoot, relativePath);
        var original = File.ReadAllBytes(path);
        var bom = original.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: bom);

        var text = File.ReadAllText(path);
        var document = new WinFormsDesignerDocument(text);
        Assert.NotEmpty(document.ClassName);
        Assert.Equal(text, document.Source);
        Assert.Equal(original, encoding.GetPreamble().Concat(encoding.GetBytes(document.Source)).ToArray());
    }

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void OutputDependencyFileDoesNotNameTheWindowsOnlyDrawingPackage(string name)
    {
        RequireUnix();
        var build = _samples.Built[name];
        var output = Assert.IsType<string>(build.Result.OutputAssemblyPath);
        var depsFiles = Directory.GetFiles(Path.GetDirectoryName(output)!, "*.deps.json");
        Assert.NotEmpty(depsFiles);
        foreach (var deps in depsFiles)
        {
            // Basalt.Drawing is itself an assembly called System.Drawing.Common
            // (the file name is expected); what must not appear is the NuGet
            // package of that name, the Windows-only one.
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(deps));
            var libraries = document.RootElement.GetProperty("libraries").EnumerateObject().Select(l => l.Name);
            Assert.DoesNotContain(libraries, name => name.StartsWith("System.Drawing.Common/", StringComparison.OrdinalIgnoreCase));
            var targets = document.RootElement.GetProperty("targets").EnumerateObject()
                .SelectMany(t => t.Value.EnumerateObject()).Select(l => l.Name);
            Assert.DoesNotContain(targets, name => name.StartsWith("System.Drawing.Common/", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task RunReceivesTheSamePropertiesAsTheBuildSoItLooksInBinBasalt()
    {
        RequireUnix();
        using var copy = WindowsDesktopSamples.CopyFixture("TaskDialogDemo");
        var projectPath = Path.Combine(copy.Root, WindowsDesktopSamples.Projects["TaskDialogDemo"]);

        // Not built, so "dotnet run --no-build" fails at once instead of
        // opening a window; what is checked is the command line it was given.
        using var run = new ProcessRunService();
        var exited = new TaskCompletionSource<int>();
        run.Exited += (_, code) => exited.TrySetResult(code);
        await run.StartAsync(projectPath);
        Assert.True(await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(60))) == exited.Task);

        Assert.Contains("--no-build", run.LastArguments);
        var expected = WindowsDesktopProjectCompatibility.Resolve(projectPath).Arguments;
        Assert.NotEmpty(expected);
        foreach (var argument in expected)
            Assert.Contains(argument, run.LastArguments);
        Assert.Contains(run.LastArguments, a => a.StartsWith("-p:DirectoryBuildPropsPath=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RoslynSeesNet10AndBasaltFormsForAWindowsDesktopProject()
    {
        RequireUnix();
        var build = _samples.Built["formatting-utility"];
        using var language = new RoslynLanguageService();
        await language.OpenSolutionAsync(build.ProjectPath);

        var solution = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.Solution>(language.CurrentSolution);
        var project = Assert.Single(solution.Projects, p => p.FilePath == build.ProjectPath);
        var compilation = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.Compilation>(
            await project.GetCompilationAsync());
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        Assert.NotNull(compilation.GetTypeByMetadataName("System.Windows.Forms.Form"));
        Assert.Contains(project.ProjectReferences, r => solution.GetProject(r.ProjectId)?.Name == "Basalt.Forms");
    }

    [Theory]
    [InlineData("Conway's-Game-of-Life")]
    [InlineData("Sudoku")]
    public async Task WithoutTheDrawingPackageRemovalSystemDrawingIsAmbiguous(string name)
    {
        RequireUnix();
        // Microsoft.Windows.Compatibility brings the Windows-only
        // System.Drawing.Common next to Basalt.Drawing, and every System.Drawing
        // type is then defined twice. The targets file removes it; this build
        // uses a copy of that file with the removal target deleted.
        using var copy = WindowsDesktopSamples.CopyFixture(name);
        var projectPath = Path.Combine(copy.Root, WindowsDesktopSamples.Projects[name]);
        var withoutRemoval = Path.Combine(copy.Root, "without-removal");
        Directory.CreateDirectory(withoutRemoval);
        foreach (var file in Directory.GetFiles(WindowsDesktopProjectCompatibility.FilesDirectory))
            File.Copy(file, Path.Combine(withoutRemoval, Path.GetFileName(file)));

        var targetsPath = Path.Combine(withoutRemoval, "Basalt.WindowsDesktop.targets");
        var targets = await File.ReadAllTextAsync(targetsPath);
        var start = targets.IndexOf("<Target Name=\"_BasaltRemoveWindowsDrawing\"", StringComparison.Ordinal);
        var end = targets.IndexOf("</Target>", start, StringComparison.Ordinal) + "</Target>".Length;
        Assert.True(start > 0, "The removal target moved; update this test.");
        await File.WriteAllTextAsync(targetsPath, targets.Remove(start, end - start));

        var properties = WindowsDesktopProjectCompatibility.GetGlobalProperties(projectPath);
        var arguments = new List<string> { "build", projectPath, "--nologo", "-nr:false" };
        foreach (var (key, value) in properties)
        {
            var replaced = key switch
            {
                "DirectoryBuildPropsPath" => Path.Combine(withoutRemoval, "Basalt.WindowsDesktop.props"),
                "BeforeMicrosoftNETSdkTargets" => Path.Combine(withoutRemoval, "Basalt.WindowsDesktop.Retarget.targets"),
                "CustomAfterMicrosoftCommonTargets" => targetsPath,
                _ => value
            };
            arguments.Add($"-p:{key}={WindowsDesktopProjectCompatibility.Escape(replaced)}");
        }

        var (exitCode, output) = await RunDotnetAsync(copy.Root, arguments);
        Assert.NotEqual(0, exitCode);
        // BC30560: "x is ambiguous in the namespace System.Drawing" (the text is localised).
        Assert.Contains("BC30560", output, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output)> RunDotnetAsync(string directory, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + await error);
    }
}

/// <summary>Builds each sample once, from a private copy.</summary>
public sealed class WindowsDesktopSamples : IAsyncLifetime
{
    /// <summary>Sample name to the project file, relative to the sample folder.</summary>
    public static IReadOnlyDictionary<string, string> Projects { get; } = new Dictionary<string, string>
    {
        ["formatting-utility"] = "vb/FormatUtility.vbproj",
        ["Conway's-Game-of-Life"] = "VB/Conway's-Game-of-Life/Conway's-Game-of-Life.vbproj",
        ["Sudoku"] = "VB/sudoku/sudoku.vbproj",
        ["TaskDialogDemo"] = "vb/TaskDialogDemo.vbproj",
    };

    public sealed record BuiltSample(
        string ProjectPath, byte[] ProjectBytesBefore, BuildResult Result, string Output);

    public Dictionary<string, BuiltSample> Built { get; } = [];

    private readonly List<TempCopy> _copies = [];

    public static string FixturesRoot { get; } = FindFixturesRoot();

    private static string FindFixturesRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory, "tests", "Basalt.Tests", "Fixtures", "VisualStudio");
            if (Directory.Exists(candidate)) return candidate;
            directory = Path.GetDirectoryName(directory);
        }
        throw new DirectoryNotFoundException("tests/Basalt.Tests/Fixtures/VisualStudio was not found above the test output.");
    }

    /// <summary>Every designer file of the samples that declares a form, relative to the fixtures folder.</summary>
    public static IEnumerable<string> FindDesignerFiles() =>
        Directory.EnumerateFiles(FixturesRoot, "*.Designer.vb", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("InitializeComponent", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(FixturesRoot, f))
            .Order(StringComparer.Ordinal);

    /// <summary>
    /// The Basalt.Forms source folder, or a clean skip when this machine has no
    /// sources (an installed build of the IDE): the retargeting needs them.
    /// </summary>
    public static string RequireSources()
    {
        var root = FindSources();
        Assert.SkipWhen(
            root is null,
            $"The Basalt.Forms sources were not found (set {BasaltSourceRoot.EnvironmentVariable}).");
        return root!;
    }

    /// <summary>The Basalt source folder when it holds Basalt.Forms, otherwise null.</summary>
    public static string? FindSources()
    {
        var root = BasaltSourceRoot.TryFind();
        return root is not null && File.Exists(Path.Combine(root, "Basalt.Forms", "Basalt.Forms.csproj")) ? root : null;
    }

    public sealed class TempCopy : IDisposable
    {
        public TempCopy(string root) => Root = root;
        public string Root { get; }
        public void Dispose() => DeleteQuietly(Root);
    }

    /// <summary>Removes a temporary folder; a leftover must not fail the test that used it.</summary>
    public static void DeleteQuietly(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a build server may still hold a file; the OS cleans the temp folder.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort, as above.
        }
    }

    /// <summary>Copies a sample, byte for byte, into a fresh temporary folder.</summary>
    public static TempCopy CopyFixture(string name)
    {
        var root = Directory.CreateTempSubdirectory("basalt-vs-" + name.Replace('\'', '_') + "-").FullName;
        var source = Path.Combine(FixturesRoot, name);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        // Sudoku's project names Sudoku_icon.ico and the file is sudoku_icon.ico:
        // fine on Windows and macOS, a missing file on a case-sensitive
        // filesystem. The project must stay untouched, so the file is added.
        var misspelled = Path.Combine(root, "VB", "sudoku", "Sudoku_icon.ico");
        var actual = Path.Combine(root, "VB", "sudoku", "sudoku_icon.ico");
        if (File.Exists(actual) && !File.Exists(misspelled)) File.Copy(actual, misspelled);
        return new TempCopy(root);
    }

    public async ValueTask InitializeAsync()
    {
        // Nothing to build on Windows (real WinForms) or without the Basalt
        // sources (an installed IDE): the tests that read a build skip.
        if (OperatingSystem.IsWindows() || FindSources() is null) return;

        foreach (var (name, relative) in Projects)
        {
            var copy = CopyFixture(name);
            _copies.Add(copy);
            var projectPath = Path.Combine(copy.Root, relative);
            var before = await File.ReadAllBytesAsync(projectPath);

            var builder = new MsBuildBuildService();
            var output = new StringBuilder();
            builder.OutputReceived += (_, line) => { lock (output) output.AppendLine(line); };
            var result = await builder.BuildAsync(projectPath);
            Built[name] = new BuiltSample(projectPath, before, result, output.ToString());
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var copy in _copies) copy.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Deciding which solutions need the retargeting, and a solution that mixes a
/// Windows desktop application with a <c>-windows</c> class library.
/// </summary>
[Collection(WindowsDesktopCollection.Name)]
public sealed class WindowsDesktopProjectDetectionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-wd-detect-").FullName;

    public void Dispose() => WindowsDesktopSamples.DeleteQuietly(_root);

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Project(string properties, string items = "", string sdk = "Microsoft.NET.Sdk") => $"""
        <Project Sdk="{sdk}">
          <PropertyGroup>
            <OutputType>Library</OutputType>
            {properties}
          </PropertyGroup>
          {items}
        </Project>
        """;

    [Theory]
    [InlineData("<TargetFramework>net8.0</TargetFramework>", "Microsoft.NET.Sdk", false)]
    [InlineData("<TargetFramework>net8.0-windows</TargetFramework>", "Microsoft.NET.Sdk", true)]
    [InlineData("<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>", "Microsoft.NET.Sdk", true)]
    [InlineData("<TargetFrameworks>net48;net8.0-Windows</TargetFrameworks>", "Microsoft.NET.Sdk", true)]
    [InlineData("<TargetFramework>net8.0</TargetFramework><UseWindowsForms>true</UseWindowsForms>", "Microsoft.NET.Sdk", true)]
    [InlineData("<TargetFramework>net8.0</TargetFramework>", "Microsoft.NET.Sdk.WindowsDesktop", true)]
    public void ProjectIsRecognisedByUseWindowsFormsWindowsFrameworkOrSdk(string properties, string sdk, bool expected)
    {
        var path = Write("App/App.vbproj", Project(properties, sdk: sdk));
        Assert.Equal(expected, WindowsDesktopProjectCompatibility.NeedsRetargeting(path));
    }

    [Fact]
    public void SolutionsAreCheckedProjectByProjectInBothFormats()
    {
        Write("Console/Console.vbproj", Project("<TargetFramework>net10.0</TargetFramework>"));
        Write("Lib/Lib.vbproj", Project("<TargetFramework>net8.0-windows</TargetFramework>"));
        const string vb = "{F184B08F-C81C-45F6-A57F-5ABD9991F28F}";
        var console = $"Project(\"{vb}\") = \"Console\", \"Console\\Console.vbproj\", \"{{11111111-1111-1111-1111-111111111111}}\"\r\nEndProject\r\n";
        var lib = $"Project(\"{vb}\") = \"Lib\", \"Lib\\Lib.vbproj\", \"{{22222222-2222-2222-2222-222222222222}}\"\r\nEndProject\r\n";

        Assert.False(WindowsDesktopProjectCompatibility.NeedsRetargeting(Write("a.sln", console)));
        Assert.True(WindowsDesktopProjectCompatibility.NeedsRetargeting(Write("b.sln", console + lib)));
        Assert.False(WindowsDesktopProjectCompatibility.NeedsRetargeting(
            Write("a.slnx", "<Solution><Project Path=\"Console/Console.vbproj\" /></Solution>")));
        Assert.True(WindowsDesktopProjectCompatibility.NeedsRetargeting(
            Write("b.slnx", "<Solution><Project Path=\"Console/Console.vbproj\" /><Project Path=\"Lib/Lib.vbproj\" /></Solution>")));
    }

    [Fact]
    public void ProjectReferenceToAWindowsLibraryCountsEvenWhenTheLibraryIsNotInTheSolution()
    {
        Write("Lib/Lib.vbproj", Project("<TargetFramework>net8.0-windows</TargetFramework>"));
        var app = Write("App/App.vbproj", Project(
            "<TargetFramework>net10.0</TargetFramework>",
            """<ItemGroup><ProjectReference Include="..\Lib\Lib.vbproj" /></ItemGroup>"""));
        Assert.True(WindowsDesktopProjectCompatibility.NeedsRetargeting(app));
    }

    [Fact]
    public void WindowsReceivesNoPropertiesAndNeitherDoProjectsInsideTheBasaltSources()
    {
        var app = Write("App/App.vbproj", Project("<TargetFramework>net8.0-windows</TargetFramework>"));
        var sourceRoot = WindowsDesktopSamples.RequireSources();

        Assert.Empty(WindowsDesktopProjectCompatibility.GetGlobalProperties(app, isWindows: true, sourceRoot));
        Assert.Empty(WindowsDesktopProjectCompatibility.GetGlobalProperties(app, isWindows: false, sourceRoot: null));

        var inside = Path.Combine(sourceRoot, "Basalt.Probe", "Probe.vbproj");
        Assert.Empty(WindowsDesktopProjectCompatibility.GetGlobalProperties(inside, isWindows: false, sourceRoot));

        var properties = WindowsDesktopProjectCompatibility.GetGlobalProperties(app, isWindows: false, sourceRoot);
        Assert.Equal(
            ["BasaltSourceRoot", "BeforeMicrosoftNETSdkTargets", "CustomAfterMicrosoftCommonTargets", "DirectoryBuildPropsPath"],
            properties.Keys.Order(StringComparer.Ordinal));
        Assert.All(properties.Where(p => p.Key != "BasaltSourceRoot"), p => Assert.True(File.Exists(p.Value), p.Value));
    }

    [Fact]
    public async Task SolutionOfAnApplicationAndAWindowsLibraryBuildsBothWithoutTouchingTheirFiles()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows builds the projects with the real WinForms.");

        // A versioned platform on the application, a plain one on the library:
        // both must end up as net10.0 or the reference between them is invalid.
        var lib = Write("Lib/Lib.vbproj", Project("<TargetFramework>net8.0-windows</TargetFramework><RootNamespace></RootNamespace>"));
        Write("Lib/Greeter.vb", """
            Public Class Greeter
                Public Shared Function Greeting() As String
                    Return "Hello from " & GetType(System.Windows.Forms.Label).Name
                End Function
            End Class
            """);
        var app = Write("App/App.vbproj", Project(
            "<OutputType>Exe</OutputType><TargetFramework>net8.0-windows10.0.19041.0</TargetFramework><UseWindowsForms>true</UseWindowsForms>",
            """<ItemGroup><ProjectReference Include="..\Lib\Lib.vbproj" /></ItemGroup>"""));
        Write("App/Program.vb", """
            Module Program
                Sub Main()
                    System.Console.WriteLine(Greeter.Greeting())
                End Sub
            End Module
            """);
        var solution = Write("Mixed.sln", """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{F184B08F-C81C-45F6-A57F-5ABD9991F28F}") = "App", "App\App.vbproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{F184B08F-C81C-45F6-A57F-5ABD9991F28F}") = "Lib", "Lib\Lib.vbproj", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            Global
                GlobalSection(SolutionConfigurationPlatforms) = preSolution
                    Debug|Any CPU = Debug|Any CPU
                EndGlobalSection
                GlobalSection(ProjectConfigurationPlatforms) = postSolution
                    {11111111-1111-1111-1111-111111111111}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                    {11111111-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0 = Debug|Any CPU
                    {22222222-2222-2222-2222-222222222222}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                    {22222222-2222-2222-2222-222222222222}.Debug|Any CPU.Build.0 = Debug|Any CPU
                EndGlobalSection
            EndGlobal
            """);
        var before = (File.ReadAllBytes(app), File.ReadAllBytes(lib));

        var builder = new MsBuildBuildService();
        var output = new StringBuilder();
        builder.OutputReceived += (_, line) => { lock (output) output.AppendLine(line); };
        var result = await builder.BuildAsync(solution);

        Assert.True(result.Succeeded, output.ToString());
        Assert.Equal(before.Item1, File.ReadAllBytes(app));
        Assert.Equal(before.Item2, File.ReadAllBytes(lib));
        Assert.True(File.Exists(Path.Combine(_root, "App", "bin", "basalt", "Debug", "net10.0", "App.dll")),
            output.ToString());
        Assert.True(File.Exists(Path.Combine(_root, "Lib", "bin", "basalt", "Debug", "net10.0", "Lib.dll")));
        Assert.False(Directory.Exists(Path.Combine(_root, "Lib", "obj", "Debug")));
    }

    // ---- a project edited after the solution was opened -------------------

    /// <summary>A session that decides as production does, against a source root that need only look like one.</summary>
    private WindowsDesktopCompatibilitySession SessionWithFakeSources()
    {
        Write("FakeSources/Basalt.Forms/Basalt.Forms.csproj", "<Project />");
        var root = Path.Combine(_root, "FakeSources");
        return new WindowsDesktopCompatibilitySession(
            path => WindowsDesktopProjectCompatibility.Resolve(path, isWindows: false, root));
    }

    [Fact]
    public async Task ProjectEditedToWindowsAfterOpeningIsRetargetedWithoutReopening()
    {
        var lib = Write("Lib/Lib.vbproj", Project("<TargetFramework>net10.0</TargetFramework>"));
        var session = SessionWithFakeSources();
        session.Open(lib);
        Assert.Empty(await session.GetArgumentsAsync(lib));

        // What the project properties dialog does: rewrites the project file.
        File.WriteAllText(lib, Project("<TargetFramework>net8.0-windows</TargetFramework>"));

        var after = await session.GetArgumentsAsync(lib);
        Assert.Contains(after, a => a.StartsWith("-p:DirectoryBuildPropsPath=", StringComparison.Ordinal));

        // And back: nothing is remembered.
        File.WriteAllText(lib, Project("<TargetFramework>net10.0</TargetFramework>"));
        Assert.Empty(await session.GetArgumentsAsync(lib));
    }

    [Fact]
    public async Task SolutionAnswerFollowsProjectEditsForAnyProjectPathAsked()
    {
        var console = Write("Console/Console.vbproj", Project("<TargetFramework>net10.0</TargetFramework>"));
        var forms = Write("Forms/Forms.vbproj", Project("<TargetFramework>net10.0</TargetFramework>"));
        var solution = Write("Mixed.sln", SolutionOf(("Console", @"Console\Console.vbproj"), ("Forms", @"Forms\Forms.vbproj")));
        var session = SessionWithFakeSources();
        session.Open(solution);
        Assert.Empty(await session.GetArgumentsAsync(console));

        File.WriteAllText(forms, Project("<TargetFramework>net8.0-windows</TargetFramework>"));

        Assert.NotEmpty(await session.GetArgumentsAsync(console));
    }

    [Fact]
    public async Task FailedResolutionIsNotCachedAndIsRetriedByTheNextCall()
    {
        var calls = 0;
        var session = new WindowsDesktopCompatibilitySession(_ =>
            ++calls == 1 ? throw new IOException("disk went away") : WindowsDesktopRetargeting.None);

        var error = await Assert.ThrowsAsync<IOException>(() => session.GetAsync("a.vbproj"));
        Assert.Equal("disk went away", error.Message);

        Assert.Same(WindowsDesktopRetargeting.None, await session.GetAsync("a.vbproj"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task BuildAfterEditingTheProjectToWindowsRetargetsIt()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows builds the projects with the real WinForms.");
        WindowsDesktopSamples.RequireSources();
        var lib = Write("Lib/Lib.vbproj", Project("<TargetFramework>net10.0</TargetFramework><RootNamespace></RootNamespace>"));
        Write("Lib/Thing.vb", "Public Class Thing\nEnd Class\n");
        var session = new WindowsDesktopCompatibilitySession();
        session.Open(lib);
        var builder = new MsBuildBuildService { Compatibility = session };
        var output = new StringBuilder();
        builder.OutputReceived += (_, line) => { lock (output) output.AppendLine(line); };
        Assert.True((await builder.BuildAsync(lib)).Succeeded, output.ToString());

        // Edited to a Windows Forms project while the IDE stays open.
        File.WriteAllText(lib, Project(
            "<TargetFramework>net8.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><RootNamespace></RootNamespace>"));
        Write("Lib/Thing.vb", "Public Class Thing\n    Public Property L As System.Windows.Forms.Label\nEnd Class\n");
        output.Clear();
        var result = await builder.BuildAsync(lib);

        Assert.True(result.Succeeded, output.ToString());
        Assert.True(File.Exists(Path.Combine(_root, "Lib", "bin", "basalt", "Debug", "net10.0", "Lib.dll")), output.ToString());
    }

    // ---- one session for the whole window --------------------------------

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheWindowGivesTheSameSessionToEveryServiceThatBuildsRunsTestsOrLoads()
    {
        using var host = new TestWindow();
        var viewModel = Assert.IsType<Basalt.Shell.ViewModels.MainWindowViewModel>(host.Window.DataContext);

        // The VM's services (language, build, run) ...
        Assert.All(viewModel.ServiceSessionsForTests, session => Assert.Same(viewModel.Compatibility, session));

        // ... and the one the window itself owns: "dotnet test" builds too.
        var field = typeof(Basalt.Shell.MainWindow).GetField(
            "_testService", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var tests = Assert.IsType<Basalt.Workspace.Testing.DotnetTestService>(field.GetValue(host.Window));
        Assert.Same(viewModel.Compatibility, tests.Compatibility);
    }

    // ---- the session follows the workspace --------------------------------

    [Fact]
    public async Task CancelledOpenLeavesTheSessionOnTheSolutionThatIsStillLoaded()
    {
        var first = Write("First/First.vbproj", Project("<TargetFramework>net10.0</TargetFramework><RootNamespace></RootNamespace>"));
        Write("First/Module1.vb", "Module Module1\nEnd Module\n");
        var second = Write("Second/Second.vbproj", Project("<TargetFramework>net10.0</TargetFramework><RootNamespace></RootNamespace>"));
        Write("Second/Module1.vb", "Module Module1\nEnd Module\n");
        var asked = new List<string>();
        var session = new WindowsDesktopCompatibilitySession(path =>
        {
            lock (asked) asked.Add(path);
            return WindowsDesktopRetargeting.None;
        });
        using var language = new RoslynLanguageService { Compatibility = session };
        await language.OpenSolutionAsync(first);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => language.OpenSolutionAsync(second, cancelled.Token));

        asked.Clear();
        await session.GetAsync("whatever.vbproj");
        Assert.Equal([first], asked);
        Assert.Equal("First", Assert.Single(language.CurrentSolution!.Projects).Name);

        await language.OpenSolutionAsync(second);
        asked.Clear();
        await session.GetAsync("whatever.vbproj");
        Assert.Equal([second], asked);
    }

    // ---- escaping -------------------------------------------------------

    [Theory]
    [InlineData("/Users/me/100%41/src", "/Users/me/100%2541/src")]
    [InlineData("/a;b,c", "/a%3Bb%2Cc")]
    [InlineData("/it's here", "/it's here")]
    [InlineData("/x%41;y'z,%", "/x%2541%3By'z%2C%25")]
    public void PropertyValuesAreEscapedForTheCommandLine(string value, string expected) =>
        Assert.Equal(expected, WindowsDesktopProjectCompatibility.Escape(value));

    [Fact]
    public async Task EscapedArgumentReachesMsBuildAsTheOriginalPath()
    {
        // A directory that holds a percent escape lookalike, a semicolon, a
        // comma and an apostrophe: what MSBuild evaluates must be the path
        // itself, not "A", not two properties.
        var tricky = Path.Combine(_root, "odd %41;a,b 'c'");
        var project = Write("Probe/Probe.proj", "<Project><Target Name=\"T\" /></Project>");
        var properties = new Dictionary<string, string> { ["BasaltSourceRoot"] = tricky };

        var arguments = new List<string> { "msbuild", project, "-getProperty:BasaltSourceRoot" };
        arguments.AddRange(WindowsDesktopProjectCompatibility.ToArguments(properties));
        var (exitCode, output) = await RunDotnetAsync(_root, arguments);

        Assert.Equal(0, exitCode);
        Assert.Equal(tricky, output.Trim());
    }

    [Fact]
    public async Task RoslynReceivesTheOriginalValueOfAnEscapedProperty()
    {
        // MSBuildWorkspace reads property values as MSBuild-escaped text, like
        // the command line does: a raw "%41" would arrive as "A".
        var tricky = "odd%41;a,b'c";
        var project = Write("Probe/Probe.vbproj", Project(
            "<TargetFramework>net10.0</TargetFramework><RootNamespace>$(Marker)</RootNamespace>"));
        Write("Probe/Module1.vb", "Module Module1\nEnd Module\n");
        using var language = new RoslynLanguageService();
        language.Compatibility = new WindowsDesktopCompatibilitySession(_ =>
            new WindowsDesktopRetargeting(new Dictionary<string, string> { ["Marker"] = tricky }, false));
        await language.OpenSolutionAsync(project);

        var opened = Assert.Single(Assert.IsAssignableFrom<Microsoft.CodeAnalysis.Solution>(language.CurrentSolution).Projects);
        Assert.Equal(tricky, opened.DefaultNamespace);
    }

    // ---- sources missing, case sensitivity -------------------------------

    [Fact]
    public void WindowsProjectWithoutBasaltSourcesIsReportedNotSilentlyIgnored()
    {
        var needs = Write("A/A.vbproj", Project("<TargetFramework>net8.0-windows</TargetFramework>"));
        var plain = Write("B/B.vbproj", Project("<TargetFramework>net10.0</TargetFramework>"));

        var missing = WindowsDesktopProjectCompatibility.Resolve(needs, isWindows: false, sourceRoot: null);
        Assert.True(missing.SourcesMissing);
        Assert.Empty(missing.Properties);
        Assert.False(WindowsDesktopProjectCompatibility.Resolve(plain, isWindows: false, sourceRoot: null).SourcesMissing);
        Assert.False(WindowsDesktopProjectCompatibility.Resolve(needs, isWindows: true, sourceRoot: null).SourcesMissing);

        // A folder that does not hold Basalt.Forms is no better than none.
        Assert.True(WindowsDesktopProjectCompatibility.Resolve(needs, isWindows: false, _root).SourcesMissing);
    }

    [Fact]
    public void SourceRootComparisonFollowsTheFileSystemCase()
    {
        // The source root is spelled "SRC" and the project lives in "src".
        // On macOS and Windows those are one folder, so the project is inside
        // the Basalt sources and is left alone; on Linux they are two folders.
        Write("SRC/Basalt.Forms/Basalt.Forms.csproj", "<Project />");
        var app = Write("src/App/App.vbproj", Project("<TargetFramework>net8.0-windows</TargetFramework>"));
        var caseInsensitive = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

        var properties = WindowsDesktopProjectCompatibility.GetGlobalProperties(
            app, isWindows: false, Path.Combine(_root, "SRC"));

        Assert.Equal(caseInsensitive, properties.Count == 0);
    }

    // ---- one answer for the whole solution ------------------------------

    private static string SolutionOf(params (string Name, string Path)[] projects)
    {
        var text = new StringBuilder("Microsoft Visual Studio Solution File, Format Version 12.00\r\n");
        foreach (var (name, path) in projects)
            text.Append(
                $"Project(\"{{F184B08F-C81C-45F6-A57F-5ABD9991F28F}}\") = \"{name}\", \"{path}\", \"{{{Guid.NewGuid()}}}\"\r\nEndProject\r\n");
        return text.ToString();
    }

    [Fact]
    public async Task ConsoleStartupProjectBuildsRunsAndIsLoadedByRoslynTheSameWayAsItsWinFormsSibling()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows builds the projects with the real WinForms.");
        WindowsDesktopSamples.RequireSources();

        // The startup project is a plain console application; the solution
        // also holds a WinForms project that nothing references. The solution
        // is retargeted as a whole, so the console project must build into
        // bin/basalt and run from there, exactly where Roslyn says its output is.
        var console = Write("Console/Console.vbproj", Project(
            "<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><RootNamespace></RootNamespace>"));
        Write("Console/Program.vb", """
            Module Program
                Sub Main()
                    System.Console.WriteLine("hello-from-console")
                End Sub
            End Module
            """);
        Write("Forms/Forms.vbproj", Project(
            "<TargetFramework>net8.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><RootNamespace></RootNamespace>"));
        Write("Forms/Thing.vb", "Public Class Thing\n    Public Property L As System.Windows.Forms.Label\nEnd Class\n");
        var solution = Write("Mixed.sln", SolutionOf(("Console", @"Console\Console.vbproj"), ("Forms", @"Forms\Forms.vbproj")));

        // What the services of one IDE window share.
        var session = new WindowsDesktopCompatibilitySession();
        using var language = new RoslynLanguageService { Compatibility = session };
        await language.OpenSolutionAsync(solution);

        var builder = new MsBuildBuildService { Compatibility = session };
        var output = new StringBuilder();
        builder.OutputReceived += (_, line) => { lock (output) output.AppendLine(line); };
        var result = await builder.BuildAsync(console);
        Assert.True(result.Succeeded, output.ToString());

        var expected = Path.Combine(_root, "Console", "bin", "basalt", "Debug", "net10.0", "Console.dll");
        Assert.Equal(expected, Path.GetFullPath(Assert.IsType<string>(result.OutputAssemblyPath)));
        Assert.False(Directory.Exists(Path.Combine(_root, "Console", "bin", "Debug")));

        // Roslyn loaded the same project with the same properties.
        var roslynProject = Assert.Single(
            Assert.IsAssignableFrom<Microsoft.CodeAnalysis.Solution>(language.CurrentSolution).Projects,
            p => p.Name == "Console");
        Assert.Equal(expected, Path.GetFullPath(Assert.IsType<string>(roslynProject.OutputFilePath)));
        var formsProject = Assert.Single(language.CurrentSolution!.Projects, p => p.Name == "Forms");
        Assert.Contains("net10.0", Path.GetFullPath(Assert.IsType<string>(formsProject.OutputFilePath)));

        // And it runs from there: "dotnet run --no-build" with the same properties.
        using var run = new ProcessRunService { Compatibility = session };
        var seen = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lines = new List<string>();
        run.OutputReceived += (_, line) =>
        {
            lock (lines) lines.Add(line);
            if (line.Contains("hello-from-console", StringComparison.Ordinal)) seen.TrySetResult(line);
        };
        await run.StartAsync(console);
        var finished = await Task.WhenAny(seen.Task, Task.Delay(TimeSpan.FromSeconds(90)));
        string transcript;
        lock (lines) transcript = string.Join(Environment.NewLine, lines);
        Assert.True(finished == seen.Task, transcript);
        Assert.Equal("hello-from-console", (await seen.Task).Trim());
    }

    private string WriteFormsLibrary()
    {
        var lib = Write("Lib/Lib.vbproj", Project(
            "<TargetFramework>net8.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><RootNamespace></RootNamespace>"));
        Write("Lib/Thing.vb", "Public Class Thing\n    Public Property L As System.Windows.Forms.Label\nEnd Class\n");
        return lib;
    }

    [Fact]
    public async Task TestServiceBuildsWithTheSamePropertiesAsTheIdeBuild()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows builds the projects with the real WinForms.");
        WindowsDesktopSamples.RequireSources();
        var lib = WriteFormsLibrary();

        // "dotnet test" restores (and, for a test project, builds): without the
        // properties the SDK refuses the Windows framework (NETSDK1100) off
        // Windows and the restore writes to obj/, not where the build looks.
        var tests = new Basalt.Workspace.Testing.DotnetTestService();
        var text = new StringBuilder();
        tests.OutputReceived += (_, chunk) => text.Append(chunk);
        await tests.DiscoverAsync(lib);

        Assert.DoesNotContain("NETSDK1100", text.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_root, "Lib", "obj", "basalt", "project.assets.json")), text.ToString());
        Assert.False(File.Exists(Path.Combine(_root, "Lib", "obj", "project.assets.json")));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task CleanInTheIdeUsesThePropertiesOfTheBuildSoRebuildWorks()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows builds the projects with the real WinForms.");
        WindowsDesktopSamples.RequireSources();
        var lib = WriteFormsLibrary();
        var dll = Path.Combine(_root, "Lib", "bin", "basalt", "Debug", "net10.0", "Lib.dll");

        using var viewModel = new Basalt.Shell.ViewModels.MainWindowViewModel();
        await viewModel.OpenSolutionAsync(lib);
        await viewModel.BuildCommand.ExecuteAsync(null);
        Assert.True(File.Exists(dll), viewModel.StatusMessage);

        // "dotnet clean" of a net8.0-windows project fails off Windows (NETSDK1100)
        // unless it gets the properties the build had: that failed every Rebuild.
        await viewModel.CleanAsync();
        Assert.Equal(
            Basalt.Core.Localization.Localizer.Get(Basalt.Core.Localization.StringKeys.StatusCleaned),
            viewModel.StatusMessage);
        Assert.False(File.Exists(dll));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task MissingBasaltSourcesAreExplainedInTheOutputInsteadOfALoneNetsdk1100()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows builds the projects with the real WinForms.");
        var lib = WriteFormsLibrary();
        var previous = Environment.GetEnvironmentVariable(BasaltSourceRoot.EnvironmentVariable);
        var previousPackages = Environment.GetEnvironmentVariable(BasaltWinFormsPackages.SourceVariable);
        // A folder without Basalt.Forms and without the .nupkg files: the same as
        // an IDE installed with neither its sources nor the shipped packages.
        Environment.SetEnvironmentVariable(BasaltSourceRoot.EnvironmentVariable, _root);
        Environment.SetEnvironmentVariable(BasaltWinFormsPackages.SourceVariable, _root);
        try
        {
            using var viewModel = new Basalt.Shell.ViewModels.MainWindowViewModel();
            await viewModel.OpenSolutionAsync(lib);

            var expected = Basalt.Core.Localization.Localizer.Get(
                Basalt.Core.Localization.StringKeys.BuildWindowsDesktopSourcesMissing,
                BasaltSourceRoot.EnvironmentVariable);
            Assert.Contains(BasaltSourceRoot.EnvironmentVariable, expected, StringComparison.Ordinal);
            Assert.Equal(1, viewModel.BuildOutput.Count(l => l == expected));
        }
        finally
        {
            Environment.SetEnvironmentVariable(BasaltSourceRoot.EnvironmentVariable, previous);
            Environment.SetEnvironmentVariable(BasaltWinFormsPackages.SourceVariable, previousPackages);
        }
    }

    [Fact]
    public async Task MultiTargetedWindowsProjectBuildsAsOneNet10Project()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows builds the projects with the real WinForms.");
        WindowsDesktopSamples.RequireSources();

        // net48 would need its reference assemblies; the retargeting replaces
        // the whole TargetFrameworks list, so it must never be asked for them,
        // and the build must not stay a cross-targeting (outer) build.
        var lib = Write("Multi/Multi.vbproj", Project(
            "<TargetFrameworks>net48;net8.0-windows</TargetFrameworks><UseWindowsForms>true</UseWindowsForms><RootNamespace></RootNamespace>"));
        Write("Multi/Thing.vb", "Public Class Thing\n    Public Property L As System.Windows.Forms.Label\nEnd Class\n");

        var builder = new MsBuildBuildService();
        var output = new StringBuilder();
        builder.OutputReceived += (_, line) => { lock (output) output.AppendLine(line); };
        var result = await builder.BuildAsync(lib);

        Assert.True(result.Succeeded, output.ToString());
        Assert.True(File.Exists(Path.Combine(_root, "Multi", "bin", "basalt", "Debug", "net10.0", "Multi.dll")),
            output.ToString());
        Assert.False(Directory.Exists(Path.Combine(_root, "Multi", "bin", "basalt", "Debug", "net48")));
    }

    private static async Task<(int ExitCode, string Output)> RunDotnetAsync(string directory, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + await error);
    }
}
