using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Designer.Model;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// The scales down the top and the left of the design surface.
/// </summary>
/// <remarks>
/// A form is laid out against numbers, and without a ruler those numbers live
/// only in the property grid: reading a size means selecting the control and
/// looking away from the form.
/// </remarks>
public class DesignerRulerTests
{
    private const string Form = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="App.MainWindow" Width="400" Height="260">
          <Canvas>
            <Button x:Name="Ok" Canvas.Left="60" Canvas.Top="40" Width="120" Height="32" />
          </Canvas>
        </Window>
        """;

    private static (DesignSurface Surface, Window Host, DesignerSession Session) Shown()
    {
        var session = new DesignerSession(XamlDocument.Parse(Form), SourceLanguage.VisualBasic);
        var surface = new DesignSurface { Session = session };
        var host = new Window { Content = surface, Width = 460, Height = 320 };

        host.Show();
        host.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();

        return (surface, host, session);
    }

    private static void Settle(Window host)
    {
        host.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
    }

    [AvaloniaFact]
    public void AreShownByDefault()
    {
        // A form is laid out against numbers, so the numbers are on by
        // default rather than hidden behind a setting.
        var (surface, host, _) = Shown();

        Assert.True(surface.ShowRulers);
        Assert.True(surface.RulersForTests.IsVisible);

        host.Close();
    }

    [AvaloniaFact]
    public void CanBeTurnedOffWhenThereIsNoRoom()
    {
        var (surface, host, _) = Shown();

        surface.ShowRulers = false;

        Assert.False(surface.RulersForTests.IsVisible);

        host.Close();
    }

    [AvaloniaFact]
    public void MarkTheSelectionWhereItActuallyIs()
    {
        // The question a ruler is really asked: how wide is this, and where
        // does it sit. The answer must be the control's own coordinates, not
        // wherever the layout happened to put the preview.
        var (surface, host, session) = Shown();

        session.Select(session.Document.Root.Descendants().First(e => e.Name.LocalName == "Button"));
        surface.RefreshNow();
        Settle(host);

        var highlight = surface.RulersForTests.Highlight;

        Assert.NotNull(highlight);
        Assert.Equal(60, highlight!.Value.X, 1);
        Assert.Equal(40, highlight.Value.Y, 1);
        Assert.Equal(120, highlight.Value.Width, 1);

        host.Close();
    }

    [AvaloniaFact]
    public void MarkNothingWhenNothingIsSelected()
    {
        var (surface, host, _) = Shown();

        Settle(host);

        Assert.Null(surface.RulersForTests.Highlight);

        host.Close();
    }

    [AvaloniaFact]
    public void FollowTheZoom()
    {
        // The numbers keep meaning device-independent pixels; it is the
        // spacing on screen that changes.
        var (surface, host, _) = Shown();

        surface.Zoom = 2;
        Settle(host);

        Assert.Equal(2, surface.RulersForTests.Zoom);

        host.Close();
    }

    [AvaloniaFact]
    public void KeepTheSelectionsOwnCoordinatesAtAnyZoom()
    {
        // The whole point of not scaling the rulers with the content: a
        // control 120 wide is 120 wide however far the view is magnified.
        var (surface, host, session) = Shown();

        session.Select(session.Document.Root.Descendants().First(e => e.Name.LocalName == "Button"));
        surface.RefreshNow();
        Settle(host);

        surface.Zoom = 0.5;
        Settle(host);

        Assert.Equal(120, surface.RulersForTests.Highlight!.Value.Width, 1);

        host.Close();
    }

    [Theory]
    [InlineData(0.25, 500)]
    [InlineData(1.0, 100)]
    [InlineData(4.0, 20)]
    public void SpaceTheMarksSoTheyStayReadable(double zoom, double expected)
    {
        // A fixed step gives marks touching each other when zoomed out and
        // one lonely mark when zoomed in.
        Assert.Equal(expected, DesignerRulers.StepFor(zoom));
    }

    [AvaloniaFact]
    public void SelectTheControlUnderThePointerDespiteTheOffset()
    {
        // The rulers push the content aside, so a click has eighteen pixels
        // to account for before anything else: getting this wrong makes
        // every selection land somewhere other than where it was made.
        var (surface, host, _) = Shown();

        Settle(host);

        var withRulers = surface.ContainerAtForTests(new Point(10, 10));

        surface.ShowRulers = false;
        Settle(host);

        Assert.Equal(
            withRulers?.Name.LocalName,
            surface.ContainerAtForTests(new Point(10, 10))?.Name.LocalName);

        host.Close();
    }
}

/// <summary>
/// Clicking after the view has been scrolled or magnified.
/// </summary>
/// <remarks>
/// Hit-testing measures where a control is drawn, with TranslatePoint against
/// the surface, so what comes back already carries the pan, the zoom and the
/// rulers. Converting the pointer to the form's coordinates first removed all
/// three a second time: after scrolling, a click landed on whatever sat that
/// far away — usually the panel behind the control being aimed at.
/// </remarks>
public class DesignerHitTestingTests
{
    private const string Form = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="App.MainWindow" Width="300" Height="200">
          <Canvas>
            <Button x:Name="Ok" Canvas.Left="40" Canvas.Top="30" Width="100" Height="28" />
          </Canvas>
        </Window>
        """;

