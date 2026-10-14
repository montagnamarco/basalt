using Basalt.Core.Services;
using Basalt.Workspace.Testing;

namespace Basalt.Tests;

/// <summary>
/// The lines a real build and a real listing printed, read by the parser.
/// </summary>
/// <remarks>
/// The fixtures were captured from a VB xUnit project with one obsolete-call
/// warning, built clean with "dotnet test --list-tests" and "dotnet build -v m".
/// Red-proof, one defect at a time: reading every four-space line as a name
/// (no heading anchor) fails ReadsTheNamesOfARealListingThatHasAWarning and
/// IgnoresIndentedLinesThatComeBeforeTheHeading; reading the build's own
/// summary as tests fails ReadsNoTestsFromARealBuildSummary; dropping names
/// shaped like a count fails KeepsATestNamedLikeACountLine; a heading that
/// does not accept the full-width colon fails ReadsTheListAfterAFullWidthColonHeading.
/// </remarks>
public class TestListingFixtureTests
{
    /// <summary>A real "dotnet test --list-tests" run of a project with a warning.</summary>
    private const string RealListing = """
  Individuazione dei progetti da ripristinare...
  /work/WarnTests/WarnTests.vbproj ripristinato (in 127 ms).
/work/WarnTests/Tests.vb(12,29): warning BC40000: 'Private Shared Function OldAdd(a As Integer, b As Integer) As Integer' è obsoleto: 'Use the other method.'. [/work/WarnTests/WarnTests.vbproj]
  WarnTests -> /work/WarnTests/bin/Debug/net10.0/WarnTests.dll
Esecuzione dei test per /work/WarnTests/bin/Debug/net10.0/WarnTests.dll (.NETCoreApp,Version=v10.0)
Sono disponibili i test seguenti:
    Sample.CalculatorTests.AddsTwoNumbers
    Sample.CalculatorTests.AddsTheseNumbersToo(a: 1, b: 1, expected: 2)
    Sample.CalculatorTests.AddsTheseNumbersToo(a: -1, b: 1, expected: 0)
    Sample.CalculatorTests.OpensWhenTheFileIsEmpty
""";

    /// <summary>A real "dotnet build -v m" of the same project: its summary is indented by four.</summary>
    private const string RealBuild = """
  Individuazione dei progetti da ripristinare...
  /work/WarnTests/WarnTests.vbproj ripristinato (in 114 ms).
/work/WarnTests/Tests.vb(12,29): warning BC40000: 'Private Shared Function OldAdd(a As Integer, b As Integer) As Integer' è obsoleto: 'Use the other method.'. [/work/WarnTests/WarnTests.vbproj]
  WarnTests -> /work/WarnTests/bin/Debug/net10.0/WarnTests.dll

Compilazione completata.

/work/WarnTests/Tests.vb(12,29): warning BC40000: 'Private Shared Function OldAdd(a As Integer, b As Integer) As Integer' è obsoleto: 'Use the other method.'. [/work/WarnTests/WarnTests.vbproj]
    Avvisi: 1
    Errori: 0

Tempo trascorso 00:00:00.67
""";

    [Fact]
    public void ReadsTheNamesOfARealListingThatHasAWarning()
    {
        Assert.Equal(
            [
                "Sample.CalculatorTests.AddsTwoNumbers",
                "Sample.CalculatorTests.AddsTheseNumbersToo(a: 1, b: 1, expected: 2)",
                "Sample.CalculatorTests.AddsTheseNumbersToo(a: -1, b: 1, expected: 0)",
                "Sample.CalculatorTests.OpensWhenTheFileIsEmpty"
            ],
            DotnetTestService.ParseDiscovered(RealListing));
    }

    [Fact]
    public void ReadsNoTestsFromARealBuildSummary()
    {
        // "    Avvisi: 1" and "    Errori: 0" are indented by four, as names are.
        Assert.Empty(DotnetTestService.ParseDiscovered(RealBuild));
    }

    [Fact]
    public void IgnoresIndentedLinesThatComeBeforeTheHeading()
    {
        // Printed before the list, not part of it: an indent of four does not make it a test.
        const string output = """
                Compiling the tests
            The following tests are available:
                Adds
            """;

        Assert.Equal(["Adds"], DotnetTestService.ParseDiscovered(output));
    }

    [Fact]
    public void KeepsATestNamedLikeACountLine()
    {
        // "Retries: 3" is a test's name. The build's count lines come after no
        // heading, so the list is the only place a name is read from.
        var listing = RealListing + "\n    Retries: 3\n";

        Assert.Equal(
            [
                "Sample.CalculatorTests.AddsTwoNumbers",
                "Sample.CalculatorTests.AddsTheseNumbersToo(a: 1, b: 1, expected: 2)",
                "Sample.CalculatorTests.AddsTheseNumbersToo(a: -1, b: 1, expected: 0)",
                "Sample.CalculatorTests.OpensWhenTheFileIsEmpty",
                "Retries: 3"
            ],
            DotnetTestService.ParseDiscovered(listing));
    }

    [Fact]
    public void ReadsTheListAfterAFullWidthColonHeading()
    {
        // Some translations end the heading with the full-width colon.
        const string output = "Les tests disponibles\uFF1A\n    Passes\n";

        Assert.Equal(["Passes"], DotnetTestService.ParseDiscovered(output));
    }
}
