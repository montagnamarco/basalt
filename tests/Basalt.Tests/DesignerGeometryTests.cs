using System.Globalization;
using System.Xml.Linq;
using Basalt.Designer;

namespace Basalt.Tests;

/// <summary>
/// Moving and resizing a control by editing its XAML.
/// </summary>
public sealed class DesignerGeometryTests
{
    private static XElement InCanvas(string attributes = "") =>
        XElement.Parse($"<Canvas><Button {attributes}/></Canvas>").Elements().First();

    private static XElement InGrid(string attributes = "") =>
        XElement.Parse($"<Grid><Button {attributes}/></Grid>").Elements().First();

    private static void Run(IEnumerable<Basalt.Designer.Model.IDesignerEdit> edits)
    {
        foreach (var edit in edits) edit.Apply();
    }

    [Fact]
    public void InsideACanvasItWritesTheAttachedPosition()
    {
        var button = InCanvas("Canvas.Left=\"10\" Canvas.Top=\"20\"");

        Run(DesignerGeometry.Move(button, 5, -4));

        Assert.Equal("15", button.Attribute("Canvas.Left")!.Value);
        Assert.Equal("16", button.Attribute("Canvas.Top")!.Value);
    }

    [Fact]
    public void AnywhereElseItWritesTheMargin()
    {
        // A Grid ignores Canvas.Left entirely: writing it would move nothing
        // and leave an attribute the layout never reads.
        var button = InGrid("Margin=\"10,10,0,0\"");

        Run(DesignerGeometry.Move(button, 5, 5));

        Assert.Equal("15,15,-5,-5", button.Attribute("Margin")!.Value);
        Assert.Null(button.Attribute("Canvas.Left"));
    }

    [Fact]
    public void AControlWithNoPositionStartsFromZero()
    {
        var button = InCanvas();

        Run(DesignerGeometry.Move(button, 12, 8));

        Assert.Equal("12", button.Attribute("Canvas.Left")!.Value);
        Assert.Equal("8", button.Attribute("Canvas.Top")!.Value);
    }

    [Theory]
    [InlineData("8", 8, 8, 8, 8)]
    [InlineData("4,2", 4, 2, 4, 2)]
    [InlineData("1,2,3,4", 1, 2, 3, 4)]
    public void TheShorthandMarginIsUnderstood(
        string written, double left, double top, double right, double bottom)
    {
        // XAML lets one number mean all four sides and two mean horizontal
        // then vertical. Reading "8" as a single left margin would move the
        // control three sides at once on the first drag.
        var margin = DesignerGeometry.MarginOf(InGrid($"Margin=\"{written}\""));

        Assert.Equal((left, top, right, bottom), margin);
    }

    [Fact]
    public void ResizingWritesWidthAndHeight()
    {
        var button = InCanvas();

        Run(DesignerGeometry.Resize(button, 120.4, 40.6));

        Assert.Equal("120", button.Attribute("Width")!.Value);
        Assert.Equal("41", button.Attribute("Height")!.Value);
    }

    [Fact]
    public void ResizingFromTheLeftAlsoMovesTheOrigin()
    {
        // Dragging the left handle makes the control wider and starts it
        // further left; only writing the width would grow it rightwards, away
        // from the handle being dragged.
        var button = InCanvas("Canvas.Left=\"50\"");

        Run(DesignerGeometry.Resize(button, 80, 30, dx: -20, dy: 0));

        Assert.Equal("80", button.Attribute("Width")!.Value);
        Assert.Equal("30", button.Attribute("Height")!.Value);
        Assert.Equal("30", button.Attribute("Canvas.Left")!.Value);
    }

    [Fact]
    public void ANegativeSizeIsNotWritten()
    {
        // Dragging a handle past the opposite edge asks for a negative size,
        // which Avalonia throws on rather than clamping.
        var button = InCanvas();

        Run(DesignerGeometry.Resize(button, -30, -10));

        Assert.Equal("0", button.Attribute("Width")!.Value);
        Assert.Equal("0", button.Attribute("Height")!.Value);
    }

    [Fact]
    public void NumbersAreWrittenTheWayXamlReadsThem()
    {
        // On a machine set to Italian a comma is the decimal point, and
        // "12,5" in XAML is two numbers rather than one.
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("it-IT");

            var button = InCanvas();
            Run(DesignerGeometry.Move(button, 12.5, 0));

            Assert.Equal("12.5", button.Attribute("Canvas.Left")!.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
