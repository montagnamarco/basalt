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
/// What the designer tells you before you touch anything.
/// </summary>
/// <remarks>
/// The surface never changed the cursor, so a resize handle looked exactly
/// like the background: whether a corner could be grabbed, and which way it
/// would stretch, could only be discovered by trying it.
/// </remarks>
public class DesignerAffordanceTests
{
    private const string Form = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="App.MainWindow" Width="400" Height="300">
          <Canvas>
            <Button x:Name="A" Canvas.Left="50" Canvas.Top="50" Width="100" Height="40" />
          </Canvas>
        </Window>
        """;

    private static (DesignSurface Surface, Window Host, DesignerSession Session) Shown()
    {
        var session = new DesignerSession(XamlDocument.Parse(Form), SourceLanguage.VisualBasic);
        var surface = new DesignSurface { Session = session };
        var host = new Window { Content = surface, Width = 420, Height = 320 };

        host.Show();
        host.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();

        return (surface, host, session);
    }

    [AvaloniaFact]
    public void ShowsAPlainArrowOverEmptySpace()
    {
        var (surface, host, _) = Shown();

        surface.ShowCursorForTests(new Point(5, 5));

        Assert.Equal(Cursor.Default, surface.Cursor);

        host.Close();
    }

    [AvaloniaFact]
    public void ShowsAMoveCursorOverTheSelection()
    {
        // Over something already selected a press moves it, and the four-way
        // arrow is how every other tool says that.
        var (surface, host, session) = Shown();

        var canvas = XamlDocument.ControlChildren(session.Document.Root).First();
        session.Select(XamlDocument.ControlChildren(canvas).First());
        surface.Refresh();

        host.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();

        // Measured rather than assumed: the preview is laid out by Avalonia,
        // and hard-coding where it put the control makes the test a guess.
        var bounds = surface.SelectionBoundsForTests!.Value;

        surface.ShowCursorForTests(bounds.Center);

        Assert.NotEqual(Cursor.Default, surface.Cursor);

        host.Close();
    }

    [AvaloniaFact]
    public void ShowsAStretchCursorOnAResizeHandle()
    {
        // The corner handles say which way they stretch, which is the whole
        // point of drawing them.
        var (surface, host, session) = Shown();

        var canvas = XamlDocument.ControlChildren(session.Document.Root).First();
        session.Select(XamlDocument.ControlChildren(canvas).First());
        surface.Refresh();

        host.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();

        var bounds = surface.SelectionBoundsForTests!.Value;

        surface.ShowCursorForTests(bounds.BottomRight);

        Assert.NotEqual(Cursor.Default, surface.Cursor);

        host.Close();
    }

    [AvaloniaFact]
    public void SaysWhatToDoWithAFormThatIsStillEmpty()
    {
        // A blank rectangle says neither "empty" nor "broken" nor what to do
        // about it.
        var empty = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="App.MainWindow">
              <Grid />
            </Window>
            """;

        var session = new DesignerSession(XamlDocument.Parse(empty), SourceLanguage.VisualBasic);
        var surface = new DesignSurface { Session = session };
        var host = new Window { Content = surface, Width = 300, Height = 200 };

        host.Show();
        host.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(surface.IsShowingEmptyHint, "an empty form should say how to start");

        host.Close();
    }

    [AvaloniaFact]
    public void StopsSayingItOnceThereIsAControl()
    {
        var (surface, host, _) = Shown();

        Assert.False(surface.IsShowingEmptyHint);

        host.Close();
    }
}
