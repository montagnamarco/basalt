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

/// <summary>
/// Regression tests for the defect recorded in docs/planning/PANELS-AUDIT.md
/// "Session 3, item 8": <c>MainWindowViewModel.DesignerSessionFor</c> used to
/// rebuild the cached <c>DesignerSession</c> (new <c>XElement</c> instances)
/// whenever its re-serialised markup was not byte-identical to the on-disk
/// text, which is true of nearly any hand-written .axaml. The deferred half
/// of <c>RefreshDesignerToolDocks</c> could then hand the Element Tree /
/// Properties panels a different session than
/// <see cref="MainWindowViewModel.ActiveDesigner"/> still pointed at, so a
/// tree click selected into an orphaned <c>XElement</c>.
/// </summary>
public sealed class XamlElementTreeSelectionTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-elementtree-select-").FullName;

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    /// <summary>
    /// A hand-written file whose formatting differs from what the designer's
    /// own serialiser would produce in every way that matters: extra spaces
    /// around attributes, attributes in a different order than the control's
    /// own property order, a comment, and CRLF line endings. Before the fix,
    /// this shape alone (not any particular one of those differences) was
    /// enough to make the very first <c>DesignerSessionFor</c> re-check
    /// rebuild the session.
    /// </summary>
    private string Write(string fileName = "MainWindow.axaml")
    {
        var path = Path.Combine(_root, fileName);

        var markup =
            "<Window xmlns=\"https://github.com/avaloniaui\"\n" +
            "        xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <!-- a hand-written comment the serialiser would never emit -->\n" +
            "  <Button   Content=\"OK\"    x:Name=\"OkButton\"  />\n" +
            "</Window>\n";

        File.WriteAllText(path, markup.Replace("\n", "\r\n"));

        return path;
    }

    private static T Field<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);

        return (T)field.GetValue(instance)!;
    }

    /// <summary>
    /// Finds the realised row for an element named like <paramref name="containing"/>
    /// and clicks it with a real pointer press/release on its own screen pixels,
    /// the same path a user takes (not a direct call into the handler).
    /// </summary>
    private static void ClickRow(TestWindow window, ElementTreePanel tree, string containing)
    {
        var treeView = tree.GetVisualDescendants().OfType<TreeView>().Single();
        var items = treeView.GetVisualDescendants().OfType<TreeViewItem>().ToList();

        var row = items.LastOrDefault(item =>
            item.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text != null && t.Text.Contains(containing)));

        Assert.True(row is not null,
            $"labels={string.Join(",", tree.Labels)} empty={tree.IsShowingEmptyState} " +
            $"itemCount={items.Count} texts=" +
            string.Join("|", items.SelectMany(i => i.GetVisualDescendants().OfType<TextBlock>()).Select(t => t.Text)));

        var bounds = Raster.BoundsIn(row!, window.Window);
        var spot = new Point(bounds.X + 10, bounds.Y + bounds.Height / 2.0);

        window.Window.MouseDown(spot, MouseButton.Left);
        window.Window.MouseUp(spot, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
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

        ClickRow(window, tree, "OkButton");

        var designer = vm.ActiveDesigner;
        Assert.NotNull(designer);

        var button = designer!.Document.Root.Descendants().First(e => e.Name.LocalName == "Button");

        Assert.Same(button, designer.Selection);
        Assert.Same(button, tree.SelectedElement);
    }

    [AvaloniaFact]
    public async Task EditingAPropertyWritesTheFileAndTheTreeStillSelectsCorrectly()
    {
        using var window = new TestWindow();
        var vm = (MainWindowViewModel)window.Window.DataContext!;

        var path = Write();
        await vm.OpenFileAsync(path, inDesigner: true);
        await window.SettleAsync();

        var factory = Field<IdeDockFactory>(window.Window, "_factory");
        factory.SetActiveDockable(factory.ElementTree);
        await window.SettleAsync(20);

        var tree = Field<ElementTreePanel>(window.Window, "_elementTree");
        ClickRow(window, tree, "OkButton");

        var designer = vm.ActiveDesigner!;
        var button = designer.Document.Root.Descendants().First(e => e.Name.LocalName == "Button");
        Assert.Same(button, designer.Selection);

        // The same route the Properties grid uses (PropertyPanel.cs wires
        // PropertyEdited straight to this call).
        designer.SetProperty("Content", "Changed");
        await window.SettleAsync();

        var document = vm.ActiveDocument!;
        Assert.Contains("Changed", document.Text);
        Assert.True(document.IsModified);

        // Re-requesting the session (as RefreshDesignerToolDocks does, twice,
        // once deferred) must not rebuild it: the edit already wrote back
        // text the session agrees with, so the tree's node and the designer's
        // selection stay the very same XElement.
        var sessionAgain = vm.DesignerSessionFor(document);
        Assert.Same(designer, sessionAgain);

        tree.Show(sessionAgain);
        await window.SettleAsync();

        Assert.Same(button, designer.Selection);
        Assert.Same(button, tree.SelectedElement);
    }

    [AvaloniaFact]
    public async Task SwitchingDocumentsAndBackKeepsTheSameSessionAndSelection()
    {
        using var window = new TestWindow();
        var vm = (MainWindowViewModel)window.Window.DataContext!;

        var firstPath = Write("First.axaml");
        var secondPath = Write("Second.axaml");

        await vm.OpenFileAsync(firstPath, inDesigner: true);
        await window.SettleAsync();

        var factory = Field<IdeDockFactory>(window.Window, "_factory");
        factory.SetActiveDockable(factory.ElementTree);
        await window.SettleAsync(20);

        var tree = Field<ElementTreePanel>(window.Window, "_elementTree");
        ClickRow(window, tree, "OkButton");

        var firstSession = vm.ActiveDesigner!;
        var firstButton = firstSession.Document.Root.Descendants().First(e => e.Name.LocalName == "Button");
        Assert.Same(firstButton, firstSession.Selection);

        await vm.OpenFileAsync(secondPath, inDesigner: true);
        await window.SettleAsync();
        Assert.NotSame(firstSession, vm.ActiveDesigner);

        var firstDocument = vm.OpenDocuments.First(d => d.FilePath == firstPath);
        vm.ActivateFromTabSelection(firstDocument);
        await window.SettleAsync(20);

        // Switching back must return exactly the session that still holds
        // the selection made before switching away, not a rebuilt one with a
        // fresh, unselected XElement tree.
        Assert.Same(firstSession, vm.ActiveDesigner);
        Assert.Same(firstButton, vm.ActiveDesigner!.Selection);

        tree.Show(vm.ActiveDesigner);
        await window.SettleAsync();
        Assert.Same(firstButton, tree.SelectedElement);
    }
}
