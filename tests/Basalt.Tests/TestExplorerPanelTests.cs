using Avalonia.Headless.XUnit;
using Basalt.Core.Services;
using Basalt.Shell.Controls;

using TestResult = Basalt.Core.Services.TestResult;

namespace Basalt.Tests;

/// <summary>The window that lists and runs the tests.</summary>
public class TestExplorerPanelTests
{
    private static TestCase Test(string name, string project = "/a/Tests.vbproj") =>
        new(name, project);

    private static IReadOnlyList<TestCase> ThreeTests =>
    [
        Test("Sample.CalculatorTests.Adds"),
        Test("Sample.CalculatorTests.Subtracts"),
        Test("Sample.ParserTests.Reads")
    ];

    [AvaloniaFact]
    public void SaysSoWhenNothingHasBeenDiscovered()
    {
        Assert.Contains("No tests", new TestExplorerPanel().Summary);
    }

    [AvaloniaFact]
    public void GroupsTestsByProjectAndClass()
    {
        var panel = new TestExplorerPanel();

        panel.Show(ThreeTests);

        var project = Assert.Single(panel.Roots);

        Assert.Equal("Tests", project.Name);
        Assert.Equal(2, project.Children.Count);
    }

    [AvaloniaFact]
    public void LeavesOffTheNamespaceTheTreeAlreadyShows()
    {
        var panel = new TestExplorerPanel();

        panel.Show(ThreeTests);

        Assert.Contains(panel.Roots[0].Children, c => c.Name == "CalculatorTests");
    }

    [AvaloniaFact]
    public void CountsWhatItFound()
    {
        var panel = new TestExplorerPanel();

        panel.Show(ThreeTests);

        Assert.Contains("3 tests", panel.Summary);
    }

    [AvaloniaFact]
    public void ShowsHowTheRunWent()
    {
        var panel = new TestExplorerPanel();
        panel.Show(ThreeTests);

        panel.ShowResults([
            new TestResult("Sample.CalculatorTests.Adds", TestOutcome.Passed, TimeSpan.FromMilliseconds(3)),
            new TestResult("Sample.CalculatorTests.Subtracts", TestOutcome.Failed, TimeSpan.Zero),
            new TestResult("Sample.ParserTests.Reads", TestOutcome.Skipped, TimeSpan.Zero)
        ]);

        Assert.Contains("1 passed", panel.Summary);
        Assert.Contains("1 failed", panel.Summary);
        Assert.Contains("1 skipped", panel.Summary);
    }

    [AvaloniaFact]
    public void MakesAClassWithAFailingTestLookFailed()
    {
        // The user must see it without opening the class.
        var panel = new TestExplorerPanel();
        panel.Show(ThreeTests);

        panel.ShowResults([
            new TestResult("Sample.CalculatorTests.Adds", TestOutcome.Passed, TimeSpan.Zero),
            new TestResult("Sample.CalculatorTests.Subtracts", TestOutcome.Failed, TimeSpan.Zero)
        ]);

        var calculator = panel.Roots[0].Children.Single(c => c.Name == "CalculatorTests");

        Assert.Equal(TestOutcome.Failed, calculator.EffectiveOutcome);
    }

    [AvaloniaFact]
    public void MakesAClassWhereEverythingPassedLookPassed()
    {
        var panel = new TestExplorerPanel();
        panel.Show(ThreeTests);

        panel.ShowResults([
            new TestResult("Sample.CalculatorTests.Adds", TestOutcome.Passed, TimeSpan.Zero),
            new TestResult("Sample.CalculatorTests.Subtracts", TestOutcome.Passed, TimeSpan.Zero)
        ]);

        var calculator = panel.Roots[0].Children.Single(c => c.Name == "CalculatorTests");

        Assert.Equal(TestOutcome.Passed, calculator.EffectiveOutcome);
    }

    [AvaloniaFact]
    public void RemembersWhichTestsFailed()
    {
        var panel = new TestExplorerPanel();
        panel.Show(ThreeTests);

        panel.ShowResults([
            new TestResult("Sample.CalculatorTests.Subtracts", TestOutcome.Failed, TimeSpan.Zero)
        ]);

        var failed = Assert.Single(panel.FailedTests);

        Assert.Equal("Sample.CalculatorTests.Subtracts", failed.FullyQualifiedName);
    }

