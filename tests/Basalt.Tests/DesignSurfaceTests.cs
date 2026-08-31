using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Designer;
using Basalt.Designer.Model;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// Dragging, resizing and nudging on the design surface.
/// </summary>
/// <remarks>
/// Driven with real pointer and key events on the window rather than by
/// calling the handlers: a test that raises the event itself skips focus,
/// tunnelling, and anyone who might mark it handled — which is how the last
/// input fault got through.
/// </remarks>
public sealed class DesignSurfaceTests
{
    private const string Xaml =
        """
        <Window xmlns="https://github.com/avaloniaui">
          <Canvas>
            <Button Canvas.Left="20" Canvas.Top="30" Width="80" Height="24" Content="Go" />
          </Canvas>
        </Window>
        """;

    /// <summary>A panel inside a panel, for testing where a drop lands.</summary>
    private const string Nested =
        """
        <Window xmlns="https://github.com/avaloniaui">
          <Canvas>
            <StackPanel Canvas.Left="40" Canvas.Top="40" Width="120" Height="80">
              <Button Content="Inner" />
            </StackPanel>
          </Canvas>
        </Window>
        """;

    /// <summary>Two controls, so there is something to align to.</summary>
    private const string TwoControls =
        """
        <Window xmlns="https://github.com/avaloniaui">
          <Canvas>
            <Button Canvas.Left="20" Canvas.Top="30" Width="80" Height="24" Content="Go" />
            <Button Canvas.Left="150" Canvas.Top="120" Width="80" Height="24" Content="Stop" />
          </Canvas>
        </Window>
        """;

