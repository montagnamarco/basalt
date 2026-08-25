using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Reaching a project's properties.
///
/// They existed, but only under the Project menu: not on the project node in
/// the tree, which is where anyone looks first.
/// </summary>
public sealed class ProjectPropertiesReachTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-props-").FullName;

    public ProjectPropertiesReachTests()
    {
        File.WriteAllText(Path.Combine(_root, "Probe.vbproj"), "<Project />");
        File.WriteAllText(Path.Combine(_root, "Program.vb"), "Module M\nEnd Module\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private (SolutionExplorerPanel Panel, MainWindowViewModel Model) Open()
    {
        var vm = new MainWindowViewModel();

        vm.Explorer.Load(Path.Combine(_root, "Probe.vbproj"));

        return (new SolutionExplorerPanel(vm), vm);
    }

    [AvaloniaFact]
    public void TheProjectNodeOffersItsProperties()
    {
        var (panel, vm) = Open();

        vm.Explorer.SelectedNode = vm.Explorer.Roots[0];

        var entries = ContextMenus.OpenForTests(panel.TreeForTests.ContextMenu!);

        Assert.Contains(entries, e =>
            e.Header.Contains("Properties", StringComparison.Ordinal) && e.Enabled);
    }

    [AvaloniaFact]
    public void ChoosingItAsksTheShell()
    {
        var (panel, vm) = Open();

        vm.Explorer.SelectedNode = vm.Explorer.Roots[0];

        SolutionExplorerPanel.ExplorerCommand? asked = null;

        panel.CommandChosen += (_, chosen) => asked = chosen.Command;

        ContextMenus.ChooseForTests(panel.TreeForTests.ContextMenu!, "Properties…");

        Assert.Equal(SolutionExplorerPanel.ExplorerCommand.Properties, asked);
    }

    [AvaloniaFact]
    public void AltEnterAsksForThemToo()
    {
        var (panel, vm) = Open();

        vm.Explorer.SelectedNode = vm.Explorer.Roots[0];

        SolutionExplorerPanel.ExplorerCommand? asked = null;

        panel.CommandChosen += (_, chosen) => asked = chosen.Command;

        var window = new Window { Content = panel, Width = 300, Height = 300 };

        window.Show();
        window.UpdateLayout();

        panel.TreeForTests.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
            KeyModifiers = KeyModifiers.Alt
        });

        window.Close();

        Assert.Equal(SolutionExplorerPanel.ExplorerCommand.Properties, asked);
    }

    [AvaloniaFact]
    public void AFileNodeDoesNotOfferThem()
    {
        // A .vb file has no properties window of its own.
        var (panel, vm) = Open();

        var file = vm.Explorer.Roots[0].Children
            .First(n => n.Name.EndsWith(".vb", StringComparison.Ordinal));

        vm.Explorer.SelectedNode = file;

        SolutionExplorerPanel.ExplorerCommand? asked = null;

        panel.CommandChosen += (_, chosen) => asked = chosen.Command;

        var window = new Window { Content = panel, Width = 300, Height = 300 };

        window.Show();
        window.UpdateLayout();

        panel.TreeForTests.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
            KeyModifiers = KeyModifiers.Alt
        });

        window.Close();

        Assert.Null(asked);
    }
}
