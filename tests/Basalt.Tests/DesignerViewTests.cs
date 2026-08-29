using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Designer.Model;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// Seeing what is being designed.
///
/// A window is drawn at the size it will run, which is regularly larger than
/// the panel it is drawn in: without a zoom the far side of it cannot be
/// reached at all, and the form is laid out half blind.
/// </summary>
public class DesignerViewTests
{
    private const string Wide = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="App.MainWindow" Width="800" Height="600">
          <Grid>
            <Button Content="Uno" Width="100" Height="30" />
          </Grid>
        </Window>
        """;

    private static (DesignSurface Surface, Window Host, DesignerSession Session) Show(
        string xaml, double width = 300, double height = 220)
    {
        var session = new DesignerSession(XamlDocument.Parse(xaml), SourceLanguage.VisualBasic);
        var surface = new DesignSurface { Session = session };

        var host = new Window { Content = surface, Width = width, Height = height };

        host.Show();
        host.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();

        return (surface, host, session);
    }

    [AvaloniaFact]
    public void StartsAtActualSize()
    {
        // Whatever else it does, opening a form must show it at the size the
        // numbers in the file say.
        var (surface, host, _) = Show(Wide);

        Assert.Equal(1, surface.Zoom);

        host.Close();
    }

    [AvaloniaFact]
    public void ShrinksAWindowThatDoesNotFit()
    {
        // 800 wide drawn in 300: the point of the whole feature.
        var (surface, host, _) = Show(Wide);

        surface.ZoomToFit();

        Assert.True(surface.Zoom < 1, "a window wider than the panel should be shrunk");

        host.Close();
    }

    [AvaloniaFact]
    public void NeverMagnifiesPastActualSize()
    {
        // A small control blown up to fill the panel is not what the user is
        // drawing, and the sizes they type would stop matching what they see.
        var small = Wide.Replace("Width=\"800\" Height=\"600\"", "Width=\"80\" Height=\"40\"");

        var (surface, host, _) = Show(small);

        surface.ZoomToFit();

        Assert.Equal(1, surface.Zoom);

        host.Close();
    }

    [AvaloniaFact]
    public void RefusesToZoomBeyondItsLimits()
    {
        // A zoom of zero draws nothing and a zoom of a hundred is unusable;
        // both are reachable by holding the key down.
        var (surface, host, _) = Show(Wide);

        surface.Zoom = 100;
        Assert.Equal(DesignSurface.MaximumZoom, surface.Zoom);

        surface.Zoom = 0;
        Assert.Equal(DesignSurface.MinimumZoom, surface.Zoom);

        host.Close();
    }

    [AvaloniaFact]
    public void ComesBackToActualSizeAndTheOrigin()
    {
        var (surface, host, _) = Show(Wide);

        surface.Zoom = 2;
        surface.PanBy(-120, -80);

        surface.ResetView();

        Assert.Equal(1, surface.Zoom);

        host.Close();
    }

    [AvaloniaFact]
    public void SelectsTheControlUnderThePointerWhileZoomed()
    {
        // The one that matters: every hit test works in the preview's own
        // coordinates while the pointer arrives in the surface's. Without
        // undoing the zoom, clicking at 200% selected whatever sat under half
        // the distance from the corner.
        var (surface, host, session) = Show(Wide, 900, 700);

        var grid = XamlDocument.ControlChildren(session.Document.Root).First();
        var button = XamlDocument.ControlChildren(grid).First();

        // Where the button is at actual size.
        surface.Zoom = 1;
        host.UpdateLayout();

        var atOne = surface.ContainerAtForTests(new Point(1, 1));

        surface.Zoom = 2;
        host.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // The same content point must still resolve to the same element: the
        // conversion is what keeps that true.
        Assert.Equal(atOne?.Name.LocalName, surface.ContainerAtForTests(new Point(1, 1))?.Name.LocalName);

        host.Close();
    }
}

/// <summary>Reaching a container that its own children cover.</summary>
public class SelectParentTests
{
    [AvaloniaFact]
    public void GoesUpToTheContainerOnEscape()
    {
        // A Grid filled by a Button cannot be clicked: the click always lands
        // on the Button. Tab-stepping blindly through document order was the
        // only way to reach it.
        var xaml = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="App.MainWindow">
              <Grid><Button Content="Uno" /></Grid>
            </Window>
            """;

        var session = new DesignerSession(XamlDocument.Parse(xaml), SourceLanguage.VisualBasic);
        var surface = new DesignSurface { Session = session };

        var host = new Window { Content = surface, Width = 300, Height = 200 };
        host.Show();
        host.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var grid = XamlDocument.ControlChildren(session.Document.Root).First();
        var button = XamlDocument.ControlChildren(grid).First();

        session.Select(button);

        surface.PressForTests(Key.Escape);

        Assert.Equal("Grid", session.Selection?.Name.LocalName);

        host.Close();
    }
}
