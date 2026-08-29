using System.Xml.Linq;
using Basalt.Designer;
using Basalt.Designer.Model;
using Basalt.Designer.Toolbox;
using Basalt.Core.Model;

namespace Basalt.Tests;

/// <summary>
/// Placing a dropped control the way its container lays out.
///
/// The designer used to write a Margin whatever it was dropped into, which
/// inside a Grid is absolute positioning wearing a Grid's clothes: right on
/// the screen it was drawn on and wrong on every other.
/// </summary>
public class DesignerLayoutTests
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

    private static readonly ToolboxItem Button =
        new("Pulsante", "Button", "Comuni", """<Button Content="Pulsante" />""");

    /// <summary>Drops a button into the first container and returns it.</summary>
    private static XElement Drop(
        DesignerSession session, double x, double y, double width, double height)
    {
        var container = XamlDocument.ControlChildren(session.Document.Root).First();
        var inserted = session.InsertFromToolbox(Button, container);

        session.PlaceDrop(inserted, x, y, width, height);

        return inserted;
    }

    [Fact]
    public void PutsAControlInTheCellItWasDroppedOn()
    {
        // The point of the whole change: a grid of four cells, dropped in the
        // bottom right, should say so — not carry a Margin that happens to
        // land there at this window size.
        var session = SessionFor(
            """<Grid RowDefinitions="*,*" ColumnDefinitions="*,*" />""");

        var button = Drop(session, 300, 200, 400, 300);

        Assert.Equal("1", button.Attribute("Grid.Row")?.Value);
        Assert.Equal("1", button.Attribute("Grid.Column")?.Value);
        Assert.Null(button.Attribute("Margin"));
    }

    [Fact]
    public void MeasuresCellsByWeightRatherThanByCount()
    {
        // A drop 40% across a grid of "Auto,3*" is still in the second column:
        // splitting the width evenly would call it the first.
        var session = SessionFor("""<Grid ColumnDefinitions="Auto,3*" />""");

        var button = Drop(session, 40, 10, 100, 50);

        Assert.Equal("1", button.Attribute("Grid.Column")?.Value);
    }

    [Fact]
    public void SaysNothingAboutACellWhenThereIsOnlyOne()
    {
        // A Grid with no tracks declared is one cell, and "row 0, column 0"
        // on it is noise in the file.
        var session = SessionFor("<Grid />");

        var button = Drop(session, 30, 40, 200, 200);

        Assert.Null(button.Attribute("Grid.Row"));
        Assert.Null(button.Attribute("Grid.Column"));
    }

    [Fact]
    public void ReadsRowsWrittenAsChildElements()
    {
        // Both spellings are legal XAML, and a file written the long way
        // would otherwise have every drop land in row 0.
        var session = SessionFor(
            """
            <Grid>
              <Grid.RowDefinitions>
                <RowDefinition Height="*" />
                <RowDefinition Height="*" />
              </Grid.RowDefinitions>
            </Grid>
            """);

        var button = Drop(session, 10, 180, 200, 200);

        Assert.Equal("1", button.Attribute("Grid.Row")?.Value);
    }

    [Fact]
    public void KeepsCoordinatesInsideACanvas()
    {
        // A Canvas is the one container where absolute placement is the
        // right answer, and it must keep working.
        var session = SessionFor("<Canvas />");

        var button = Drop(session, 120, 60, 400, 300);

        Assert.Equal("120", button.Attribute("Canvas.Left")?.Value);
        Assert.Equal("60", button.Attribute("Canvas.Top")?.Value);
    }

    [Fact]
    public void DocksAgainstTheNearestEdgeOfADockPanel()
    {
        // Nearest in proportion, not in pixels: in a wide, short panel every
        // drop would otherwise dock to the top or the bottom.
        var session = SessionFor("<DockPanel />");

        var button = Drop(session, 10, 100, 400, 200);

        Assert.Equal("Left", button.Attribute("DockPanel.Dock")?.Value);
    }

    [Fact]
    public void LeavesAStackPanelToDoItsOwnStacking()
    {
        // A StackPanel puts children one after another; a Margin written to
        // say "here" would fight the panel rather than instruct it.
        var session = SessionFor("<StackPanel />");

        var button = Drop(session, 50, 90, 200, 300);

        Assert.Null(button.Attribute("Margin"));
        Assert.Null(button.Attribute("Grid.Row"));
    }

    [Fact]
    public void MovesBetweenCellsWhenDraggedInsideAGrid()
    {
        // Dragging used to add to the Margin, so a control walked further
        // from its cell with every drag instead of changing cell.
        var session = SessionFor(
            """<Grid RowDefinitions="*,*" ColumnDefinitions="*,*" />""");

        var button = Drop(session, 10, 10, 400, 300);

        Assert.Equal("0", button.Attribute("Grid.Column")?.Value);

        session.Select(button);
        session.DragSelectionTo(300, 200, 400, 300, 290, 190);

        Assert.Equal("1", button.Attribute("Grid.Row")?.Value);
        Assert.Equal("1", button.Attribute("Grid.Column")?.Value);
        Assert.Null(button.Attribute("Margin"));
    }
}

