using System.Reflection;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Designer.Toolbox;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Drives the Avalonia XAML designer the way a real user does: a real
/// <see cref="MainWindow"/>, opened through <see cref="TestWindow"/>, with real
/// pointer gestures (MouseDown/MouseMove/MouseUp on screen pixels found through
/// <c>GetVisualDescendants</c>) and real keyboard input (KeyPress) on the design
/// surface, the Toolbox, the Element Tree and the Properties panel - not direct
/// calls into the handlers that <see cref="DesignSurfaceTests"/>,
/// <see cref="ElementTreeTests"/> and <see cref="XamlElementTreeSelectionTests"/>
/// already cover unit by unit.
/// </summary>
public sealed class UserJourneyXamlDesignerTests
{
    private const string ButtonXaml = """
        <Window xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
          <Canvas>
            <Button x:Name="GoButton" Canvas.Left="20" Canvas.Top="30" Width="80" Height="24" Content="Go" />
          </Canvas>
        </Window>
        """;

    private const string EmptyXaml = """
        <Window xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
          <Canvas />
        </Window>
        """;

    private const string NestedXaml = """
        <Window xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
          <Canvas>
            <StackPanel x:Name="Group" Canvas.Left="20" Canvas.Top="20" Width="200" Height="120">
              <Button x:Name="Inner" Content="Inner" />
            </StackPanel>
          </Canvas>
        </Window>
        """;

    private sealed class Fixture : IDisposable
    {
        public required TestWindow Host { get; init; }
        public required MainWindowViewModel Vm { get; init; }
        public required string Path { get; init; }
        public required string Root { get; init; }

        public EditorDocumentViewModel Document => Vm.OpenDocuments.Single(d => d.FilePath == Path);

        public void Dispose()
        {
            Host.Dispose();
            try { Directory.Delete(Root, true); } catch (IOException) { }
        }
    }

    private static async Task<Fixture> OpenAsync(string xaml)
    {
        var root = Directory.CreateTempSubdirectory("basalt-journey-xaml-").FullName;
        var path = Path.Combine(root, "MainWindow.axaml");
        await File.WriteAllTextAsync(path, xaml);

        var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await vm.OpenFileAsync(path, inDesigner: true);
        await host.SettleAsync();

        return new Fixture { Host = host, Vm = vm, Path = path, Root = root };
    }

    // ------------------------------------------------------------------
    // Shared helpers.
    // ------------------------------------------------------------------

