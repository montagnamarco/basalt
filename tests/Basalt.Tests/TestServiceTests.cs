using Basalt.Core.Services;
using Basalt.Workspace.Testing;

using TestResult = Basalt.Core.Services.TestResult;

namespace Basalt.Tests;

/// <summary>Reading what the test runner reports.</summary>
public class DotnetTestParsingTests
{
    [Fact]
    public void ReadsTheNamesFromADiscoveryListing()
    {
        const string output = """
              Determining projects to restore...
              VbTests -> /tmp/vbtest/bin/Debug/net10.0/VbTests.dll
            The following tests are available:
                Sample.CalculatorTests.AddsTwoNumbers
                Sample.CalculatorTests.SubtractsTwoNumbers
            """;

        var names = DotnetTestService.ParseDiscovered(output);

        Assert.Equal(2, names.Count);
        Assert.Contains("Sample.CalculatorTests.AddsTwoNumbers", names);
    }

    [Fact]
    public void IgnoresTheHeadingWhateverLanguageItIsIn()
    {
        // The heading is translated; matching on it would work in English and
        // fail in Italian.
        const string output = """
            Sono disponibili i test seguenti:
                Sample.CalculatorTests.AddsTwoNumbers
            """;

        Assert.Single(DotnetTestService.ParseDiscovered(output));
    }

    [Fact]
    public void IgnoresTheBuildOutputPathsAmongTheLines()
    {
        // A built assembly's path is indented and contains dots too.
        const string output = """
              VbTests -> /tmp/vbtest/bin/Debug/net10.0/VbTests.dll
                Sample.CalculatorTests.AddsTwoNumbers
            """;

        var names = DotnetTestService.ParseDiscovered(output);

        Assert.Single(names);
        Assert.DoesNotContain(names, n => n.Contains(".dll"));
    }

    [Fact]
    public void ReadsEachCaseOfATheory()
    {
        // A theory is listed once per case, with its arguments in brackets.
        // Rejecting lines with spaces lost every parameterised test.
        const string output = """
            The following tests are available:
                CalculatorTests.AddsTwoNumbers
                CalculatorTests.AddsTheseToo(a: 1, b: 1, expected: 2)
                CalculatorTests.AddsTheseToo(a: 2, b: 3, expected: 5)
            """;

        var names = DotnetTestService.ParseDiscovered(output);

        Assert.Equal(3, names.Count);
        Assert.Contains(names, n => n.Contains("a: 2, b: 3"));
    }

    [Fact]
    public void StillIgnoresProse()
    {
        // A translated heading is indented in some locales and has dots.
        const string output = """
                Some sentence. With dots.
                CalculatorTests.Adds
            """;

        Assert.Single(DotnetTestService.ParseDiscovered(output));
    }

    [Fact]
    public void ReadsNothingFromAnEmptyListing()
    {
        Assert.Empty(DotnetTestService.ParseDiscovered(""));
    }

    [Fact]
    public void ReadsTheOutcomesFromAReport()
    {
        var results = DotnetTestService.ParseReport(SampleReport);

        Assert.Equal(3, results.Count);
        Assert.Equal(TestOutcome.Passed,
            results.Single(r => r.FullyQualifiedName.EndsWith("AddsTwoNumbers")).Outcome);
        Assert.Equal(TestOutcome.Failed,
            results.Single(r => r.FullyQualifiedName.EndsWith("ThisOneFails")).Outcome);
    }

    [Fact]
    public void ReadsWhyATestFailed()
    {
        var failed = DotnetTestService.ParseReport(SampleReport)
            .Single(r => r.Outcome == TestOutcome.Failed);

        Assert.Contains("Values differ", failed.Message);
    }

    [Fact]
    public void ReadsWhereATestFailed()
    {
        // Without this the user is told a test failed but not where to look.
        var failed = DotnetTestService.ParseReport(SampleReport)
            .Single(r => r.Outcome == TestOutcome.Failed);

        Assert.Equal("/private/tmp/vbtest/CalculatorTests.vb", failed.FilePath);
        Assert.Equal(18, failed.Line);
    }

