using System.Xml.Linq;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Designer.Model;
using Basalt.Designer.Toolbox;

namespace Basalt.Tests;

/// <summary>
/// Keeping the markup readable as the designer writes it.
/// </summary>
/// <remarks>
/// The document preserves whitespace so a person's formatting survives being
/// opened; the cost was that nothing indented what the designer added. A
/// control dropped on a form arrived hard against its neighbour's closing tag
/// at no indent at all, and the file got worse the more the designer was used.
/// </remarks>
public class XamlFormattingTests
{
    private static readonly ToolboxItem Button =
        new("Pulsante", "Button", "Comuni", """<Button Content="Due" />""");

    private static DesignerSession SessionFor(string xaml) =>
        new(XamlDocument.Parse(xaml), SourceLanguage.VisualBasic);

    private const string TwoSpaces = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="App.MainWindow">
          <StackPanel>
            <Button Content="Uno" />
          </StackPanel>
        </Window>
        """;

    [Fact]
    public void PutsANewControlOnItsOwnLine()
    {
        // It used to arrive glued to the closing tag of the panel.
        var session = SessionFor(TwoSpaces);
        var stack = XamlDocument.ControlChildren(session.Document.Root).First();

        session.InsertFromToolbox(Button, stack);

        Assert.Contains("<Button Content=\"Uno\" />\n", session.Document.ToXaml());
        Assert.DoesNotContain("/><Button", session.Document.ToXaml());
    }

    [Fact]
    public void IndentsItLikeTheControlBesideIt()
    {
        var session = SessionFor(TwoSpaces);
        var stack = XamlDocument.ControlChildren(session.Document.Root).First();

        session.InsertFromToolbox(Button, stack);

        Assert.Contains("    <Button Content=\"Due\" />", session.Document.ToXaml());
    }

    [Fact]
    public void FollowsTheFilesOwnIndentation()
    {
        // A file written with four spaces should not start growing two-space
        // children halfway down.
        var fourSpaces = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="App.MainWindow">
                <StackPanel>
                    <Button Content="Uno" />
                </StackPanel>
            </Window>
            """;

        var session = SessionFor(fourSpaces);
        var stack = XamlDocument.ControlChildren(session.Document.Root).First();

        session.InsertFromToolbox(Button, stack);

        Assert.Contains("        <Button Content=\"Due\" />", session.Document.ToXaml());
    }

    [Fact]
    public void OpensOutAContainerWrittenOnOneLine()
    {
        // A child on the same line as its parent's tags is unreadable.
        var oneLine = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="App.MainWindow">
              <StackPanel></StackPanel>
            </Window>
            """;

        var session = SessionFor(oneLine);
        var stack = XamlDocument.ControlChildren(session.Document.Root).First();

        session.InsertFromToolbox(Button, stack);

        var xaml = session.Document.ToXaml();

        Assert.DoesNotContain("<StackPanel><Button", xaml);
        Assert.DoesNotContain("/></StackPanel>", xaml);
    }

    [Fact]
    public void LeavesNoBlankLineBehindWhenUndone()
    {
        // Taking the element alone leaves its indentation, so twenty inserts
        // undone leave twenty blank lines: the file gets worse every time the
        // user changes their mind.
        var session = SessionFor(TwoSpaces);
        var stack = XamlDocument.ControlChildren(session.Document.Root).First();

        var before = session.Document.ToXaml();

        session.InsertFromToolbox(Button, stack);
        session.Undo();

        Assert.Equal(before, session.Document.ToXaml());
    }

    [Fact]
    public void KeepsTheCommentsAndSpacingAPersonWrote()
    {
        // The whole reason whitespace is preserved: hand-written markup must
        // survive a trip through the designer.
        var handWritten = """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="App.MainWindow">
              <!-- The buttons along the bottom -->
              <StackPanel>
                <Button Content="Uno" />
              </StackPanel>
            </Window>
            """;

        var session = SessionFor(handWritten);
        var stack = XamlDocument.ControlChildren(session.Document.Root).First();

        session.InsertFromToolbox(Button, stack);

        Assert.Contains("<!-- The buttons along the bottom -->", session.Document.ToXaml());
    }
}

/// <summary>Drawing one control over or under the ones beside it.</summary>
public class ZOrderTests
{
    private static DesignerSession SessionFor() =>
        new(XamlDocument.Parse("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="App.MainWindow">
              <Canvas>
                <Button x:Name="A" />
                <Button x:Name="B" />
                <Button x:Name="C" />
              </Canvas>
            </Window>
            """), SourceLanguage.VisualBasic);

    private static IReadOnlyList<string> Order(DesignerSession session) =>
    [
        .. XamlDocument
            .ControlChildren(XamlDocument.ControlChildren(session.Document.Root).First())
            .Select(e => XamlDocument.GetName(e) ?? "?")
    ];

    [Fact]
    public void BringsAControlInFrontOfTheOthers()
    {
        // Among siblings a control is drawn in the order it is written, so
        // "in front" is last.
        var session = SessionFor();
        var canvas = XamlDocument.ControlChildren(session.Document.Root).First();

        session.Select(XamlDocument.ControlChildren(canvas).First());
        session.BringToFront();

        Assert.Equal(["B", "C", "A"], Order(session));
    }

    [Fact]
    public void SendsAControlBehindTheOthers()
    {
        var session = SessionFor();
        var canvas = XamlDocument.ControlChildren(session.Document.Root).First();

        session.Select(XamlDocument.ControlChildren(canvas).Last());
        session.BringToFront(false);

        Assert.Equal(["C", "A", "B"], Order(session));
    }

    [Fact]
    public void RecordsNothingWhenItIsAlreadyThere()
    {
        // An undo step that changes nothing is one the user has to press
        // twice to get anywhere.
        var session = SessionFor();
        var canvas = XamlDocument.ControlChildren(session.Document.Root).First();

        session.Select(XamlDocument.ControlChildren(canvas).Last());
        session.BringToFront();

        Assert.False(session.History.CanUndo);
    }
}

