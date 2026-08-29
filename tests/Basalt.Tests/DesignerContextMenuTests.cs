using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Designer.Model;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// What the designer offers on a right-click.
/// </summary>
/// <remarks>
/// Right-clicking is where a person looks for what can be done to the thing
/// under the pointer, and the surface answered with nothing: every command
/// lived in the menu bar, a long way from the control being worked on.
/// </remarks>
public class DesignerContextMenuTests
{
    private const string Form = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="App.MainWindow">
          <Canvas>
            <Button x:Name="A" />
            <Button x:Name="B" />
          </Canvas>
        </Window>
        """;

    private static (DesignSurface Surface, DesignerSession Session) Built()
    {
        var session = new DesignerSession(XamlDocument.Parse(Form), SourceLanguage.VisualBasic);

        return (new DesignSurface { Session = session }, session);
    }

    [AvaloniaFact]
    public void OffersAMenuAtAll()
    {
        var (surface, _) = Built();

        Assert.NotNull(surface.MenuForTests);
    }

    [AvaloniaFact]
    public void GreysWhatCannotBeDoneRatherThanHidingIt()
    {
        // A menu that changes shape is one you have to read again every time.
        var (surface, _) = Built();

        var entries = ContextMenus.OpenForTests(surface.MenuForTests!);

        Assert.Contains(entries, e => e.Header == "Cut" && !e.Enabled);
        Assert.Contains(entries, e => e.Header == "Align Left" && !e.Enabled);
    }

    [AvaloniaFact]
    public void LightsTheEntriesThatApplyToTheSelection()
    {
        var (surface, session) = Built();

        var canvas = XamlDocument.ControlChildren(session.Document.Root).First();
        session.Select(XamlDocument.ControlChildren(canvas).First());

        var entries = ContextMenus.OpenForTests(surface.MenuForTests!);

        Assert.Contains(entries, e => e.Header == "Cut" && e.Enabled);
        Assert.Contains(entries, e => e.Header == "Bring to Front" && e.Enabled);

        // Aligning needs something to align to.
        Assert.Contains(entries, e => e.Header == "Align Left" && !e.Enabled);
    }

    [AvaloniaFact]
    public void OffersAligningOnceTwoAreSelected()
    {
        var (surface, session) = Built();

        var canvas = XamlDocument.ControlChildren(session.Document.Root).First();
        session.SelectMany(XamlDocument.ControlChildren(canvas));

        var entries = ContextMenus.OpenForTests(surface.MenuForTests!);

        Assert.Contains(entries, e => e.Header == "Align Left" && e.Enabled);
    }

    [AvaloniaFact]
    public void ReportsWhatWasChosenRatherThanActingAlone()
    {
        // The surface knows the geometry; the window owns the panels that
        // have to be brought back in step afterwards.
        var (surface, session) = Built();

        var canvas = XamlDocument.ControlChildren(session.Document.Root).First();
        session.Select(XamlDocument.ControlChildren(canvas).First());

        DesignSurface.DesignerMenuCommand? asked = null;
        surface.MenuCommand += (_, command) => asked = command;

        ContextMenus.ChooseForTests(surface.MenuForTests!, "Bring to Front");

        Assert.Equal(DesignSurface.DesignerMenuCommand.BringToFront, asked);
    }
}
