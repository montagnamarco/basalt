using Avalonia.Headless.XUnit;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Designer.Model;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// The events of the selected control.
/// </summary>
/// <remarks>
/// A double click on the surface gives a control the one event it obviously
/// means; this is for the others. Without a list there is no way to reach
/// them from the designer at all.
/// </remarks>
public class EventsPanelTests
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

    private static System.Xml.Linq.XElement Named(DesignerSession s, string name) =>
        s.Document.Root.Descendants().First(e => e.Name.LocalName == name);

    [AvaloniaFact]
    public void SaysWhatToDoBeforeAnythingIsSelected()
    {
        var panel = new EventsPanel();

        panel.Show(SessionFor("<Grid />"));

        Assert.Empty(panel.Events);
    }

    [AvaloniaFact]
    public void PutsTheControlsOwnEventFirst()
    {
        // Click at the top for a Button, rather than sorted under C among
        // the dozen every control has.
        var session = SessionFor("<Grid><Button /></Grid>");
        session.Select(Named(session, "Button"));

        var panel = new EventsPanel();
        panel.Show(session);

        Assert.Equal("Click", panel.Events[0]);
    }

    [AvaloniaFact]
    public void OffersADifferentFirstEventForADifferentControl()
    {
        var session = SessionFor("<Grid><TextBox /></Grid>");
        session.Select(Named(session, "TextBox"));

        var panel = new EventsPanel();
        panel.Show(session);

        Assert.Equal("TextChanged", panel.Events[0]);
    }

    [AvaloniaFact]
    public void OffersTheEventsEveryControlHasAsWell()
    {
        var session = SessionFor("<Grid><Button /></Grid>");
        session.Select(Named(session, "Button"));

        var panel = new EventsPanel();
        panel.Show(session);

        Assert.Contains("KeyDown", panel.Events);
        Assert.Contains("LostFocus", panel.Events);
    }

    [AvaloniaFact]
    public void NamesTheEventOnlyOnceWhenItIsBothOwnAndCommon()
    {
        // A CheckBox has Click of its own and Click is not in the common
        // list twice; a duplicated row would look like two different events.
        var session = SessionFor("<Grid><CheckBox /></Grid>");
        session.Select(Named(session, "CheckBox"));

        var panel = new EventsPanel();
        panel.Show(session);

        Assert.Equal(1, panel.Events.Count(e => e == "Click"));
    }

    [AvaloniaFact]
    public void ShowsTheMethodAnEventAlreadyCalls()
    {
        // The list doubles as an answer to "what does this control do".
        var session = SessionFor("""<Grid><Button Click="OkButton_Click" /></Grid>""");
        session.Select(Named(session, "Button"));

        var panel = new EventsPanel();
        panel.Show(session);

        Assert.Equal("OkButton_Click", panel.Handlers["Click"]);
    }

    [AvaloniaFact]
    public void ShowsNoHandlerForAnEventThatHasNone()
    {
        var session = SessionFor("<Grid><Button /></Grid>");
        session.Select(Named(session, "Button"));

        var panel = new EventsPanel();
        panel.Show(session);

        Assert.Empty(panel.Handlers);
    }

    [AvaloniaFact]
    public void AsksForAHandlerByName()
    {
        // The panel chooses which event; the window writes the code.
        var session = SessionFor("<Grid><Button /></Grid>");
        session.Select(Named(session, "Button"));

        var panel = new EventsPanel();
        panel.Show(session);

        string? asked = null;
        panel.HandlerRequested += (_, name) => asked = name;

        panel.RequestForTests("KeyDown");

        Assert.Equal("KeyDown", asked);
    }

    [Fact]
    public void WiresWhicheverEventIsAskedFor()
    {
        // Not only the default: the whole reason the list exists.
        var root = Path.Combine(Path.GetTempPath(), "basalt-events-panel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var path = Path.Combine(root, "MainWindow.axaml");

            var xaml = """
                <Window xmlns="https://github.com/avaloniaui"
                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                        x:Class="App.MainWindow">
                  <Grid><Button x:Name="OkButton" /></Grid>
                </Window>
                """;

            File.WriteAllText(path, xaml);

            var session = new DesignerSession(
                XamlDocument.Parse(xaml, path), SourceLanguage.VisualBasic);

            session.Select(Named(session, "Button"));

            var handler = session.AttachHandler("KeyDown");

            Assert.Equal("OkButton_KeyDown", handler!.MethodName);
            Assert.Contains("KeyDown=\"OkButton_KeyDown\"", session.Document.ToXaml());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