    [Fact]
    public void ReadsHowLongEachTestTook()
    {
        var results = DotnetTestService.ParseReport(SampleReport);

        Assert.All(results, r => Assert.True(r.Duration >= TimeSpan.Zero));
        Assert.Contains(results, r => r.Duration > TimeSpan.Zero);
    }

    [Fact]
    public void ReadsASkippedTest()
    {
        var results = DotnetTestService.ParseReport(SampleReport);

        Assert.Equal(TestOutcome.Skipped,
            results.Single(r => r.FullyQualifiedName.EndsWith("NotRunHere")).Outcome);
    }

    [Fact]
    public void SurvivesAReportItCannotRead()
    {
        // A run killed part-way leaves a truncated file.
        Assert.Empty(DotnetTestService.ParseReport("<TestRun><Unclosed>"));
    }

    [Theory]
    [InlineData("   at A.B() in /src/File.vb:line 42", "/src/File.vb", 42)]
    [InlineData("   at A.B() in /src/File.vb:riga 42", "/src/File.vb", 42)]
    [InlineData("   at A.B()", null, 0)]
    [InlineData("", null, 0)]
    public void ReadsALocationFromAStackFrame(string frame, string? file, int line)
    {
        // The word before the number is translated; the digits are not.
        var (readFile, readLine) = DotnetTestService.LocationOf(frame);

        Assert.Equal(file, readFile);
        Assert.Equal(line, readLine);
    }

    [Fact]
    public void TakesTheFirstFrameThatNamesAFile()
    {
        // The frames above belong to the assertion library, which is not
        // where the user wants to be taken.
        const string stack = """
               at Xunit.Assert.Equal(Int32 expected, Int32 actual)
               at Sample.CalculatorTests.ThisOneFails() in /src/CalculatorTests.vb:line 18
            """;

        var (file, line) = DotnetTestService.LocationOf(stack);

        Assert.Equal("/src/CalculatorTests.vb", file);
        Assert.Equal(18, line);
    }

    [Fact]
    public void BuildsAFilterThatMatchesWholeNames()
    {
        // A prefix match would run "AddsTwoNegatives" as well as "AddsTwo".
        var filter = DotnetTestService.FilterFor(["A.B.One", "A.B.Two"]);

        Assert.Equal("FullyQualifiedName=A.B.One|FullyQualifiedName=A.B.Two", filter);
    }

    [Fact]
    public void SplitsATestNameIntoItsClassAndMethod()
    {
        var test = new TestCase("Sample.CalculatorTests.AddsTwoNumbers", "/p.vbproj");

        Assert.Equal("AddsTwoNumbers", test.DisplayName);
        Assert.Equal("Sample.CalculatorTests", test.ClassName);
    }

    [Fact]
    public void CopesWithATestNameThatHasNoClass()
    {
        var test = new TestCase("Standalone", "/p.vbproj");

        Assert.Equal("Standalone", test.DisplayName);
        Assert.Equal("", test.ClassName);
    }

    /// <summary>A report of the shape the runner produces.</summary>
    private const string SampleReport = """
        <?xml version="1.0" encoding="UTF-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testName="Sample.CalculatorTests.AddsTwoNumbers"
                            outcome="Passed" duration="00:00:00.0074428" />
            <UnitTestResult testName="Sample.CalculatorTests.NotRunHere"
                            outcome="NotExecuted" duration="00:00:00" />
            <UnitTestResult testName="Sample.CalculatorTests.ThisOneFails"
                            outcome="Failed" duration="00:00:00.0014959">
              <Output>
                <ErrorInfo>
                  <Message>Assert.Equal() Failure: Values differ</Message>
                  <StackTrace>   at Sample.CalculatorTests.ThisOneFails() in /private/tmp/vbtest/CalculatorTests.vb:line 18</StackTrace>
                </ErrorInfo>
              </Output>
            </UnitTestResult>
          </Results>
        </TestRun>
        """;
}
