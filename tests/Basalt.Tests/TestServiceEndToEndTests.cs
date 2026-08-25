using Basalt.Core.Services;
using Basalt.Workspace.Testing;
using TestResult = Basalt.Core.Services.TestResult;

namespace Basalt.Tests;

/// <summary>
/// A real Visual Basic test project, discovered and run.
///
/// The parsing tests use samples; this one builds an actual project and reads
/// what the runner really produced, which is the only way to know the two
/// agree.
/// </summary>
[Collection("PackageBuild")]
public sealed class TestServiceEndToEndTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-testrun", Guid.NewGuid().ToString("N"));

    private string ProjectPath => Path.Combine(_root, "VbTests.vbproj");

    private readonly DotnetTestService _service = new();

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);

        await File.WriteAllTextAsync(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit.v3" Version="3.2.2" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
              </ItemGroup>
            </Project>
            """);

        // One of each outcome, so every branch of the reading is exercised
        // against what the runner actually writes.
        await File.WriteAllTextAsync(Path.Combine(_root, "CalculatorTests.vb"), """
            Imports Xunit

            Namespace Sample
                Public Class CalculatorTests

                    <Fact>
                    Public Sub AddsTwoNumbers()
                        Assert.Equal(4, 2 + 2)
                    End Sub

                    <Fact>
                    Public Sub ThisOneFails()
                        Assert.Equal(5, 2 + 2)
                    End Sub

                    <Fact(Skip:="Deliberately skipped.")>
                    Public Sub NotRunHere()
                        Assert.True(False)
                    End Sub

                End Class
            End Namespace
            """);
    }

    [Fact]
    public async Task FindsTheTestsAVisualBasicProjectDeclares()
    {
        var tests = await _service.DiscoverAsync(ProjectPath);

        Assert.Equal(3, tests.Count);
        Assert.Contains(tests, t => t.FullyQualifiedName == "Sample.CalculatorTests.AddsTwoNumbers");
    }

    [Fact]
    public async Task GroupsWhatItFoundByClass()
    {
        var tests = await _service.DiscoverAsync(ProjectPath);

        Assert.All(tests, t => Assert.Equal("Sample.CalculatorTests", t.ClassName));
    }

    [Fact]
    public async Task RunsThemAndReportsEachOutcome()
    {
        var results = await _service.RunAsync(ProjectPath);

        Assert.Equal(3, results.Count);

        Assert.Equal(TestOutcome.Passed, Outcome(results, "AddsTwoNumbers"));
        Assert.Equal(TestOutcome.Failed, Outcome(results, "ThisOneFails"));
        Assert.Equal(TestOutcome.Skipped, Outcome(results, "NotRunHere"));
    }

    [Fact]
    public async Task SaysWhereAFailingTestFailed()
    {
        // Without this the user is told something failed but not where.
        var results = await _service.RunAsync(ProjectPath);

        var failed = results.Single(r => r.Outcome == TestOutcome.Failed);

        Assert.NotNull(failed.Message);
        Assert.EndsWith("CalculatorTests.vb", failed.FilePath);
        Assert.True(failed.Line > 0, "The failing line was not found.");
    }

    [Fact]
    public async Task RunsOnlyTheTestsAskedFor()
    {
        var filter = DotnetTestService.FilterFor(["Sample.CalculatorTests.AddsTwoNumbers"]);

        var results = await _service.RunAsync(ProjectPath, filter);

        var only = Assert.Single(results);

        Assert.Equal("Sample.CalculatorTests.AddsTwoNumbers", only.FullyQualifiedName);
    }

    [Fact]
    public async Task RunsTheFailedOnesAgain()
    {
        // What "Run Failed" does: the names come from the previous run.
        var first = await _service.RunAsync(ProjectPath);

        var failed = first
            .Where(r => r.Outcome == TestOutcome.Failed)
            .Select(r => r.FullyQualifiedName)
            .ToList();

        var again = await _service.RunAsync(ProjectPath, DotnetTestService.FilterFor(failed));

        Assert.Equal(failed.Count, again.Count);
        Assert.All(again, r => Assert.Equal(TestOutcome.Failed, r.Outcome));
    }

    private static TestOutcome Outcome(IReadOnlyList<TestResult> results, string name) =>
        results.Single(r => r.FullyQualifiedName.EndsWith(name, StringComparison.Ordinal)).Outcome;

    public ValueTask DisposeAsync()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// The test project template, built and run.
///
/// A template that does not produce a working project is worse than none:
/// the user's first experience of the feature is a project that fails.
/// </summary>
[Collection("PackageBuild")]
public sealed class TestProjectTemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-testtemplate", Guid.NewGuid().ToString("N"));

    public TestProjectTemplateTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task TheTemplateProducesTestsThatAreFoundAndPass()
    {
        var result = await Basalt.Designer.SolutionTemplates.CreateAsync(
            _root, "SampleTests", Basalt.Designer.ProjectTemplate.TestProject);

        var service = new DotnetTestService();

        var discovered = await service.DiscoverAsync(result.ProjectPath);

        // One Fact and a Theory with three cases.
        Assert.True(discovered.Count >= 2, $"Only {discovered.Count} tests were found.");

        var results = await service.RunAsync(result.ProjectPath);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(TestOutcome.Passed, r.Outcome));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
