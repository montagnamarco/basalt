using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Core.Services;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The Test Explorer's Refresh action, and discovery after a build of a test project.
/// </summary>
/// <remarks>
/// Red-proof, one defect at a time: removing the Refresh button's click
/// handler fails TheRefreshButtonAsksForDiscovery; removing the window's
/// RefreshRequested subscription fails RefreshFindsATestAddedSinceTheSolutionOpened;
/// removing the successful-build subscription fails
/// ABuildOfATestProjectDiscoversItsTestsAgain.
/// </remarks>
public class TestExplorerRefreshFixTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-testrefresh", Guid.NewGuid().ToString("N"));

    public TestExplorerRefreshFixTests() => Directory.CreateDirectory(_root);

    [AvaloniaFact]
    public void TheRefreshButtonAsksForDiscovery()
    {
        var panel = new TestExplorerPanel();

        var asked = 0;
        panel.RefreshRequested += (_, _) => asked++;

        var window = new Window { Content = panel, Width = 500, Height = 400 };
        window.Show();

        try
        {
            var refresh = panel.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Content as string == Localizer.Get(StringKeys.TestsRefresh));

            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(1, asked);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RefreshFindsATestAddedSinceTheSolutionOpened()
    {
        var project = WriteMSTestProject("RefreshProject");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        var panel = Field<TestExplorerPanel>(host.Window, "_tests");

        Assert.True(await WaitUntilAsync(host, () => panel.AllTests.Count == 1, TimeSpan.FromMinutes(3)),
            "The first test was not discovered when the project opened.");

        // A test written by hand, with no build to say so. The button itself is
        // covered on a panel of its own; the Tests tool is not realised in this window.
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(project)!, "More.vb"), SecondTestCode);

        panel.RequestRefreshForTests();

        Assert.True(await WaitUntilAsync(host, () => panel.AllTests.Count == 2, TimeSpan.FromMinutes(3)),
            "Refresh did not find the test added on disk.");
    }

    [AvaloniaFact]
    public async Task ABuildOfATestProjectDiscoversItsTestsAgain()
    {
        var project = WriteMSTestProject("BuildProject");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        var panel = Field<TestExplorerPanel>(host.Window, "_tests");
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        Assert.True(await WaitUntilAsync(host, () => panel.AllTests.Count == 1, TimeSpan.FromMinutes(3)),
            "The first test was not discovered when the project opened.");

        File.WriteAllText(Path.Combine(Path.GetDirectoryName(project)!, "More.vb"), SecondTestCode);

        // A real build of the test project, which is what makes discovery run again.
        await vm.BuildCommand.ExecuteAsync(null);

        Assert.True(await WaitUntilAsync(host, () => panel.AllTests.Count == 2, TimeSpan.FromMinutes(3)),
            "A build of the test project did not discover its tests again.");
    }

    private string WriteMSTestProject(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);

        var project = Path.Combine(folder, name + ".vbproj");

        File.WriteAllText(project, """
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

        File.WriteAllText(Path.Combine(folder, "First.vb"), """
            Imports Microsoft.VisualStudio.TestTools.UnitTesting

            Namespace Sample
                <TestClass>
                Public Class FirstTests
                    <TestMethod>
                    Public Sub Passes()
                        Assert.AreEqual(4, 2 + 2)
                    End Sub
                End Class
            End Namespace
            """);

        return project;
    }

    private const string SecondTestCode = """
        Imports Microsoft.VisualStudio.TestTools.UnitTesting

        Namespace Sample
            <TestClass>
            Public Class SecondTests
                <TestMethod>
                Public Sub AlsoPasses()
                    Assert.AreEqual(4, 2 + 2)
                End Sub
            End Class
        End Namespace
        """;

    private static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < timeout)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }

        return condition();
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(target)!;

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
