using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The buttons above the file tree.
///
/// There were none: no way to see what the tree hides, to close a project
/// opened all the way down, or to find the file being edited in it.
/// </summary>
public sealed class SolutionExplorerToolbarTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-explorertb-").FullName;

    public SolutionExplorerToolbarTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "obj"));
        Directory.CreateDirectory(Path.Combine(_root, "Views"));

        File.WriteAllText(Path.Combine(_root, "Probe.vbproj"), "<Project />");
        File.WriteAllText(Path.Combine(_root, "Program.vb"), "Module M\nEnd Module\n");
        File.WriteAllText(Path.Combine(_root, "Views", "Index.vb"), "' view\n");
        File.WriteAllText(Path.Combine(_root, "obj", "leftover.txt"), "build output");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private SolutionExplorerViewModel Loaded()
    {
        var explorer = new SolutionExplorerViewModel();

        explorer.Load(Path.Combine(_root, "Probe.vbproj"));

        return explorer;
    }

    private static IEnumerable<SolutionTreeNode> Walk(SolutionTreeNode node)
    {
        yield return node;

        foreach (var child in node.Children)
            foreach (var found in Walk(child))
                yield return found;
    }

    [Fact]
    public void TheOutputFoldersAreHiddenToBeginWith()
    {
        var explorer = Loaded();

        var names = explorer.Roots.SelectMany(Walk).Select(n => n.Name).ToList();

        Assert.Contains("Program.vb", names);
        Assert.DoesNotContain("obj", names);
    }

    [Fact]
    public void ShowingAllFilesBringsThemBack()
    {
        var explorer = Loaded();

        explorer.ShowAllFiles = true;

        var names = explorer.Roots.SelectMany(Walk).Select(n => n.Name).ToList();

        Assert.Contains("obj", names);
    }

    [Fact]
    public void TurningItOffHidesThemAgain()
    {
        var explorer = Loaded();

        explorer.ShowAllFiles = true;
        explorer.ShowAllFiles = false;

        var names = explorer.Roots.SelectMany(Walk).Select(n => n.Name).ToList();

        Assert.DoesNotContain("obj", names);
    }

    [Fact]
    public void RefreshingPicksUpAFileWrittenSince()
    {
        var explorer = Loaded();

        File.WriteAllText(Path.Combine(_root, "Added.vb"), "' new\n");

        explorer.Refresh();

        var names = explorer.Roots.SelectMany(Walk).Select(n => n.Name).ToList();

        Assert.Contains("Added.vb", names);
    }

    [AvaloniaFact]
    public void ThePanelOffersItsButtons()
    {
        var vm = new MainWindowViewModel();
        var panel = new SolutionExplorerPanel(vm);

        // The logical tree: the panel has never been shown, so there is no
        // visual tree to walk.
        var buttons = ((StackPanel)panel.Tools).Children.OfType<Button>().ToList();

        // Show all files, collapse, sync, refresh.
        Assert.True(buttons.Count >= 4, $"only {buttons.Count} buttons");

        Assert.Contains(buttons, b => b is ToggleButton);
    }

    [AvaloniaFact]
    public void CollapsingClosesTheFoldersButKeepsTheRoot()
    {
        var vm = new MainWindowViewModel();

        vm.Explorer.Load(Path.Combine(_root, "Probe.vbproj"));

        var panel = new SolutionExplorerPanel(vm);

        var views = vm.Explorer.Roots.SelectMany(Walk).First(n => n.Name == "Views");

        views.IsExpanded = true;

        panel.CollapseAll();

        Assert.False(views.IsExpanded);

        // The root stays open, or the tree would look empty.
        Assert.True(vm.Explorer.Roots[0].IsExpanded);
    }

    [AvaloniaFact]
    public void RevealingAFileOpensTheBranchLeadingToIt()
    {
        var vm = new MainWindowViewModel();

        vm.Explorer.Load(Path.Combine(_root, "Probe.vbproj"));

        var panel = new SolutionExplorerPanel(vm);

        panel.CollapseAll();
        panel.Reveal(Path.Combine(_root, "Views", "Index.vb"));

        var views = vm.Explorer.Roots.SelectMany(Walk).First(n => n.Name == "Views");
        var file = vm.Explorer.Roots.SelectMany(Walk).First(n => n.Name == "Index.vb");

        Assert.True(views.IsExpanded);
        Assert.True(file.IsSelected);
    }
}
