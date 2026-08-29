using System.Xml.Linq;
using Basalt.Designer;
using Basalt.Designer.Model;

namespace Basalt.Tests;

/// <summary>
/// Showing where inside a container a control will land.
/// </summary>
/// <remarks>
/// The surface outlined the whole container, which says which panel takes the
/// control and nothing about where in it: a Grid of four cells looked the same
/// wherever the pointer was, and the cell it went into was a surprise.
/// </remarks>
public class DropHintTests
{
    private static XElement Parse(string xaml) =>
        XamlDocument.Parse($"""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="App.W">
              {xaml}
            </Window>
            """).Root.Elements().First();

    private static DesignerLayout.DropHint Hint(
        string xaml, double x, double y, double w = 400, double h = 300,
        IReadOnlyList<DesignerLayout.Rect>? children = null) =>
        DesignerLayout.HintFor(
            Parse(xaml),
            new DesignerLayout.Point(x, y),
            new DesignerLayout.Size(w, h),
            children ?? []);

    [Fact]
    public void MarksTheCellOfAGridRatherThanTheWholeOfIt()
    {
        // Dropped bottom-right of a 2x2: the hint must cover that quarter
        // alone, or the feedback says nothing the outline did not.
        var hint = Hint("""<Grid RowDefinitions="*,*" ColumnDefinitions="*,*" />""", 300, 200);

        Assert.NotNull(hint.Region);

        var region = hint.Region!.Value;

        Assert.Equal(200, region.X);
        Assert.Equal(150, region.Y);
        Assert.Equal(200, region.Width);
        Assert.Equal(150, region.Height);
    }

    [Fact]
    public void SizesTheCellByItsTrackRatherThanEvenly()
    {
        // "Auto,3*": the second column is three quarters of the width, and a
        // hint drawn as half of it would point at the wrong place.
        var hint = Hint("""<Grid ColumnDefinitions="Auto,3*" />""", 300, 10);

        var region = hint.Region!.Value;

        Assert.Equal(100, region.X);
        Assert.Equal(300, region.Width);
    }

    [Fact]
    public void NamesTheCellSoItNeedNotBeCounted()
    {
        var hint = Hint("""<Grid RowDefinitions="*,*" ColumnDefinitions="*,*" />""", 300, 200);

        Assert.Equal("riga 1, col 1", hint.Label);
    }

    [Fact]
    public void SaysNothingAboutACellWhenTheGridHasOnlyOne()
    {
        // "riga 0, col 0" on a one-cell grid is noise.
        var hint = Hint("<Grid />", 100, 100);

        Assert.Null(hint.Label);
    }

    [Fact]
    public void ShowsTheStripADockedControlWouldTake()
    {
        // Against the left edge: a band down that side, not the whole panel.
        var hint = Hint("<DockPanel />", 10, 150);

        var region = hint.Region!.Value;

        Assert.Equal(0, region.X);
        Assert.Equal(300, region.Height);
        Assert.True(region.Width < 400, "a docked control does not take the whole width");
        Assert.Equal("Left", hint.Label);
    }

    [Fact]
    public void DrawsALineWhereAStackWouldInsert()
    {
        // In a stack the question is the order, not the place: a rectangle
        // cannot say "between these two".
        var children = new[]
        {
            new DesignerLayout.Rect(0, 0, 400, 40),
            new DesignerLayout.Rect(0, 40, 400, 40),
        };

        // Below the middle of the second child, so it goes after it.
        var hint = Hint("<StackPanel />", 10, 70, children: children);

        Assert.Null(hint.Region);

        var line = hint.Line!.Value;

        Assert.Equal(80, line.From.Y);
        Assert.Equal(80, line.To.Y);
    }

    [Fact]
    public void PutsTheLineBeforeAChildThePointerHasNotReached()
    {
        var children = new[]
        {
            new DesignerLayout.Rect(0, 0, 400, 40),
            new DesignerLayout.Rect(0, 40, 400, 40),
        };

        // Above the middle of the first child: it goes first.
        var hint = Hint("<StackPanel />", 10, 5, children: children);

        Assert.Equal(0, hint.Line!.Value.From.Y);
    }

    [Fact]
    public void TurnsTheLineSidewaysForAHorizontalStack()
    {
        // A row of buttons inserts to the left or the right of one, so the
        // line has to be vertical.
        var children = new[] { new DesignerLayout.Rect(0, 0, 100, 300) };

        var hint = Hint("""<StackPanel Orientation="Horizontal" />""", 80, 10, children: children);

        var line = hint.Line!.Value;

        Assert.Equal(100, line.From.X);
        Assert.Equal(100, line.To.X);
        Assert.NotEqual(line.From.Y, line.To.Y);
    }

    [Fact]
    public void ShowsTheStartOfAnEmptyStack()
    {
        // Nothing to sit between yet, and a panel with no feedback at all
        // reads as one that will not take the control.
        var hint = Hint("<StackPanel />", 10, 100);

        Assert.NotNull(hint.Line);
        Assert.Equal(0, hint.Line!.Value.From.Y);
    }
}
