using Basalt.Core.Services;
using Basalt.Workspace.Testing;

using TestResult = Basalt.Core.Services.TestResult;

namespace Basalt.Tests;

/// <summary>
/// Real MSTest, NUnit, MSTest.Sdk and xUnit projects, discovered and run.
/// </summary>
/// <remarks>
/// The unit tests read sample listings; these build the frameworks' own
/// projects and read what the runners really print, which is the only way to
/// know that the names discovery gives are the ones the runner filters on.
/// Red-proof, one defect at a time: skipping the class lookup in
/// TestNameResolver.Qualify fails the three FindsTheTests... tests; running
/// the MSTest.Sdk program during discovery fails FindsTheTestsOfAnMSTestSdk...
/// (the marker file is written); dropping the report's class fails
/// RunsAnMSTestSdkProject...; restoring the method-free filter fails
/// RunsOneCaseOfAnXUnitTheory... (the case matched no test).
/// </remarks>
[Collection("PackageBuild")]
public sealed class TestDiscoveryFixTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-testexplorer", Guid.NewGuid().ToString("N"));

    private readonly DotnetTestService _service = new(new Basalt.Workspace.BuildGate());

    public TestDiscoveryFixTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task FindsMSTestTestsByTheirQualifiedNames()
    {
        var project = Project("MsTest", VSTestProject("""
            <PackageReference Include="MSTest.TestFramework" Version="3.9.3" />
            <PackageReference Include="MSTest.TestAdapter" Version="3.9.3" />
            """), MSTestCode);

        var names = (await _service.DiscoverAsync(project)).Select(t => t.FullyQualifiedName).ToList();

        // MSTest lists "Passes" and "Adds (1,1,2)"; the class is what makes them runnable.
        Assert.Contains("Sample.CalcTests.Passes", names);
        Assert.Contains("Sample.CalcTests.Adds (1,1,2)", names);
        Assert.Contains("Sample.CalcTests.Adds (2,2,5)", names);
        Assert.DoesNotContain("Passes", names);
    }

    [Fact]
    public async Task FindsNUnitTestsByTheirQualifiedNames()
    {
        var project = Project("NUnitCalc", VSTestProject("""
            <PackageReference Include="NUnit" Version="4.3.2" />
            <PackageReference Include="NUnit3TestAdapter" Version="5.0.0" />
            """), NUnitCode);

        var names = (await _service.DiscoverAsync(project)).Select(t => t.FullyQualifiedName).ToList();

        Assert.Contains("Sample.CalcTests.Passes", names);
        Assert.Contains("Sample.CalcTests.Adds(1,1,2)", names);
    }

    [Fact]
    public async Task FindsTheTestsOfAnMSTestSdkProjectWithoutRunningThem()
    {
        // The marker is written only if the test runs. Discovery must list the
        // tests, and the platform's own "--list-tests" through "dotnet test" would run them.
        var marker = Path.Combine(_root, "ran.marker");

        var project = Project("MsTestSdk", """
            <Project Sdk="MSTest.Sdk/3.9.3">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace></RootNamespace></PropertyGroup>
            </Project>
            """, MSTestSdkCode(marker));

        var names = (await _service.DiscoverAsync(project)).Select(t => t.FullyQualifiedName).ToList();

        Assert.Contains("Sample.SdkTests.A", names);
        Assert.Contains("Sample.SdkTests.B (1,2)", names);
        Assert.False(File.Exists(marker), "Discovery ran the tests.");
    }

    [Fact]
    public async Task RunsAnMSTestSdkProjectAndNamesEachResult()
    {
        var project = Project("MsTestSdkRun", """
            <Project Sdk="MSTest.Sdk/3.9.3">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace></RootNamespace></PropertyGroup>
            </Project>
            """, MSTestSdkCode(Path.Combine(_root, "ran-again.marker")));

        var results = await _service.RunAsync(project);

        // The platform's report carries the class in its definitions, so each result
        // has the name discovery gave it.
        Assert.Equal(TestOutcome.Passed, Outcome(results, "Sample.SdkTests.A"));
        Assert.Equal(TestOutcome.Passed, Outcome(results, "Sample.SdkTests.B (1,2)"));
    }

    [Fact]
    public async Task RunsOneCaseOfAnMSTestDataRowAndReportsIt()
    {
        var project = Project("MsTestCase", VSTestProject("""
            <PackageReference Include="MSTest.TestFramework" Version="3.9.3" />
            <PackageReference Include="MSTest.TestAdapter" Version="3.9.3" />
            """), MSTestCode);

        var results = await _service.RunAsync(project,
            DotnetTestService.FilterFor(["Sample.CalcTests.Adds (1,1,2)"]));

        // The case the user picked is reported under its own name.
        Assert.Equal(TestOutcome.Passed, Outcome(results, "Sample.CalcTests.Adds (1,1,2)"));
    }

    [Fact]
    public async Task RunsOneCaseOfAnXUnitTheoryAndReportsIt()
    {
        // The defect: a case's name was used as the filter, which the runner
        // matched against no test, so the case never ran.
        // The project is not named "XUnit": the adapter then listed no tests
        // at all, so the fixture's name must differ from the framework's.
        var project = Project("XUnitCalc", """
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
            """, XUnitCode);

        const string caseName = "Sample.CalculatorTests.AddsTheseNumbersToo(a: 1, b: 1, expected: 2)";

        var discovered = await _service.DiscoverAsync(project);
        Assert.Contains(discovered, t => t.FullyQualifiedName == caseName);

        var output = new List<string>();
        _service.OutputReceived += (_, text) => output.Add(text);

        var results = await _service.RunAsync(project, DotnetTestService.FilterFor([caseName]));

        Assert.Equal(TestOutcome.Passed, Outcome(results, caseName));

        // The build, the case run that matched nothing, and the whole-method run: one build.
        Assert.Equal(3, output.Count);
    }

    [Fact]
    public async Task RunsOneNUnitTestCaseAndReportsExactlyThatCase()
    {
        // NUnit's own name for a case carries its arguments; the filter must still
        // select it, and the run must report that case and nothing else.
        var project = Project("NUnitCase", VSTestProject("""
            <PackageReference Include="NUnit" Version="4.3.2" />
            <PackageReference Include="NUnit3TestAdapter" Version="5.0.0" />
            """), NUnitCode);

        // Two cases of Adds: the one picked must run, and the other must not.
        const string caseName = "Sample.CalcTests.Adds(1,1,2)";
        const string otherCase = "Sample.CalcTests.Adds(2,2,5)";

        var discovered = await _service.DiscoverAsync(project);
        Assert.Contains(discovered, t => t.FullyQualifiedName == caseName);
        Assert.Contains(discovered, t => t.FullyQualifiedName == otherCase);

        var output = new List<string>();
        _service.OutputReceived += (_, text) => output.Add(text);

        var results = await _service.RunAsync(project, DotnetTestService.FilterFor([caseName]));

        // The build and the one run: a case that matched is not run again.
        Assert.Equal(2, output.Count);

        var only = Assert.Single(results);
        Assert.Equal(caseName, only.FullyQualifiedName);
        Assert.Equal(TestOutcome.Passed, only.Outcome);
    }

    [Fact]
    public async Task ASelectionThatMatchesNothingIsRunOnce()
    {
        // Only a case filter is retried with the whole method; a filter that
        // names no test is not, so there is one build and one run.
        var project = Project("NoMatch", VSTestProject("""
            <PackageReference Include="MSTest.TestFramework" Version="3.9.3" />
            <PackageReference Include="MSTest.TestAdapter" Version="3.9.3" />
            """), MSTestCode);

        var output = new List<string>();
        _service.OutputReceived += (_, text) => output.Add(text);

        var results = await _service.RunAsync(project, "FullyQualifiedName=Sample.CalcTests.NoSuchTest");

        Assert.Empty(results);
        Assert.Equal(2, output.Count);
    }

    [Fact]
    public async Task ABuildThatFailsEndsTheRunWithItsErrorsShownOnce()
    {
        // The project does not compile. The run must stop after the build: one
        // output, the errors, and no test run and no second build behind them.
        var project = Project("BrokenBuild", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
            </Project>
            """, "Public Class (\n");

        var output = new List<string>();
        _service.OutputReceived += (_, text) => output.Add(text);

        var results = await _service.RunAsync(project, DotnetTestService.FilterFor(["Sample.T.Adds(1)"]));

        Assert.Empty(results);
        var only = Assert.Single(output);
        Assert.Contains("error", only, StringComparison.OrdinalIgnoreCase);

        // The last process started was the build: no test run followed it. A run
        // of a project that did not build prints nothing, so the process is the evidence.
        Assert.Equal("build", _service.LastStartInfo!.ArgumentList[0]);
    }

    private static TestOutcome Outcome(IReadOnlyList<TestResult> results, string name)
    {
        var match = results.SingleOrDefault(r => r.FullyQualifiedName == name);

        Assert.True(match is not null, $"The run reported no result named \"{name}\". Reported: "
            + string.Join("; ", results.Select(r => r.FullyQualifiedName)));

        return match!.Outcome;
    }

    private string Project(string name, string projectXml, string code)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, name + ".vbproj");
        File.WriteAllText(path, projectXml);
        File.WriteAllText(Path.Combine(folder, "Tests.vb"), code);

        return path;
    }

    private static string VSTestProject(string packages) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <RootNamespace></RootNamespace>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
            {packages}
          </ItemGroup>
        </Project>
        """;

    private const string MSTestCode = """
        Imports Microsoft.VisualStudio.TestTools.UnitTesting

        Namespace Sample
            <TestClass>
            Public Class CalcTests
                <TestMethod>
                Public Sub Passes()
                    Assert.AreEqual(4, 2 + 2)
                End Sub

                <DataTestMethod>
                <DataRow(1, 1, 2)>
                <DataRow(2, 2, 5)>
                Public Sub Adds(a As Integer, b As Integer, expected As Integer)
                    Assert.AreEqual(expected, a + b)
                End Sub
            End Class
        End Namespace
        """;

    private const string NUnitCode = """
        Imports NUnit.Framework

        Namespace Sample
            <TestFixture>
            Public Class CalcTests
                <Test>
                Public Sub Passes()
                    Assert.That(2 + 2, [Is].EqualTo(4))
                End Sub

                <TestCase(1, 1, 2)>
                <TestCase(2, 2, 5)>
                Public Sub Adds(a As Integer, b As Integer, expected As Integer)
                    Assert.That(a + b, [Is].EqualTo(expected))
                End Sub
            End Class
        End Namespace
        """;

    /// <summary>
    /// A program that writes <paramref name="marker"/> when one of its tests runs.
    /// </summary>
    private static string MSTestSdkCode(string marker) => $"""
        Imports Microsoft.VisualStudio.TestTools.UnitTesting

        Namespace Sample
            <TestClass>
            Public Class SdkTests
                <TestMethod>
                Public Sub A()
                    System.IO.File.WriteAllText("{marker}", "")
                End Sub

                <DataTestMethod>
                <DataRow(1, 2)>
                Public Sub B(x As Integer, y As Integer)
                End Sub
            End Class
        End Namespace
        """;

    private const string XUnitCode = """
        Imports Xunit

        Namespace Sample
            Public Class CalculatorTests
                <Fact>
                Public Sub AddsTwoNumbers()
                    Assert.Equal(4, 2 + 2)
                End Sub

                <Theory>
                <InlineData(1, 1, 2)>
                <InlineData(2, 3, 5)>
                <InlineData(-1, 1, 0)>
                Public Sub AddsTheseNumbersToo(a As Integer, b As Integer, expected As Integer)
                    Assert.Equal(expected, a + b)
                End Sub
            End Class
        End Namespace
        """;

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
