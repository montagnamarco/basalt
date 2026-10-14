using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Services;
using Basalt.Shell.Controls;
using Basalt.Workspace;
using Basalt.Workspace.Testing;

namespace Basalt.Tests;

/// <summary>
/// A rediscovery keeps what the user has in the window.
/// </summary>
/// <remarks>
/// Red-proof, one defect at a time: dropping the outcome and result copy fails
/// RediscoveryKeepsTheResultsTheOpenClassesAndTheSelection; dropping the
/// RememberExpansion call at the start of Show fails
/// RediscoveryKeepsAClassTheUserOpenedInAShownWindow (the row the user opened
/// is written to the model only at the end of Show, after the open state was read).
/// </remarks>
[Collection("PackageBuild")]
public class TestExplorerTreeFixTests
{
    private static TestCase Test(string name) => new(name, "/a/Tests.vbproj");

    private static IReadOnlyList<TestCase> Tests =>
    [
        Test("Sample.CalculatorTests.Adds"),
        Test("Sample.CalculatorTests.Subtracts"),
        Test("Sample.ParserTests.Reads")
    ];

    [AvaloniaFact]
    public void RediscoveryKeepsAClassTheUserOpenedInAShownWindow()
    {
        var panel = new TestExplorerPanel();
        panel.Show(Tests);

        var window = new Window { Content = panel, Width = 500, Height = 400 };
        window.Show();

        try
        {
            var tree = panel.GetVisualDescendants().OfType<TreeView>().Single();

            // The user opens the project, then the class, as a click on each chevron would.
            Settle(window);
            var project = panel.Roots.Single();
            RowOf(tree, project).IsExpanded = true;
            Settle(window);

            var calculator = project.Children.Single(c => c.Name == "CalculatorTests");
            RowOf(tree, calculator).IsExpanded = true;
            Settle(window);

            // The same solution is discovered again.
            panel.Show(Tests);
            Settle(window);

            var again = panel.Roots.Single().Children.Single(c => c.Name == "CalculatorTests");

            Assert.True(tree.TreeContainerFromItem(again) is TreeViewItem { IsExpanded: true },
                "The class the user opened was closed by the rediscovery.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RediscoveryKeepsTheResultsTheOpenClassesAndTheSelection()
    {
        var panel = new TestExplorerPanel();
        panel.Show(Tests);

        panel.ShowResults([new Basalt.Core.Services.TestResult(
            "Sample.CalculatorTests.Adds", TestOutcome.Passed, TimeSpan.FromMilliseconds(3))]);

        var calculator = panel.Roots[0].Children.Single(c => c.Name == "CalculatorTests");
        calculator.IsExpanded = true;
        panel.SelectForTests(calculator.Children.Single(l => l.Name == "Adds"));

        // The same solution is discovered again: the same tests, freshly built.
        panel.Show(Tests);

        var again = panel.Roots[0].Children.Single(c => c.Name == "CalculatorTests");

        Assert.Equal(TestOutcome.Passed, again.Children.Single(l => l.Name == "Adds").Outcome);
        Assert.True(again.IsExpanded);
        Assert.Equal("Sample.CalculatorTests.Adds", panel.SelectedTest?.FullyQualifiedName);
    }

    [AvaloniaFact]
    public void AFailedDiscoveryKeepsTheTreeTheWindowShowed()
    {
        // One project did not build: its tests are missing from the result, and
        // the tests the window had must not be dropped with them.
        var panel = new TestExplorerPanel();
        panel.Show(Tests);

        panel.ShowDiscovered(new TestDiscoveryResult([], ["Broken.vbproj: the build failed."]));

        Assert.Equal(3, panel.AllTests.Count);
    }

    [AvaloniaFact]
    public void ASuccessfulDiscoveryWithNoTestsShowsNone()
    {
        var panel = new TestExplorerPanel();
        panel.Show(Tests);

        panel.ShowDiscovered(new TestDiscoveryResult([], []));

        Assert.Empty(panel.AllTests);
    }

    [AvaloniaFact]
    public async Task AProjectThatDoesNotCompileKeepsTheEarlierTree()
    {
        // The project has a test SDK and a file that does not compile. Its discovery
        // is a failure, so the tree the window had is kept, not replaced by nothing.
        var root = Path.Combine(Path.GetTempPath(), "basalt-uncompiled", Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "Uncompiled");
        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllText(Path.Combine(folder, "Uncompiled.vbproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <RootNamespace></RootNamespace>
                    <IsPackable>false</IsPackable>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                    <PackageReference Include="MSTest.TestFramework" Version="3.9.3" />
                    <PackageReference Include="MSTest.TestAdapter" Version="3.9.3" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(folder, "Tests.vb"), "Public Class (\n");

            var panel = new TestExplorerPanel();
            panel.Show(Tests);

            var result = await TestDiscovery.DiscoverFolderAsync(
                root, new DotnetTestService(new BuildGate()), _ => { });

            Assert.False(result.Succeeded, "A project that does not compile was reported as discovered.");

            panel.ShowDiscovered(result);

            Assert.Equal(3, panel.AllTests.Count);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static TreeViewItem RowOf(TreeView tree, TestNode node) =>
        (TreeViewItem)tree.TreeContainerFromItem(node)!;
}