    [AvaloniaFact]
    public void ShowsATestAsRunningWhileItRuns()
    {
        var panel = new TestExplorerPanel();
        panel.Show(ThreeTests);

        panel.MarkRunning(ThreeTests);

        Assert.Contains("Running", panel.Summary);
    }

    [AvaloniaFact]
    public void TreatsATestThatNeverReportedAsNotRun()
    {
        // A build failure leaves tests marked running with no result.
        var panel = new TestExplorerPanel();
        panel.Show(ThreeTests);

        panel.MarkRunning(ThreeTests);
        panel.ShowResults([]);

        Assert.All(panel.Roots[0].Leaves, leaf =>
            Assert.NotEqual(TestOutcome.Running, leaf.Outcome));
    }

    [AvaloniaFact]
    public void RunsEverythingWhenNothingIsSelected()
    {
        var panel = new TestExplorerPanel();
        panel.Show(ThreeTests);

        Assert.Equal(3, panel.SelectedTests.Count);
    }

    [AvaloniaFact]
    public void RunsAWholeClassWhenTheClassIsSelected()
    {
        var panel = new TestExplorerPanel();
        panel.Show(ThreeTests);

        var calculator = panel.Roots[0].Children.Single(c => c.Name == "CalculatorTests");

        panel.SelectForTests(calculator);

        Assert.Equal(2, panel.SelectedTests.Count);
    }

    [AvaloniaFact]
    public void RunsOneTestWhenOneIsSelected()
    {
        var panel = new TestExplorerPanel();
        panel.Show(ThreeTests);

        var leaf = panel.Roots[0].Leaves.First();

        panel.SelectForTests(leaf);

        Assert.Single(panel.SelectedTests);
    }

    [AvaloniaFact]
    public void ShowsWhyATestFailedWhenItIsSelected()
    {
        var panel = new TestExplorerPanel();
        panel.Show(ThreeTests);

        panel.ShowResults([
            new TestResult("Sample.CalculatorTests.Adds", TestOutcome.Failed, TimeSpan.Zero)
            {
                Message = "Assert.Equal() Failure",
                StackTrace = "   at Sample.CalculatorTests.Adds()"
            }
        ]);

        panel.SelectForTests(panel.Roots[0].Leaves.Single(l => l.Name == "Adds"));

        Assert.Contains("Assert.Equal", panel.Details);
    }

    [AvaloniaFact]
    public void ShowsNothingForATestThatPassed()
    {
        var panel = new TestExplorerPanel();
        panel.Show(ThreeTests);

        panel.ShowResults([
            new TestResult("Sample.CalculatorTests.Adds", TestOutcome.Passed, TimeSpan.Zero)
        ]);

        panel.SelectForTests(panel.Roots[0].Leaves.Single(l => l.Name == "Adds"));

        Assert.Equal("", panel.Details);
    }

    [AvaloniaFact]
    public void ShowsHowLongATestTook()
    {
        var panel = new TestExplorerPanel();
        panel.Show(ThreeTests);

        panel.ShowResults([
            new TestResult("Sample.CalculatorTests.Adds", TestOutcome.Passed,
                           TimeSpan.FromMilliseconds(42))
        ]);

        var leaf = panel.Roots[0].Leaves.Single(l => l.Name == "Adds");

        Assert.Contains("42 ms", leaf.Display);
    }

    [Theory]
    [InlineData(TestOutcome.Passed, IconKind.Run)]
    [InlineData(TestOutcome.Failed, IconKind.Error)]
    [InlineData(TestOutcome.Skipped, IconKind.Warning)]
    public void MarksEachOutcomeWithItsOwnIcon(TestOutcome outcome, IconKind expected)
    {
        Assert.Equal(expected, TestExplorerPanel.IconFor(outcome));
    }

    [AvaloniaFact]
    public void GroupsTestsOfSeveralProjectsSeparately()
    {
        var panel = new TestExplorerPanel();

        panel.Show([
            Test("A.Tests.One", "/a/First.vbproj"),
            Test("B.Tests.Two", "/b/Second.vbproj")
        ]);

        Assert.Equal(2, panel.Roots.Count);
    }
}
