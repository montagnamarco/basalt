using System.Reflection;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Designer;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class XamlElementTreeSelectionTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-elementtree-select-").FullName;

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Write()
    {
        var path = Path.Combine(_root, "MainWindow.axaml");

        // A single root control: the row is realised as soon as the panel is
        // shown, with no expander to click first, which keeps the test to
        // the one thing it means to prove (a click on a real row reaches the
        // designer) rather than Dock's own virtualization timing.
        File.WriteAllText(path, """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Button x:Name="OkButton" Content="OK" />
            </Window>
            """);

        return path;
    }

    private static T Field<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);

        return (T)field.GetValue(instance)!;
    }

    [AvaloniaFact]
    public async Task ClickingATreeRowSelectsTheSameElementInTheDesigner()
    {
        using var window = new TestWindow();
        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenFileAsync(Write(), inDesigner: true);
        await window.SettleAsync();

        var factory = Field<IdeDockFactory>(window.Window, "_factory");
        factory.SetActiveDockable(factory.ElementTree);
        await window.SettleAsync(20);

        var tree = Field<ElementTreePanel>(window.Window, "_elementTree");

        // KNOWN GAP (see docs/planning/PANELS-AUDIT.md): MainWindowViewModel.
        // DesignerSessionFor silently rebuilds the DesignerSession (a new
        // XamlDocument, so new XElement instances) whenever the cached
        // session's re-serialised markup is not byte-identical to the file's
        // on-disk text, which is true of nearly every hand-written file.
        // RefreshDesignerToolDocks calls DesignerSessionFor again (deferred
        // at Loaded priority) and can hand the tree a *different* session
        // object than the one MainWindowViewModel.ActiveDesigner still
        // points at, so a tree built from one generation selects into a
        // session nobody else is showing. Forcing both to the same session
        // here isolates the one thing this test means to prove — that a
        // real pointer click on a real tree row reaches the designer at
        // all — from that separate, unfixed staleness defect.
        tree.Show(vm.ActiveDesigner);
        await window.SettleAsync();

        var treeView = tree.GetVisualDescendants().OfType<TreeView>().Single();

        var items = treeView.GetVisualDescendants().OfType<TreeViewItem>().ToList();

        var row = items.LastOrDefault(item =>
            item.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text != null && t.Text.Contains("OkButton")));

        Assert.True(row is not null,
            $"labels={string.Join(",", tree.Labels)} empty={tree.IsShowingEmptyState} " +
            $"itemCount={items.Count} texts=" +
            string.Join("|", items.SelectMany(i => i.GetVisualDescendants().OfType<TextBlock>()).Select(t => t.Text)));

        var bounds = Raster.BoundsIn(row!, window.Window);
        var spot = new Point(bounds.X + 10, bounds.Y + bounds.Height / 2.0);

        window.Window.MouseDown(spot, MouseButton.Left);
        window.Window.MouseUp(spot, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var designer = vm.ActiveDesigner;
        Assert.NotNull(designer);

        var button = designer!.Document.Root.Descendants().First(e => e.Name.LocalName == "Button");

        Assert.Same(button, designer.Selection);
        Assert.Same(button, tree.SelectedElement);
    }
}
