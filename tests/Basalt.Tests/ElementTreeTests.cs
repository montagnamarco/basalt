using Avalonia.Headless.XUnit;
using Basalt.Core.Commands;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Designer.Model;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// The tree of controls beside the surface.
///
/// The surface can only reach what can be clicked, and a container is covered
/// by whatever is inside it: a Grid filled by a Button could not be selected
/// at all.
/// </summary>
public class ElementTreeTests
{
    private static DesignerSession SessionFor(string inner)
    {
        var xaml = $"""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="App.MainWindow">
              {inner}
            </Window>
            """;

        return new DesignerSession(XamlDocument.Parse(xaml), SourceLanguage.VisualBasic);
    }

    [AvaloniaFact]
    public void ShowsTheControlsNestedAsTheyAreWritten()
    {
        var session = SessionFor(
            """
            <Grid>
              <StackPanel>
                <Button Content="Uno" />
              </StackPanel>
            </Grid>
            """);

        var tree = new ElementTreePanel();
        tree.Show(session);

        Assert.Equal(["Grid", "StackPanel", "Button"], tree.Labels);
    }

    [AvaloniaFact]
    public void NamesAControlThatHasAName()
    {
        // In a form of nine buttons the type says nothing about which is
        // which, and the name is what the code behind refers to.
        var session = SessionFor("""<Grid><Button x:Name="OkButton" /></Grid>""");

        var tree = new ElementTreePanel();
        tree.Show(session);

        Assert.Contains("OkButton  (Button)", tree.Labels);
    }

    [AvaloniaFact]
    public void ReachesAContainerThatItsChildrenCover()
    {
        // The whole point: the Grid is behind the Button everywhere, so no
        // click can ever land on it.
        var session = SessionFor("""<Grid><Button Content="Uno" /></Grid>""");

        var grid = XamlDocument.ControlChildren(session.Document.Root).First();

        var tree = new ElementTreePanel();
        tree.Show(session);

        System.Xml.Linq.XElement? chosen = null;
        tree.ElementSelected += (_, element) => chosen = element;

        tree.SelectForTests(grid);

        Assert.Same(grid, chosen);
    }

    [AvaloniaFact]
    public void ShowsWhatTheSurfaceSelectedWithoutReportingItBack()
    {
        // Otherwise the two answer each other in a loop.
        var session = SessionFor("""<Grid><Button Content="Uno" /></Grid>""");

        var grid = XamlDocument.ControlChildren(session.Document.Root).First();
        var button = XamlDocument.ControlChildren(grid).First();

        var tree = new ElementTreePanel();
        tree.Show(session);

        var reports = 0;
        tree.ElementSelected += (_, _) => reports++;

        tree.Follow(button);

        Assert.Same(button, tree.SelectedElement);
        Assert.Equal(0, reports);
    }

    [AvaloniaFact]
    public void EmptiesWhenThereIsNoDesigner()
    {
        var tree = new ElementTreePanel();

        tree.Show(SessionFor("<Grid />"));
        tree.Show(null);

        Assert.Empty(tree.Labels);
    }
}

/// <summary>Commands the designer had but nothing could invoke.</summary>
public class DesignerCommandTests
{
    [Fact]
    public void OffersAligningAndZoomingAsCommands()
    {
        // The alignment arithmetic and its tests existed all along; no menu,
        // no shortcut and no palette entry reached any of it.
        var registry = IdeCommands.CreateRegistry();

        foreach (var id in new[]
        {
            IdeCommands.DesignerAlignLeft,
            IdeCommands.DesignerAlignRight,
            IdeCommands.DesignerAlignTop,
            IdeCommands.DesignerAlignBottom,
            IdeCommands.DesignerSameWidth,
            IdeCommands.DesignerSameHeight,
            IdeCommands.DesignerZoomIn,
            IdeCommands.DesignerZoomOut,
            IdeCommands.DesignerZoomReset,
            IdeCommands.DesignerZoomToFit,
            IdeCommands.DesignerSelectParent,
        })
        {
            Assert.Contains(registry.All, c => c.Id == id);
        }
    }

    [Fact]
    public void LeavesTheZoomKeysToNavigationAndBindsTheRest()
    {
        // Ctrl+- and Ctrl+Shift+- are Navigate Back and Forward, which were
        // here first. Zooming in and out is reached by Ctrl and the wheel,
        // by the menu, and by whatever key the user cares to bind.
        var registry = IdeCommands.CreateRegistry();

        Assert.Null(registry.GestureFor(IdeCommands.DesignerZoomIn));
        Assert.Null(registry.GestureFor(IdeCommands.DesignerZoomOut));

        Assert.Equal("Ctrl+0", registry.GestureFor(IdeCommands.DesignerZoomReset));
        Assert.Equal("Ctrl+9", registry.GestureFor(IdeCommands.DesignerZoomToFit));
    }
}