/// <summary>
/// The markup staying tidy as controls are moved and removed, not only added.
/// </summary>
/// <remarks>
/// Inserting went through the formatter; moving and deleting did not. A
/// control dragged into another panel arrived on one line with its new
/// parent, and the panel it left kept the blank lines where it used to be.
/// </remarks>
public class XamlFormattingAfterEditsTests
{
    private const string Form = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="App.MainWindow">
          <Grid>
            <StackPanel>
              <Button Content="Uno" />
              <Button Content="Due" />
            </StackPanel>
            <Canvas />
          </Grid>
        </Window>
        """;

    private static DesignerSession SessionFor() =>
        new(XamlDocument.Parse(Form), SourceLanguage.VisualBasic);

    private static XElement Find(DesignerSession s, string name) =>
        s.Document.Root.Descendants().First(e => e.Name.LocalName == name);

    [Fact]
    public void IndentsAControlMovedIntoAnotherPanel()
    {
        // It used to arrive on the same line as its new parent's tags.
        var session = SessionFor();

        session.Reparent(Find(session, "Button"), Find(session, "Canvas"), 10, 10, 100, 100);

        var xaml = session.Document.ToXaml();

        Assert.DoesNotContain("<Canvas><Button", xaml);
        Assert.Contains("      <Button Content=\"Uno\"", xaml);
    }

    [Fact]
    public void LeavesNoGapWhereAMovedControlUsedToBe()
    {
        var session = SessionFor();

        session.Reparent(Find(session, "Button"), Find(session, "Canvas"), 10, 10, 100, 100);

        Assert.DoesNotContain("\n\n", session.Document.ToXaml());
    }

    [Fact]
    public void ClosesAPanelLeftWithNothingInIt()
    {
        // Removing the last control left the indentation around it, so the
        // panel closed several blank lines below where it opened.
        var session = SessionFor();

        foreach (var button in XamlDocument.ControlChildren(Find(session, "StackPanel")).ToList())
        {
            session.Select(button);
            session.RemoveSelected();
        }

        Assert.Contains("<StackPanel />", session.Document.ToXaml());
    }

    [Fact]
    public void LeavesNoGapWhereADeletedControlUsedToBe()
    {
        var session = SessionFor();

        session.Select(Find(session, "Button"));
        session.RemoveSelected();

        Assert.DoesNotContain("\n\n", session.Document.ToXaml());
    }

    [Fact]
    public void PutsTheFileBackExactlyWhenAMoveIsUndone()
    {
        // The strictest test there is: byte for byte, or the formatting is
        // drifting somewhere.
        var session = SessionFor();
        var before = session.Document.ToXaml();

        session.Reparent(Find(session, "Button"), Find(session, "Canvas"), 10, 10, 100, 100);
        session.Undo();

        Assert.Equal(before, session.Document.ToXaml());
    }

    [Fact]
    public void PutsTheFileBackExactlyWhenADeleteIsUndone()
    {
        var session = SessionFor();
        var before = session.Document.ToXaml();

        session.Select(Find(session, "Button"));
        session.RemoveSelected();
        session.Undo();

        Assert.Equal(before, session.Document.ToXaml());
    }

    [Fact]
    public void KeepsWhatIsInsideATextBlockAlone()
    {
        // Only whitespace is safe to tidy away: text content is what the
        // control says, and dropping it would change the form.
        var session = new DesignerSession(
            XamlDocument.Parse("""
                <Window xmlns="https://github.com/avaloniaui"
                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                        x:Class="App.MainWindow">
                  <StackPanel>
                    <TextBlock>Ciao</TextBlock>
                    <Button Content="Uno" />
                  </StackPanel>
                </Window>
                """), SourceLanguage.VisualBasic);

        session.Select(Find(session, "Button"));
        session.RemoveSelected();

        Assert.Contains("<TextBlock>Ciao</TextBlock>", session.Document.ToXaml());
    }
}
