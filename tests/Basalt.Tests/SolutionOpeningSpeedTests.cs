using System.Diagnostics;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// How long a solution takes to open.
/// </summary>
/// <remarks>
/// A declared threshold rather than a measurement in a report: the point is
/// not what the number is today but that nobody makes it much worse without
/// being told. Opening is where an IDE makes its first impression, and it is
/// also where work accumulates — a reference resolved here, an analyser
/// loaded there — with no single change ever looking expensive.
///
/// The budget is deliberately loose. It is a floor under a regression, not a
/// target: a machine under load should not fail a build.
/// </remarks>
public sealed class SolutionOpeningSpeedTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-open-speed", Guid.NewGuid().ToString("N"));

    public SolutionOpeningSpeedTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>What opening a solution of this size may cost.</summary>
    private const double Budget = 30_000;

    /// <summary>A project of many files, as a real one is.</summary>
    private string WriteProject(int files)
    {
        var project = Path.Combine(_root, "Big.vbproj");

        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        for (var i = 1; i <= files; i++)
        {
            var body = string.Join("\n", Enumerable.Range(1, 20).Select(m =>
                $"    Public Function F{m}(x As Integer) As Integer\n        Return x + {m}\n    End Function"));

            File.WriteAllText(
                Path.Combine(_root, $"File{i}.vb"),
                $"Module M{i}\n{body}\nEnd Module\n");
        }

        return project;
    }

    [Fact]
    public async Task OpeningAProjectOfFiftyFilesStaysUnderTheBudget()
    {
        var project = WriteProject(50);

        var service = new RoslynLanguageService();

        var watch = Stopwatch.StartNew();
        await service.OpenSolutionAsync(project);
        watch.Stop();

        Assert.True(watch.Elapsed.TotalMilliseconds < Budget,
            $"opening took {watch.Elapsed.TotalSeconds:0.0}s, over the {Budget / 1000:0}s budget");
    }

    [Fact]
    public async Task TheProjectIsActuallyLoadedAfterOpening()
    {
        // A fast open that loaded nothing would pass the test above and fail
        // the user: the speed only means anything if the work was done.
        var project = WriteProject(10);

        var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(project);

        // Something that can only be answered from a loaded compilation.
        Assert.Empty(await service.GetDiagnosticsAsync(Path.Combine(_root, "File1.vb")));
    }
}
