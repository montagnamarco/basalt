using System.Xml.Linq;
using Avalonia.Headless.XUnit;
using Basalt.Designer;
using Basalt.Designer.Model;
using Box = Basalt.Designer.AlignmentArithmetic.Box;

namespace Basalt.Tests;

/// <summary>
/// Lining several selected controls up with one another.
/// </summary>
public sealed class AlignmentCommandTests
{
    [Fact]
    public void TheLastSelectedIsTheOneTheOthersMoveTo()
    {
        // As in Visual Studio: the anchor is whichever the user picked most
        // recently, because that is the one they were looking at.
        var boxes = new[] { new Box(10, 0, 40, 20), new Box(100, 50, 40, 20) };

        var adjusted = AlignmentArithmetic.Align(boxes, AlignmentCommand.Left);

        Assert.Equal(90, adjusted[0].Dx);
        Assert.Equal(0, adjusted[1].Dx);
    }

    [Fact]
    public void RightAlignsTheFarEdges()
    {
        var boxes = new[] { new Box(10, 0, 40, 20), new Box(100, 50, 80, 20) };

        var adjusted = AlignmentArithmetic.Align(boxes, AlignmentCommand.Right);

        // The anchor's right edge is 180; a 40-wide box must start at 140.
        Assert.Equal(130, adjusted[0].Dx);
    }

    [Fact]
    public void CentringUsesTheMiddleOfEach()
    {
        var boxes = new[] { new Box(0, 0, 40, 20), new Box(100, 50, 80, 20) };

        var adjusted = AlignmentArithmetic.Align(boxes, AlignmentCommand.HorizontalCentre);

        // The anchor's centre is 140; a 40-wide box centres there from 120.
        Assert.Equal(120, adjusted[0].Dx);
    }

    [Fact]
    public void VerticalCommandsLeaveTheHorizontalAlone()
    {
        var boxes = new[] { new Box(10, 0, 40, 20), new Box(100, 50, 40, 20) };

        var adjusted = AlignmentArithmetic.Align(boxes, AlignmentCommand.Top);

        Assert.Equal(0, adjusted[0].Dx);
        Assert.Equal(50, adjusted[0].Dy);
    }

    [Fact]
    public void SameWidthTakesTheAnchorsWidth()
    {
        var boxes = new[] { new Box(10, 0, 40, 20), new Box(100, 50, 90, 30) };

        var adjusted = AlignmentArithmetic.Align(boxes, AlignmentCommand.SameWidth);

        Assert.Equal(90, adjusted[0].Width);

        // And leaves the height alone: matching both would be two commands.
        Assert.Equal(20, adjusted[0].Height);
    }

    [Fact]
    public void OneControlAlignsToNothing()
    {
        Assert.Empty(AlignmentArithmetic.Align([new Box(0, 0, 10, 10)], AlignmentCommand.Left));
    }

    [AvaloniaFact]
    public void AligningIsOneUndo()
    {
        // Three controls moving is six attributes. Undone separately they
        // leave the layout half-aligned, which is worse than either state.
        var document = XamlDocument.Parse(
            """
            <Window xmlns="https://github.com/avaloniaui">
              <Canvas>
                <Button Canvas.Left="10" Canvas.Top="0" Width="40" Height="20" />
                <Button Canvas.Left="60" Canvas.Top="30" Width="40" Height="20" />
                <Button Canvas.Left="100" Canvas.Top="60" Width="40" Height="20" />
              </Canvas>
            </Window>
            """);

        var session = new DesignerSession(document, Basalt.Core.Model.SourceLanguage.VisualBasic);

        var buttons = document.Root.Descendants()
            .Where(e => e.Name.LocalName == "Button").ToList();

        session.SelectMany(buttons);

        session.AlignSelection(
            [new Box(10, 0, 40, 20), new Box(60, 30, 40, 20), new Box(100, 60, 40, 20)],
            AlignmentCommand.Left);

        Assert.Equal("100", buttons[0].Attribute("Canvas.Left")?.Value);
        Assert.Equal("100", buttons[1].Attribute("Canvas.Left")?.Value);

        session.Undo();

        Assert.Equal("10", buttons[0].Attribute("Canvas.Left")?.Value);
        Assert.Equal("60", buttons[1].Attribute("Canvas.Left")?.Value);
    }

    [AvaloniaFact]
    public void ShiftClickingTogglesAnElementInAndOut()
    {
        var document = XamlDocument.Parse(
            """
            <Window xmlns="https://github.com/avaloniaui">
              <Canvas><Button /><Button /></Canvas>
            </Window>
            """);

        var session = new DesignerSession(document, Basalt.Core.Model.SourceLanguage.VisualBasic);

        var buttons = document.Root.Descendants()
            .Where(e => e.Name.LocalName == "Button").ToList();

        session.Select(buttons[0]);
        session.ToggleSelection(buttons[1]);

        Assert.Equal(2, session.SelectedElements.Count);

        // The last picked is the one the property panel shows.
        Assert.Same(buttons[1], session.Selection);

        // Clicking it again takes it out rather than starting over.
        session.ToggleSelection(buttons[1]);

        Assert.Single(session.SelectedElements);
        Assert.Same(buttons[0], session.Selection);
    }
}
