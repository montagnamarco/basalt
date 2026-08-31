using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