    private static (Window Window, DesignSurface Surface, DesignerSession Session) Open(
        string? xaml = null)
    {
        var session = new DesignerSession(
            XamlDocument.Parse(xaml ?? Xaml), Basalt.Core.Model.SourceLanguage.VisualBasic);

        var surface = new DesignSurface { Session = session };
        var window = new Window { Width = 400, Height = 300, Content = surface };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, surface, session);
    }

    /// <summary>The button in the document, which is what every test moves.</summary>
    private static XElement Button(DesignerSession session) =>
        session.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "Button");

    /// <summary>Where the button is drawn, in window coordinates.</summary>
    /// <summary>
    /// A point on the form, in the window's own coordinates.
    /// </summary>
    /// <remarks>
    /// The rulers push the content aside, so a position read off the XAML is
    /// not where that point is on screen. Converted here rather than in every
    /// gesture: the offset belongs to the surface, not to any one test.
    /// </remarks>
    private static Point OnForm(double x, double y) =>
        new(x + DesignerRulers.Thickness, y + DesignerRulers.Thickness);

    /// <summary>
    /// The middle of the button, in the window's own coordinates.
    /// </summary>
    /// <remarks>
    /// The rulers push the content aside, so a position taken from the XAML
    /// is not where that control is on screen. Added here rather than in each
    /// test: the offset is a property of the surface, not of any one gesture.
    /// </remarks>
    private static Point Centre(DesignerSession session) =>
        OnForm(Left(session) + 40, Top(session) + 12);

    private static double Left(DesignerSession session) =>
        double.Parse(Button(session).Attribute("Canvas.Left")?.Value ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);

    private static double Top(DesignerSession session) =>
        double.Parse(Button(session).Attribute("Canvas.Top")?.Value ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);

    [AvaloniaFact]
    public void ThePreviewRenders()
    {
        var (_, surface, session) = Open();

        var result = session.Render();

        Assert.True(result.Succeeded, result.Error);
        Assert.NotNull(surface.Content);

    }

    [AvaloniaFact]
    public void AClickSelects()
    {
        var (window, _, session) = Open();

        window.MouseDown(Centre(session), MouseButton.Left);
        window.MouseUp(Centre(session), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(session.Selection);
        Assert.Equal("Button", session.Selection!.Name.LocalName);
    }

    [AvaloniaFact]
    public void AClickThatDoesNotMoveChangesNothing()
    {
        // A hand on a trackpad never presses and releases at the same point,
        // so without a threshold every click nudged the control.
        var (window, _, session) = Open();

        var at = Centre(session);

        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(at + new Vector(1, 1));
        window.MouseUp(at + new Vector(1, 1), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(20, Left(session));
        Assert.Equal(30, Top(session));
    }

    [AvaloniaFact]
    public void DraggingTheBodyMovesTheControl()
    {
        var (window, _, session) = Open();

        var from = Centre(session);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(25, 15));
        window.MouseUp(from + new Vector(25, 15), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(45, Left(session));
        Assert.Equal(45, Top(session));
    }

    [AvaloniaFact]
    public void AWholeDragIsOneUndo()
    {
        // A move writes two attributes. Undone separately they leave the
        // control somewhere it never was, which reads as a broken undo.
        var (window, _, session) = Open();

        var from = Centre(session);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(25, 15));
        window.MouseUp(from + new Vector(25, 15), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        session.Undo();

        Assert.Equal(20, Left(session));
        Assert.Equal(30, Top(session));
    }

    [AvaloniaFact]
    public void EscapeAbandonsADragInProgress()
    {
        var (window, _, session) = Open();

        var from = Centre(session);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(40, 40));
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.MouseUp(from + new Vector(40, 40), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(20, Left(session));
        Assert.Equal(30, Top(session));
    }

    [AvaloniaFact]
    public void AnArrowKeyNudgesByOne()
    {
        var (window, _, session) = Open();

        window.MouseDown(Centre(session), MouseButton.Left);
        window.MouseUp(Centre(session), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(21, Left(session));
    }

    [AvaloniaFact]
    public void ShiftAndAnArrowKeyNudgeByTen()
    {
        var (window, _, session) = Open();

        window.MouseDown(Centre(session), MouseButton.Left);
        window.MouseUp(Centre(session), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(40, Top(session));
    }

    [AvaloniaFact]
    public void DeleteRemovesTheSelection()
    {
        var (window, _, session) = Open();

        window.MouseDown(Centre(session), MouseButton.Left);
        window.MouseUp(Centre(session), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(session.Document.Root.Descendants(),
            e => e.Name.LocalName == "Button");
    }

    [AvaloniaFact]
    public void DraggingTheBottomRightHandleResizes()
    {
        var (window, surface, session) = Open();

        window.MouseDown(Centre(session), MouseButton.Left);
        window.MouseUp(Centre(session), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // Read from the control rather than computed from the XAML: the theme
        // decides the real height, and hard-coding it made this test fail the
        // day the inputs were made smaller — a change with nothing to do with
        // resizing.
        var drawn = surface.GetVisualDescendants()
            .OfType<Avalonia.Controls.Button>()
            .First(b => (b.Content as string) == "Go");

        // Measured rather than computed. Where a control is drawn depends on
        // the rulers, on the layout centring the preview, and on the theme's
        // idea of how tall a button is: adding those up by hand is how this
        // test breaks for reasons that have nothing to do with resizing.
        var box = surface.SelectionBoundsForTests!.Value;

        var corner = new Point(box.Right, box.Bottom);

        window.MouseDown(corner, MouseButton.Left);
        window.MouseMove(corner + new Vector(20, 10));
        window.MouseUp(corner + new Vector(20, 10), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var button = Button(session);

        Assert.Equal("100", button.Attribute("Width")?.Value);

        // Ten taller than it was drawn, whatever the theme made that.
        Assert.Equal(
            Math.Round(drawn.Bounds.Height + 10).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            button.Attribute("Height")?.Value);
    }

    [AvaloniaFact]
    public void DraggingTheTopLeftHandleMovesTheOriginToo()
    {
        // Only writing the size would grow the control away from the handle
        // being dragged, which is the opposite of what the gesture means.
        var (window, surface, session) = Open();

        window.MouseDown(Centre(session), MouseButton.Left);
        window.MouseUp(Centre(session), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // Measured, for the same reason as the bottom-right handle: where a
        // control is drawn is not what the XAML says about it.
        var corner = surface.SelectionBoundsForTests!.Value.Position;

        window.MouseDown(corner, MouseButton.Left);
        window.MouseMove(corner + new Vector(-10, -6));
        window.MouseUp(corner + new Vector(-10, -6), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(10, Left(session));
        Assert.Equal(24, Top(session));
        Assert.Equal("90", Button(session).Attribute("Width")?.Value);
    }

    [AvaloniaFact]
    public void TheHandlesAreDrawn()
    {
        // Rendered and looked at: eight small squares in the right places is
        // exactly the kind of thing that passes every numeric assertion and
        // still comes out wrong on screen.
        var (window, _, session) = Open();

        window.MouseDown(Centre(session), MouseButton.Left);
        window.MouseUp(Centre(session), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var target = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(400, 300));
        target.Render(window);

        var path = Path.Combine(Path.GetTempPath(), "basalt-designsurface.png");

        using (var file = File.Create(path))
            target.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        Assert.True(new FileInfo(path).Length > 1000, "the render is empty");
    }

    [AvaloniaFact]
    public void ADraggedControlSnapsToItsNeighbour()
    {
        // Dropped three pixels from the other's left edge, which is invisible
        // while dragging and plainly wrong once the pointer is up.
        var (window, _, session) = Open(TwoControls);

        var from = Centre(session);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(127, 0));
        window.MouseUp(from + new Vector(127, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // 20 + 127 is 147, and the other button's left edge is at 150.
        Assert.Equal(150, Left(session));
    }

    [AvaloniaFact]
    public void HoldingAltPlacesItExactly()
    {
        var (window, _, session) = Open(TwoControls);

        var from = Centre(session);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(127, 0), RawInputModifiers.Alt);
        window.MouseUp(from + new Vector(127, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(147, Left(session));
    }

    [AvaloniaFact]
    public void ShiftClickingSelectsBoth()
    {
        var (window, _, session) = Open(TwoControls);

        window.MouseDown(OnForm(60, 42), MouseButton.Left);
        window.MouseUp(OnForm(60, 42), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        window.MouseDown(OnForm(190, 132), MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(OnForm(190, 132), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, session.SelectedElements.Count);
    }

    [AvaloniaFact]
    public void AligningMovesTheOthersToTheLastPicked()
    {
        var (window, surface, session) = Open(TwoControls);

        window.MouseDown(OnForm(60, 42), MouseButton.Left);
        window.MouseUp(OnForm(60, 42), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        window.MouseDown(OnForm(190, 132), MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(OnForm(190, 132), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        surface.Align(AlignmentCommand.Left);
        Dispatcher.UIThread.RunJobs();

        // Both now start where the second one did.
        var lefts = session.Document.Root.Descendants()
            .Where(e => e.Name.LocalName == "Button")
            .Select(e => e.Attribute("Canvas.Left")?.Value)
            .ToList();

        Assert.Equal(["150", "150"], lefts);
    }

    [AvaloniaFact]
    public void AClickWithoutShiftStartsOver()
    {
        var (window, _, session) = Open(TwoControls);

        window.MouseDown(OnForm(60, 42), MouseButton.Left);
        window.MouseUp(OnForm(60, 42), MouseButton.Left);
        window.MouseDown(OnForm(190, 132), MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(OnForm(190, 132), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        window.MouseDown(OnForm(60, 42), MouseButton.Left);
        window.MouseUp(OnForm(60, 42), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(session.SelectedElements);
    }

    [AvaloniaFact]
    public void TabWalksTheControls()
    {
        var (window, _, session) = Open(TwoControls);

        window.MouseDown(OnForm(60, 42), MouseButton.Left);
        window.MouseUp(OnForm(60, 42), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var first = session.Selection;

        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Dispatcher.UIThread.RunJobs();

        Assert.NotSame(first, session.Selection);

        // Around the whole cycle and back: the container counts too, since
        // selecting a Canvas to set its background is a real thing to want.
        var seen = new List<System.Xml.Linq.XElement?> { first, session.Selection };

        for (var i = 0; i < 8 && !ReferenceEquals(session.Selection, first); i++)
        {
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();

            seen.Add(session.Selection);
        }

        Assert.Same(first, session.Selection);

        // Every control was visited on the way round rather than skipped.
        Assert.True(seen.Distinct().Count() >= 3, "Tab did not reach every control");
    }

    [AvaloniaFact]
    public void ShiftTabWalksBackwards()
    {
        var (window, _, session) = Open(TwoControls);

        window.MouseDown(OnForm(60, 42), MouseButton.Left);
        window.MouseUp(OnForm(60, 42), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var first = session.Selection;

        window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
        Dispatcher.UIThread.RunJobs();

        Assert.NotSame(first, session.Selection);
    }

    [AvaloniaFact]
    public void ABandOverBothPicksBoth()
    {
        var (window, _, session) = Open(TwoControls);

        // From above and left of the first to below and right of the second.
        window.MouseDown(OnForm(5, 5), MouseButton.Left);
        window.MouseMove(OnForm(300, 200));
        window.MouseUp(OnForm(300, 200), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, session.SelectedElements.Count);
    }

    [AvaloniaFact]
    public void ABandOnlyPicksWhatItCoversWholly()
    {
        // Merely touching is not enough: a band drawn across a busy layout
        // brushes half of it, and picking up everything it grazed is never
        // what was meant.
        var (window, _, session) = Open(TwoControls);

        // Covers the first, clips the corner of the second.
        window.MouseDown(OnForm(5, 5), MouseButton.Left);
        window.MouseMove(OnForm(160, 130));
        window.MouseUp(OnForm(160, 130), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(session.SelectedElements);
        Assert.Equal("Go", session.Selection?.Attribute("Content")?.Value);
    }

    [AvaloniaFact]
    public void AClickOnEmptySpaceClears()
    {
        var (window, _, session) = Open(TwoControls);

        window.MouseDown(OnForm(60, 42), MouseButton.Left);
        window.MouseUp(OnForm(60, 42), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(session.Selection);

        window.MouseDown(OnForm(320, 250), MouseButton.Left);
        window.MouseUp(OnForm(320, 250), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(session.Selection);
    }

    [AvaloniaFact]
    public void ADropFindsTheInnermostContainer()
    {
        // Dragging into a panel nested in another must put the control where
        // the pointer is, not in the outermost thing that accepts children.
        var (window, surface, _) = Open(Nested);

        var container = surface.ContainerAt(new Point(100, 100));

        Assert.NotNull(container);
        Assert.Equal("StackPanel", container!.Name.LocalName);
    }

    [AvaloniaFact]
    public void ADropOutsideEverythingFindsTheRoot()
    {
        var (window, surface, _) = Open(Nested);

        var container = surface.ContainerAt(new Point(300, 250));

        // The Canvas fills the window, so a point anywhere in it is still in
        // a container — just the outermost one.
        Assert.Equal("Canvas", container?.Name.LocalName);
    }

    [AvaloniaFact]
    public void ADroppedControlLandsInTheNestedPanel()
    {
        var (window, surface, session) = Open(Nested);

        var container = surface.ContainerAt(new Point(100, 100));

        session.InsertFromToolbox(
            new Basalt.Designer.Toolbox.ToolboxItem(
                "TextBox", "TextBox", "Common", "<TextBox />"),
            container);

        var panel = session.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "StackPanel");

        Assert.Contains(panel.Elements(), e => e.Name.LocalName == "TextBox");
    }

    [AvaloniaFact]
    public void ABandDraggedBackwardsDoesNotCrash()
    {
        // Rect's two-point constructor keeps the sign, so dragging up or to
        // the left gave a negative width — and Avalonia throws on that rather
        // than clamping. It brought the application down mid-drag, while
        // every test here dragged down and to the right.
        var (window, _, session) = Open(TwoControls);

        window.MouseDown(OnForm(300, 200), MouseButton.Left);
        window.MouseMove(OnForm(5, 5));
        window.MouseUp(OnForm(5, 5), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // And it still selects: a band is a band whichever way it is drawn.
        Assert.Equal(2, session.SelectedElements.Count);
    }

    [AvaloniaFact]
    public void ADragMovingLeftDoesNotCrash()
    {
        var (window, _, session) = Open(TwoControls);

        var from = Centre(session);

        // With Alt, so the snap does not have a say: dragging 15 left from
        // x=20 puts the edge within snapping distance of the Canvas at 0, and
        // this test is about the direction, not the alignment.
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(-15, -12), RawInputModifiers.Alt);
        window.MouseUp(from + new Vector(-15, -12), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(5, Left(session));
        Assert.Equal(18, Top(session));
    }

    [AvaloniaFact]
    public void TheVisualBasic6LookDrawsItsGridAndFilledHandles()
    {
        // Rendered and looked at. The dotted grid, the small filled handles
        // and the absent frame are most of why a Visual Basic 6 designer is
        // recognisable at a glance, and none of that is visible in an
        // assertion about a property.
        var (window, surface, session) = Open();

        surface.Look = DesignerLook.VisualBasic6;

        window.MouseDown(Centre(session), MouseButton.Left);
        window.MouseUp(Centre(session), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var target = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(400, 300));
        target.Render(window);

        var path = Path.Combine(Path.GetTempPath(), "basalt-vb6-look.png");

        using (var file = File.Create(path))
            target.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        Assert.True(new FileInfo(path).Length > 1000, "the render is empty");
    }

    [AvaloniaFact]
    public void TheVisualBasic6LookSnapsToItsGrid()
    {
        // VB6 snapped to the grid and drew it, so where a control would land
        // was visible before it was dropped. A snap with no grid to see is the
        // same behaviour with the explanation taken away.
        var look = DesignerLook.VisualBasic6;

        Assert.Equal(8, look.SnapToGrid(9));
        Assert.Equal(16, look.SnapToGrid(14));

        // And the modern look leaves a position exactly where it was put.
        Assert.Equal(9, DesignerLook.Modern.SnapToGrid(9));
    }
}