/// <summary>
/// Defining a grid's rows and columns from the designer.
///
/// Without this, laying out by cells means hand-editing the XAML, which is
/// the part the designer exists to spare.
/// </summary>
public class GridTrackEditingTests
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

    private static XElement Grid(DesignerSession session) =>
        XamlDocument.ControlChildren(session.Document.Root).First();

    [Fact]
    public void ReadsTheTracksAsTheyAreWritten()
    {
        // "Auto" has to come back as "Auto", not as the number it was
        // measured by when deciding which cell a drop was over.
        var session = SessionFor("""<Grid RowDefinitions="Auto,*,120" />""");
        session.Select(Grid(session));

        Assert.Equal(["Auto", "*", "120"], session.TracksOf(DesignerLayout.Track.Row));
    }

    [Fact]
    public void AddsASecondTrackWithoutSwallowingTheFirst()
    {
        // A Grid with nothing declared already has one implicit row; adding
        // one has to write both, or everything already in it lands in the new
        // one.
        var session = SessionFor("<Grid />");
        session.Select(Grid(session));

        session.AddTrack(DesignerLayout.Track.Row);

        Assert.Equal(["*", "*"], session.TracksOf(DesignerLayout.Track.Row));
    }

    [Fact]
    public void BringsControlsBackWhenTheirRowIsRemoved()
    {
        // Left pointing past the end they would all pile into the last row,
        // which reads as the designer having scrambled the form.
        var session = SessionFor(
            """
            <Grid RowDefinitions="*,*,*">
              <Button Grid.Row="2" />
              <TextBox Grid.Row="1" />
            </Grid>
            """);

        session.Select(Grid(session));
        session.RemoveTrack(DesignerLayout.Track.Row, 1);

        var grid = Grid(session);
        var button = grid.Elements().First(e => e.Name.LocalName == "Button");
        var box = grid.Elements().First(e => e.Name.LocalName == "TextBox");

        Assert.Equal(["*", "*"], session.TracksOf(DesignerLayout.Track.Row));

        // The one below moved up; the one that was in the removed row came
        // back to the row before it, which is row 0 and so written as nothing.
        Assert.Equal("1", button.Attribute("Grid.Row")?.Value);
        Assert.Null(box.Attribute("Grid.Row"));
    }

    [Fact]
    public void ReplacesTheLongSpellingRatherThanLeavingBoth()
    {
        // Two declarations of the same tracks is a file Avalonia refuses to
        // load, and the designer would have written exactly that.
        var session = SessionFor(
            """
            <Grid>
              <Grid.RowDefinitions>
                <RowDefinition Height="*" />
              </Grid.RowDefinitions>
            </Grid>
            """);

        session.Select(Grid(session));
        session.SetTracks(DesignerLayout.Track.Row, ["*", "Auto"]);

        var grid = Grid(session);

        Assert.Equal("*,Auto", grid.Attribute("RowDefinitions")?.Value);
        Assert.DoesNotContain(grid.Elements(), e => e.Name.LocalName == "Grid.RowDefinitions");
    }

    [Fact]
    public void TurnsATypoIntoASizeThatWillLoad()
    {
        // A size the layout cannot parse is XAML that will not open at all,
        // which is a worse answer than a star.
        var session = SessionFor("<Grid />");
        session.Select(Grid(session));

        session.SetTracks(DesignerLayout.Track.Column, ["*", "nonsense", "2*"]);

        Assert.Equal(["*", "*", "2*"], session.TracksOf(DesignerLayout.Track.Column));
    }

    [Fact]
    public void OffersTheGridAControlSitsInAsWellAsTheGridItself()
    {
        // The rows are wanted just as often while looking at a button inside
        // them as while looking at the grid.
        var session = SessionFor("""<Grid RowDefinitions="*,*"><Button /></Grid>""");

        var button = Grid(session).Elements().First(e => e.Name.LocalName == "Button");
        session.Select(button);

        Assert.NotNull(session.GridInScope);
        Assert.Equal(["*", "*"], session.TracksOf(DesignerLayout.Track.Row));
    }

    [Fact]
    public void UndoesATrackChangeInOneStep()
    {
        // Rewriting the tracks and shifting the controls is one action to the
        // user, so it has to be one to the undo stack.
        var session = SessionFor(
            """<Grid RowDefinitions="*,*,*"><Button Grid.Row="2" /></Grid>""");

        session.Select(Grid(session));
        session.RemoveTrack(DesignerLayout.Track.Row, 0);
        session.Undo();

        var grid = Grid(session);
        var button = grid.Elements().First(e => e.Name.LocalName == "Button");

        Assert.Equal("*,*,*", grid.Attribute("RowDefinitions")?.Value);
        Assert.Equal("2", button.Attribute("Grid.Row")?.Value);
    }
}

