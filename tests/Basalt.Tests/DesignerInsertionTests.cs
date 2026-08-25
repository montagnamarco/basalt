using Avalonia.Headless.XUnit;
using Basalt.Designer;
using Basalt.Designer.Model;
using Basalt.Designer.Toolbox;

namespace Basalt.Tests;

/// <summary>
/// Where a control dropped from the toolbox actually lands.
/// </summary>
/// <remarks>
/// A Border, a Window and a ScrollViewer set their Content, and a second
/// child makes Avalonia refuse the whole file: "Unable to find setter that
/// allows multiple assignments to the content". The preview goes blank, which
/// reads as the designer breaking rather than as one control too many.
/// </remarks>
public sealed class DesignerInsertionTests
{
    private static DesignerSession Open(string xaml) =>
        new(XamlDocument.Parse(xaml), Basalt.Core.Model.SourceLanguage.VisualBasic);

    private static ToolboxItem Button() =>
        new("Button", "Button", "Common", "<Button Content=\"New\" />");

    [AvaloniaFact]
    public void APanelTakesAsManyAsYouLike()
    {
        var session = Open("""
            <Window xmlns="https://github.com/avaloniaui">
              <StackPanel />
            </Window>
            """);

        var panel = session.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "StackPanel");

        session.InsertFromToolbox(Button(), panel);
        session.InsertFromToolbox(Button(), panel);

        Assert.Equal(2, panel.Elements().Count());
    }

    [AvaloniaFact]
    public void AnEmptyBorderTakesTheFirst()
    {
        var session = Open("""
            <Window xmlns="https://github.com/avaloniaui">
              <Canvas><Border /></Canvas>
            </Window>
            """);

        var border = session.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "Border");

        session.InsertFromToolbox(Button(), border);

        Assert.Single(border.Elements());
    }

    [AvaloniaFact]
    public void AFullBorderSendsTheSecondToItsParent()
    {
        // The whole point: the second control goes somewhere that can hold
        // it, rather than making the file unloadable.
        var session = Open("""
            <Window xmlns="https://github.com/avaloniaui">
              <Canvas><Border /></Canvas>
            </Window>
            """);

        var border = session.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "Border");

        var canvas = session.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "Canvas");

        session.InsertFromToolbox(Button(), border);
        session.InsertFromToolbox(Button(), border);

        Assert.Single(border.Elements());
        Assert.Equal(2, canvas.Elements().Count());
    }

    [AvaloniaFact]
    public void AControlThatHostsNothingSendsItToItsParent()
    {
        var session = Open("""
            <Window xmlns="https://github.com/avaloniaui">
              <Canvas><TextBox /></Canvas>
            </Window>
            """);

        var textBox = session.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "TextBox");

        session.InsertFromToolbox(Button(), textBox);

        Assert.Empty(textBox.Elements());

        Assert.Equal(2, session.Document.Root.Descendants()
            .First(e => e.Name.LocalName == "Canvas").Elements().Count());
    }

    [AvaloniaFact]
    public void AWindowHoldingItsRootSendsTheNextInside()
    {
        // A Window with a Canvas already in it: a control dropped on the
        // window itself belongs in the Canvas, not beside it.
        var session = Open("""
            <Window xmlns="https://github.com/avaloniaui">
              <Canvas />
            </Window>
            """);

        session.InsertFromToolbox(Button(), session.Document.Root);

        // Not in the Window, which already has its one child.
        Assert.Single(session.Document.Root.Elements());
    }

    [AvaloniaFact]
    public void TheInsertedControlIsTheOneSelected()
    {
        var session = Open("""
            <Window xmlns="https://github.com/avaloniaui">
              <StackPanel />
            </Window>
            """);

        var inserted = session.InsertFromToolbox(Button());

        Assert.Same(inserted, session.Selection);
    }

    [AvaloniaFact]
    public void AnEmptyWindowTakesTheFirstControl()
    {
        // The state someone is in when they start a file: an empty window,
        // and the first control has to land somewhere.
        var session = Open("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
            """);

        session.InsertFromToolbox(Button());

        Assert.Single(session.Document.Root.Elements());
    }

    [AvaloniaFact]
    public void AnEmptyWindowStillPreviews()
    {
        // It rendered as nothing at all, with no error either: a new file
        // looked exactly like a designer that had broken.
        var session = Open("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
            """);

        var result = session.Render();

        Assert.True(result.Succeeded, result.Error ?? "no reason given");
        Assert.NotNull(result.Root);
    }
}