    private static T Field<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);

        return (T)field.GetValue(instance)!;
    }

    private static T Single<T>(Visual root) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>());

    /// <summary>The real preview control the renderer produced for an element with this content.</summary>
    private static Button PreviewButton(DesignSurface surface, string content) =>
        Assert.Single(surface.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, content));

    private static Point Centre(Rect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    private static Point ToWindow(Visual from, TestWindow window, Point point) =>
        from.TranslatePoint(point, window.Window) ?? point;

    private static void Click(TestWindow window, Control control)
    {
        var bounds = Raster.BoundsIn(control, window.Window);
        var at = new Point(bounds.X + bounds.Width / 2.0, bounds.Y + bounds.Height / 2.0);

        window.Window.MouseDown(at, MouseButton.Left);
        window.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Finds the realised row for an element named like <paramref name="containing"/>
    /// in the Element Tree and clicks it with a real pointer press/release on its own
    /// screen pixels, the same path a user takes.
    /// </summary>
    private static void ClickTreeRow(TestWindow window, ElementTreePanel tree, string containing)
    {
        var treeView = tree.GetVisualDescendants().OfType<TreeView>().Single();
        var items = treeView.GetVisualDescendants().OfType<TreeViewItem>().ToList();

        var row = items.LastOrDefault(item =>
            item.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text != null && t.Text.Contains(containing, StringComparison.Ordinal)));

        Assert.True(row is not null,
            $"labels={string.Join(",", tree.Labels)} itemCount={items.Count}");

        var bounds = Raster.BoundsIn(row!, window.Window);
        var spot = new Point(bounds.X + 10, bounds.Y + bounds.Height / 2.0);

        window.Window.MouseDown(spot, MouseButton.Left);
        window.Window.MouseUp(spot, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string? what = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The designer did not reach the expected state in time" + (what is null ? "." : ": " + what));
    }

    // ------------------------------------------------------------------
    // 1. Double-clicking a Toolbox item adds the control to the markup and
    //    to the Element Tree, without anything already selected to receive it.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task DoubleClickingAToolboxItemAddsTheControlToTheMarkupAndTheElementTree()
    {
        using var f = await OpenAsync(EmptyXaml);

        var toolbox = Field<ToolboxPanel>(f.Host.Window, "_toolbox");
        var list = toolbox.Body.List;

        var row = Assert.Single(list.GetVisualDescendants().OfType<ListBoxItem>(),
            item => item.DataContext is ToolboxItem entry && entry.DisplayName == "Text Box");
        row.BringIntoView();
        f.Host.Window.UpdateLayout();
        var bounds = Raster.BoundsIn(row, f.Host.Window);
        var at = new Point(bounds.X + 10, bounds.Y + bounds.Height / 2.0);

        f.Host.Window.MouseDown(at, MouseButton.Left);
        f.Host.Window.MouseUp(at, MouseButton.Left);
        f.Host.Window.MouseDown(at, MouseButton.Left);
        f.Host.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        await WaitUntilAsync(() => f.Document.Text.Contains("<TextBox", StringComparison.Ordinal),
            "the markup never gained a TextBox");

        var factory = Field<IdeDockFactory>(f.Host.Window, "_factory");
        factory.SetActiveDockable(factory.ElementTree);
        await f.Host.SettleAsync(20);

        var tree = Field<ElementTreePanel>(f.Host.Window, "_elementTree");
        Assert.Contains(tree.Labels, label => label.StartsWith("TextBox", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------
    // 2. Clicking an element on the surface selects it: the Properties panel
    //    shows it and the Element Tree highlights the same node.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ClickingAnElementOnTheSurfaceSelectsItInThePropertiesAndTheTree()
    {
        using var f = await OpenAsync(ButtonXaml);

        var factory = Field<IdeDockFactory>(f.Host.Window, "_factory");
        factory.SetActiveDockable(factory.ElementTree);
        await f.Host.SettleAsync(20);

        var surface = Single<DesignSurface>(f.Host.Window);
        Click(f.Host, PreviewButton(surface, "Go"));

        var vm = f.Vm;
        var designer = vm.ActiveDesigner!;
        var button = designer.Document.Root.Descendants().First(e => e.Name.LocalName == "Button");
        Assert.Same(button, designer.Selection);

        var properties = Field<PropertyPanel>(f.Host.Window, "_properties");
        var elementLabel = Field<TextBlock>(properties, "_element");
        Assert.Equal("Button", elementLabel.Text);
        Assert.Contains(properties.Properties.Rows.OfType<Grid>(),
            row => row.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Content"));

        var tree = Field<ElementTreePanel>(f.Host.Window, "_elementTree");
        Assert.Same(button, tree.SelectedElement);
    }

    // ------------------------------------------------------------------
    // 3. Editing a property in the Properties panel updates the markup, the
    //    surface re-renders, and Undo restores it.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task EditingAPropertyUpdatesTheMarkupRerendersAndUndoRestoresIt()
    {
        using var f = await OpenAsync(ButtonXaml);

        var surface = Single<DesignSurface>(f.Host.Window);
        Click(f.Host, PreviewButton(surface, "Go"));

        var properties = Field<PropertyPanel>(f.Host.Window, "_properties");
        var row = Assert.Single(properties.Properties.Rows.OfType<Grid>(),
            r => r.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Content"));
        var editor = Assert.Single(row.GetVisualDescendants().OfType<TextBox>());

        editor.Focus();
        editor.Text = "Changed";

        // A real blur: clicking elsewhere moves focus off the box, which is
        // what commits the edit - the standard property grid (unlike the
        // Visual Studio-style one WinForms uses) writes only on LostFocus.
        Click(f.Host, surface);
        await f.Host.SettleAsync();

        Assert.Contains("Changed", f.Document.Text);
        Assert.True(f.Document.IsModified);

        surface.RefreshNow();
        await f.Host.SettleAsync();
        Assert.Contains(surface.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Changed"));

        // Real Ctrl+Z, through the window's own key binding - the same
        // gesture the Edit menu's Undo entry is bound to.
        f.Host.Window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
        await f.Host.SettleAsync();

        Assert.DoesNotContain("Changed", f.Document.Text);
        Assert.Contains("Content=\"Go\"", f.Document.Text);

        surface.RefreshNow();
        await f.Host.SettleAsync();
        Assert.Contains(surface.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Go"));
    }

    // ------------------------------------------------------------------
    // 4. Dragging moves, a corner grip resizes, and the arrow keys nudge the
    //    selected control - each written to the markup as it happens.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task DraggingResizingAndNudgingTheSelectionUpdateTheMarkup()
    {
        using var f = await OpenAsync(ButtonXaml);

        var surface = Single<DesignSurface>(f.Host.Window);
        Click(f.Host, PreviewButton(surface, "Go"));

        // Move: from the middle of the button, 30 right and 15 down.
        var boundsBefore = surface.SelectionBoundsForTests!.Value;
        var from = ToWindow(surface, f.Host, Centre(boundsBefore));
        var to = from + new Vector(30, 15);

        f.Host.Window.MouseDown(from, MouseButton.Left);
        f.Host.Window.MouseMove(from + new Vector(4, 4));
        f.Host.Window.MouseMove(to);
        f.Host.Window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        await WaitUntilAsync(() => f.Document.Text.Contains("Canvas.Left=\"50\"", StringComparison.Ordinal),
            "the drag did not move the button");
        await WaitUntilAsync(() => f.Document.Text.Contains("Canvas.Top=\"45\"", StringComparison.Ordinal));

        // Resize: grab the bottom-right handle and pull it out by (20, 10).
        // Alt held, as in Visual Studio, so there is nothing to snap to -
        // there is only one control on the canvas, but the edges of the
        // canvas itself are still candidates.
        var boundsAfterMove = surface.SelectionBoundsForTests!.Value;
        var grip = ToWindow(surface, f.Host, boundsAfterMove.BottomRight);
        var resizeTo = grip + new Vector(20, 10);

        f.Host.Window.MouseDown(grip, MouseButton.Left, RawInputModifiers.Alt);
        f.Host.Window.MouseMove(grip + new Vector(4, 4), RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
        f.Host.Window.MouseMove(resizeTo, RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
        f.Host.Window.MouseUp(resizeTo, MouseButton.Left, RawInputModifiers.Alt);
        Dispatcher.UIThread.RunJobs();

        await WaitUntilAsync(() => f.Document.Text.Contains("Width=\"100\"", StringComparison.Ordinal),
            "the resize did not change the width");
        await WaitUntilAsync(() => f.Document.Text.Contains("Height=\"34\"", StringComparison.Ordinal));

        // Nudge: one key at a time, as a person presses them.
        f.Host.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        await WaitUntilAsync(() => f.Document.Text.Contains("Canvas.Left=\"51\"", StringComparison.Ordinal),
            "Right did not nudge the button");
        await f.Host.SettleAsync(1);
        f.Host.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        await WaitUntilAsync(() => f.Document.Text.Contains("Canvas.Top=\"46\"", StringComparison.Ordinal),
            "Down did not nudge the button");
    }

    // ------------------------------------------------------------------
    // 4b. Delete removes the selection, and Undo brings it back.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task DeleteRemovesTheSelectionAndUndoRestoresIt()
    {
        using var f = await OpenAsync(ButtonXaml);

        var surface = Single<DesignSurface>(f.Host.Window);
        Click(f.Host, PreviewButton(surface, "Go"));

        f.Host.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        await WaitUntilAsync(() => !f.Document.Text.Contains("<Button", StringComparison.Ordinal),
            "Delete did not remove the button");

        f.Host.Window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
        await WaitUntilAsync(() => f.Document.Text.Contains("<Button", StringComparison.Ordinal),
            "Undo did not bring the button back");
    }

    // ------------------------------------------------------------------
    // 5. Clicking a nested row in the Element Tree selects that element,
    //    after the branch it lives in has been expanded.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ClickingANestedElementTreeRowSelectsItAfterExpandingTheBranch()
    {
        using var f = await OpenAsync(NestedXaml);

        var factory = Field<IdeDockFactory>(f.Host.Window, "_factory");
        factory.SetActiveDockable(factory.ElementTree);
        await f.Host.SettleAsync(20);

        var tree = Field<ElementTreePanel>(f.Host.Window, "_elementTree");

        // The branch is expanded automatically when the tree is shown, which
        // is itself the behaviour under test: the nested row has to be on
        // screen before it can be clicked, exactly as it would after a user
        // opens the StackPanel's own branch by hand. TreeView realises a
        // container's own children only once that container itself has been
        // realised and measured, so a row two levels deep needs an extra
        // layout pass beyond the root's own - polled rather than assumed,
        // the way every other wait in this file is.
        await WaitUntilAsync(
            () => tree.GetVisualDescendants().OfType<TreeView>().Single()
                .GetVisualDescendants().OfType<TreeViewItem>().Count() >= 3,
            "the StackPanel's branch never expanded down to its own child");

        Assert.Contains(tree.Labels, label => label.StartsWith("Group", StringComparison.Ordinal));
        Assert.Contains(tree.Labels, label => label.StartsWith("Inner", StringComparison.Ordinal));

        ClickTreeRow(f.Host, tree, "Inner");

        var designer = f.Vm.ActiveDesigner!;
        var inner = designer.Document.Root.Descendants().First(e => e.Name.LocalName == "Button");

        Assert.Same(inner, designer.Selection);
        Assert.Same(inner, tree.SelectedElement);

        // A branch the user closes stays closed: expanding is for rows as
        // they first appear, not something re-imposed on every layout pass.
        var group = GroupRow(tree);
        group.IsExpanded = false;
        await f.Host.SettleAsync(10);
        Assert.False(group.IsExpanded, "the collapsed branch reopened by itself");

        // Every edit rebuilds the tree's rows; the closed branch stays closed.
        designer.SetProperty("Width", "210");
        await WaitUntilAsync(() => f.Document.Text.Contains("Width=\"210\"", StringComparison.Ordinal));
        await f.Host.SettleAsync(10);
        var rebuilt = GroupRow(tree);
        Assert.False(rebuilt.IsExpanded, "an edit reopened the branch the user had closed");
    }

    // The row's own element, not its text: the Canvas row above it contains
    // "Group" among its descendants too.
    private static TreeViewItem GroupRow(ElementTreePanel tree) =>
        tree.GetVisualDescendants().OfType<TreeViewItem>().First(row =>
            row.DataContext is ElementTreePanel.Node node
            && node.Element.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "Group"));

    /// <summary>
    /// Content written between the tags, or as a &lt;Button.Content&gt;
    /// element, is not offered as a text field: a Content attribute next to
    /// it would set the property twice and break the file.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("<Button x:Name=\"Go\">Go</Button>")]
    [InlineData("<Button x:Name=\"Go\"><Button.Content><TextBlock Text=\"Go\" /></Button.Content></Button>")]
    public async Task ContentWrittenInsideTheElementIsNotOfferedAsATextField(string button)
    {
        var xaml = NestedXaml.Replace("<Button x:Name=\"Inner\" Content=\"Inner\" />", button, StringComparison.Ordinal);
        using var f = await OpenAsync(xaml);

        var tree = Field<ElementTreePanel>(f.Host.Window, "_elementTree");
        var element = f.Vm.ActiveDesigner!.Document.Root.Descendants().First(e => e.Name.LocalName == "Button");
        tree.SelectForTests(element);
        await f.Host.SettleAsync(5);

        var properties = Field<PropertyPanel>(f.Host.Window, "_properties");
        Assert.Equal("Button", Field<TextBlock>(properties, "_element").Text);
        Assert.DoesNotContain(properties.Properties.Rows.OfType<Grid>(),
            row => row.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Content"));
    }

    // ------------------------------------------------------------------
    // 6. Save, then reopen the file from disk: round trips byte for byte.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task SavingAndReopeningTheDesignerFileRoundTrips()
    {
        using var f = await OpenAsync(ButtonXaml);

        var surface = Single<DesignSurface>(f.Host.Window);
        Click(f.Host, PreviewButton(surface, "Go"));

        f.Host.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        await WaitUntilAsync(() => f.Document.Text.Contains("Canvas.Left=\"21\"", StringComparison.Ordinal));

        await f.Vm.SaveCommand.ExecuteAsync(null);
        var saved = await File.ReadAllTextAsync(f.Path);
        Assert.Contains("Canvas.Left=\"21\"", saved);

        f.Vm.CloseActiveDocument();
        await f.Host.SettleAsync(3);
        await f.Vm.OpenFileAsync(f.Path, inDesigner: true);
        await f.Host.SettleAsync(5);

        var reopenedSurface = Single<DesignSurface>(f.Host.Window);
        Assert.Contains(reopenedSurface.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Go"));
        Assert.Equal(saved, f.Document.Text);
        Assert.Equal("21", f.Vm.ActiveDesigner!.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "Button").Attribute("Canvas.Left")?.Value);
    }

    // ------------------------------------------------------------------
    // 6b. Editing the markup by hand in the text view and switching back to
    //     the designer shows the change: no stale session left over.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task EditingTheMarkupInTheCodeViewAndSwitchingBackShowsTheChange()
    {
        using var f = await OpenAsync(ButtonXaml);

        var host = Single<DesignerHost>(f.Host.Window);
        host.ShowText();
        await f.Host.SettleAsync();

        // What the text half of the tab does on every keystroke (see
        // XamlElementTreeSelectionTests.SavingAfterEditingTheTextByHandKeepsWhatWasTyped
        // for the same convention): this is not testing the text editor's own
        // typing, only that the designer picks the result up again.
        f.Document.Text = f.Document.Text.Replace("Content=\"Go\"", "Content=\"Typed\"", StringComparison.Ordinal);
        await f.Host.SettleAsync();

        host.ShowDesigner();
        await f.Host.SettleAsync();

        var surface = Single<DesignSurface>(f.Host.Window);
        Assert.Contains(surface.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Typed"));
        Assert.Equal("Typed", f.Vm.ActiveDesigner!.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "Button").Attribute("Content")?.Value);
    }
}
