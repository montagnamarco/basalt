using System.Xml.Linq;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Designer.Model;

namespace Basalt.Tests;

/// <summary>
/// Moving a control from one container into another.
///
/// Without this a control could only ever be nudged around inside the panel
/// it was first dropped in: rearranging a form meant deleting it and adding
/// it again elsewhere, losing everything that had been set on it.
/// </summary>
public class DesignerReparentTests
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

    private static XElement Named(DesignerSession s, string name) =>
        s.Document.Root.Descendants().First(e => e.Name.LocalName == name);

    private const string TwoPanels = """
        <Grid ColumnDefinitions="*,*">
          <StackPanel>
            <Button Content="Uno" Margin="10,20,0,0" />
          </StackPanel>
          <Canvas Grid.Column="1" />
        </Grid>
        """;

    [Fact]
    public void MovesAControlIntoAnotherPanel()
    {
        var session = SessionFor(TwoPanels);

        var button = Named(session, "Button");
        var canvas = Named(session, "Canvas");

        session.Reparent(button, canvas, 30, 40, 200, 200);

        Assert.Equal("Canvas", button.Parent?.Name.LocalName);
        Assert.Empty(Named(session, "StackPanel").Elements());
    }

    [Fact]
    public void KeepsTheSameElementRatherThanACopy()
    {
        // LINQ-to-XML copies an element that still belongs to a document
        // instead of moving it, so the selection would go on pointing at a
        // node no longer in the file.
        var session = SessionFor(TwoPanels);

        var button = Named(session, "Button");
        var canvas = Named(session, "Canvas");

        session.Reparent(button, canvas, 30, 40, 200, 200);

        Assert.Same(button, Named(session, "Button"));
    }

    [Fact]
    public void DropsThePositioningOfThePanelItLeft()
    {
        // A Margin that placed it in a StackPanel is not a gap the user asked
        // for, and carried into a Canvas it fights the coordinates.
        var session = SessionFor(TwoPanels);

        var button = Named(session, "Button");
        var canvas = Named(session, "Canvas");

        session.Reparent(button, canvas, 30, 40, 200, 200);

        Assert.Null(button.Attribute("Margin"));
        Assert.Equal("30", button.Attribute("Canvas.Left")?.Value);
        Assert.Equal("40", button.Attribute("Canvas.Top")?.Value);
    }

    [Fact]
    public void WritesTheNewContainersOwnPositioning()
    {
        // Into a Grid it is a cell, not coordinates.
        var session = SessionFor(
            """
            <Grid RowDefinitions="*,*">
              <Canvas><Button Canvas.Left="5" Canvas.Top="5" /></Canvas>
            </Grid>
            """);

        var button = Named(session, "Button");
        var grid = Named(session, "Grid");

        session.Reparent(button, grid, 10, 180, 200, 200);

        Assert.Null(button.Attribute("Canvas.Left"));
        Assert.Equal("1", button.Attribute("Grid.Row")?.Value);
    }

    [Fact]
    public void UndoesTheWholeMoveInOneStep()
    {
        // Detaching, clearing and re-placing is one action to the user.
        var session = SessionFor(TwoPanels);

        var button = Named(session, "Button");
        var canvas = Named(session, "Canvas");

        session.Reparent(button, canvas, 30, 40, 200, 200);
        session.Undo();

        Assert.Equal("StackPanel", Named(session, "Button").Parent?.Name.LocalName);
        Assert.Equal("10,20,0,0", Named(session, "Button").Attribute("Margin")?.Value);
    }

    [Fact]
    public void RefusesToPutAContainerInsideItself()
    {
        // Which would detach the very subtree it is being put into, losing it.
        var session = SessionFor(TwoPanels);

        var grid = Named(session, "Grid");
        var stack = Named(session, "StackPanel");

        session.Reparent(grid, stack, 0, 0, 100, 100);

        Assert.Equal("Grid", stack.Parent?.Name.LocalName);
        Assert.NotNull(Named(session, "Button"));
    }

    [Fact]
    public void LeavesAControlAloneWhenItIsDroppedWhereItAlreadyIs()
    {
        // No edit, so nothing to undo and no history entry for a non-move.
        var session = SessionFor(TwoPanels);

        var button = Named(session, "Button");
        var stack = Named(session, "StackPanel");

        session.Reparent(button, stack, 5, 5, 100, 100);

        Assert.Equal("10,20,0,0", button.Attribute("Margin")?.Value);
    }
}