    private static (DesignSurface Surface, Window Host, DesignerSession Session) Shown()
    {
        var session = new DesignerSession(XamlDocument.Parse(Form), SourceLanguage.VisualBasic);
        var surface = new DesignSurface { Session = session };
        var host = new Window { Content = surface, Width = 460, Height = 340 };

        host.Show();
        host.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();

        session.Select(session.Document.Root.Descendants().First(e => e.Name.LocalName == "Button"));
        surface.RefreshNow();

        host.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        return (surface, host, session);
    }

    private static void Settle(Window host)
    {
        host.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ClickingTheControlSelectsItBeforeAnythingIsScrolled()
    {
        var (surface, host, _) = Shown();

        var middle = surface.SelectionBoundsForTests!.Value.Center;

        Assert.Equal("Button", surface.SelectedByClickForTests(middle));

        host.Close();
    }

    [AvaloniaFact]
    public void ClickingTheControlStillSelectsItAfterScrolling()
    {
        // The one that was broken: the control moves, the click follows it,
        // and the wrong thing was selected.
        var (surface, host, _) = Shown();

        surface.PanBy(-60, -40);
        Settle(host);

        var middle = surface.SelectionBoundsForTests!.Value.Center;

        Assert.Equal("Button", surface.SelectedByClickForTests(middle));

        host.Close();
    }

    [AvaloniaFact]
    public void ClickingTheControlStillSelectsItWhenMagnified()
    {
        var (surface, host, _) = Shown();

        surface.Zoom = 2;
        Settle(host);

        var middle = surface.SelectionBoundsForTests!.Value.Center;

        Assert.Equal("Button", surface.SelectedByClickForTests(middle));

        host.Close();
    }

    [AvaloniaFact]
    public void ClickingTheControlStillSelectsItScrolledAndMagnified()
    {
        // Both at once, which is the state a form is actually worked on in.
        var (surface, host, _) = Shown();

        surface.Zoom = 1.5;
        surface.PanBy(-30, -20);
        Settle(host);

        var middle = surface.SelectionBoundsForTests!.Value.Center;

        Assert.Equal("Button", surface.SelectedByClickForTests(middle));

        host.Close();
    }
}

/// <summary>
/// Where the selection is drawn, as against where the control is.
/// </summary>
/// <remarks>
/// The adorners are measured with BoundsOf and drawn on a canvas of their
/// own, and the two have to agree about which coordinates they mean. They did
/// not: the canvas lived inside the scaled panel while the measurement was
/// taken against the surface, so selecting a control outlined a spot up and
/// to the left of it. Drawing them inside Refresh made it worse — the
/// content had only just been put in, so there was nothing to measure and
/// every handle went to the origin.
/// </remarks>
public class SelectionAdornerTests
{
    private const string Form = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="App.MainWindow" Width="300" Height="200">
          <Canvas>
            <Button x:Name="Ok" Canvas.Left="40" Canvas.Top="30" Width="100" Height="28" />
          </Canvas>
        </Window>
        """;

    /// <summary>Where the button is drawn, and where its outline is.</summary>
    private static (Point Control, Point? Adorner) Positions(double zoom = 1, Vector pan = default)
    {
        var session = new DesignerSession(XamlDocument.Parse(Form), SourceLanguage.VisualBasic);
        var surface = new DesignSurface { Session = session };
        var host = new Window { Content = surface, Width = 460, Height = 340 };

        host.Show();
        host.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();

        session.Select(session.Document.Root.Descendants().First(e => e.Name.LocalName == "Button"));
        surface.RefreshNow();

        if (zoom != 1) surface.Zoom = zoom;
        if (pan != default) surface.PanBy(pan.X, pan.Y);

        host.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();

        var drawn = surface.GetVisualDescendants().OfType<Button>().First();
        var control = drawn.TranslatePoint(default, host)!.Value;

        // The frame is the one adorner as wide as the control itself.
        var frame = surface.GetVisualDescendants()
            .OfType<Canvas>()
            .Where(c => !c.IsHitTestVisible)
            .SelectMany(c => c.Children.OfType<Control>())
            .FirstOrDefault(c => c.Width > 50);

        var adorner = frame?.TranslatePoint(default, host);

        host.Close();

        return (control, adorner);
    }

    [AvaloniaFact]
    public void OutlineTheControlItself()
    {
        // The one that was wrong: the outline sat up and to the left.
        var (control, adorner) = Positions();

        Assert.NotNull(adorner);
        Assert.Equal(control.X, adorner!.Value.X, 1);
        Assert.Equal(control.Y, adorner.Value.Y, 1);
    }

    [AvaloniaFact]
    public void OutlineItStillWhenTheViewIsScrolled()
    {
        var (control, adorner) = Positions(pan: new Vector(-40, -30));

        Assert.NotNull(adorner);
        Assert.Equal(control.X, adorner!.Value.X, 1);
        Assert.Equal(control.Y, adorner.Value.Y, 1);
    }

    [AvaloniaFact]
    public void OutlineItStillWhenMagnified()
    {
        var (control, adorner) = Positions(zoom: 2);

        Assert.NotNull(adorner);
        Assert.Equal(control.X, adorner!.Value.X, 1);
        Assert.Equal(control.Y, adorner.Value.Y, 1);
    }
}
