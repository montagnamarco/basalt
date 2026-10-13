using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Core.Settings;
using Basalt.Shell;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// A solution or project opens with its tree showing: the root expanded, and
/// a solution's projects one level down, so the files are visible at once.
///
/// The root was marked expanded in the view model, but the tree never bound
/// its rows to that flag, so every root showed closed and nothing under it was
/// visible. These tests open a real window and look for the file rows.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class SolutionExplorerOpenStateTests : IDisposable
{
    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-explorer-open-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public SolutionExplorerOpenStateTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);
        try { File.Delete(_settings); } catch (IOException) { }
    }

    /// <summary>A project folder with one module in it.</summary>
    private static string WriteProject(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "App.vbproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Library</OutputType>"
            + "<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(folder, "Module1.vb"), "Module Module1\nEnd Module\n");
        return Path.Combine(folder, "App.vbproj");
    }

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "basalt-explorer-" + Guid.NewGuid().ToString("N"));

    /// <summary>A project opened alone.</summary>
    private static string WriteProjectOnly()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);
        return WriteProject(root);
    }

    /// <summary>A solution whose one project sits in a folder of its own.</summary>
    private static string WriteSolutionWithOneProject()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Tour.slnx"), "<Solution />");
        WriteProject(Path.Combine(root, "App"));
        return Path.Combine(root, "Tour.slnx");
    }

    private static MainWindowViewModel ViewModelOf(TestWindow host) =>
        (MainWindowViewModel)host.Window.DataContext!;

    private static async Task ShowAsync(TestWindow host)
    {
        host.Window.Width = 1440;
        host.Window.Height = 900;
        await host.SettleAsync(8);
    }

    /// <summary>The row a node is drawn as, or null when no row exists for it.</summary>
    private static TreeViewItem? RowFor(TestWindow host, string name) =>
        host.Window.GetVisualDescendants().OfType<TreeViewItem>()
            .FirstOrDefault(row => (row.DataContext as SolutionTreeNode)?.Name == name);

    [AvaloniaFact]
    public async Task AProjectOpensWithItsRootExpandedAndItsFilesInView()
    {
        using var host = new TestWindow(WriteProjectOnly());
        await ShowAsync(host);

        var root = ViewModelOf(host).Explorer.Roots.Single();
        Assert.True(root.IsExpanded, "The project's root is not expanded in the model.");

        var file = RowFor(host, "Module1.vb");
        Assert.NotNull(file);
        Assert.True(file.IsEffectivelyVisible && file.Bounds.Height > 0,
            "Module1.vb is not drawn: the project's root shows closed.");
    }

    [AvaloniaFact]
    public async Task ASolutionOpensWithItsProjectExpandedAndTheProjectsFilesInView()
    {
        using var host = new TestWindow(WriteSolutionWithOneProject());
        await ShowAsync(host);

        var solution = ViewModelOf(host).Explorer.Roots.Single();
        Assert.True(solution.IsExpanded, "The solution's root is not expanded.");

        var project = solution.Children.Single(child => child.Kind == NodeKind.Project);
        Assert.True(project.IsExpanded, "The solution's project is not expanded, so its files are hidden.");

        var file = RowFor(host, "Module1.vb");
        Assert.NotNull(file);
        Assert.True(file.IsEffectivelyVisible && file.Bounds.Height > 0,
            "Module1.vb, inside the solution's project, is not drawn.");
    }

    [AvaloniaFact]
    public async Task ARememberedSessionThatClosedTheRootKeepsItClosed()
    {
        // A user who closed the root before leaving: the session remembers an
        // empty tree, and reopening must not open the root again.
        using var host = new TestWindow(WriteProjectOnly());
        await ShowAsync(host);

        var viewModel = ViewModelOf(host);
        var path = viewModel.SolutionPath!;
        viewModel.Explorer.Roots.Single().IsExpanded = false;
        await host.SettleAsync(3);

        var session = viewModel.CaptureSession();
        session.ExpandedFolders = [];

        viewModel.Explorer.Load(path);
        await viewModel.RestoreSessionAsync(session);
        await host.SettleAsync(6);

        Assert.False(viewModel.Explorer.Roots.Single().IsExpanded,
            "A remembered session that closed the root opened it again.");
        Assert.False(RowFor(host, "Module1.vb")?.IsEffectivelyVisible ?? false,
            "Files show under a root the session had closed.");
    }
}