/// <summary>The rows and columns editor in the properties panel.</summary>
public class GridTrackEditorTests
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

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void StaysOutOfTheWayWhenNothingIsAGrid()
    {
        // A Canvas form should not be asked about rows it does not have.
        var session = SessionFor("<Canvas />");
        session.Select(XamlDocument.ControlChildren(session.Document.Root).First());

        var editor = new Basalt.Shell.Controls.GridTrackEditor();
        editor.Show(session);

        Assert.False(editor.IsVisible);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ShowsTheTracksOfTheSelectedGrid()
    {
        var session = SessionFor("""<Grid RowDefinitions="Auto,*" ColumnDefinitions="*,2*,Auto" />""");
        session.Select(XamlDocument.ControlChildren(session.Document.Root).First());

        var editor = new Basalt.Shell.Controls.GridTrackEditor();
        editor.Show(session);

        Assert.True(editor.IsVisible);
        Assert.Equal(["Auto", "*"], editor.Rows);
        Assert.Equal(["*", "2*", "Auto"], editor.Columns);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ShowsTheImplicitTrackOfAGridThatDeclaresNone()
    {
        // A bare Grid plainly has one row in it; a blank panel in front of one
        // reads as the editor having failed rather than as "none declared".
        var session = SessionFor("<Grid />");
        session.Select(XamlDocument.ControlChildren(session.Document.Root).First());

        var editor = new Basalt.Shell.Controls.GridTrackEditor();
        editor.Show(session);

        Assert.Equal(["*"], editor.Rows);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AddsAndRemovesThroughTheSession()
    {
        var session = SessionFor("<Grid />");
        session.Select(XamlDocument.ControlChildren(session.Document.Root).First());

        var editor = new Basalt.Shell.Controls.GridTrackEditor();
        editor.Show(session);

        editor.AddForTests(DesignerLayout.Track.Column);
        Assert.Equal(["*", "*"], editor.Columns);

        editor.RemoveForTests(DesignerLayout.Track.Column, 0);
        Assert.Equal(["*"], editor.Columns);
    }
}
