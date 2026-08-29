using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Designer.Model;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// Knowing how big the window will really be.
/// </summary>
/// <remarks>
/// The surface draws the form at whatever size the panel gives it, which is
/// not the size it will run at: the numbers a person lays out against were
/// invisible, and designing in a narrow panel was guesswork.
/// </remarks>
public class DesignedSizeTests
{
    private static DesignSurface For(string root)
    {
        var xaml = $"""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="App.MainWindow" {root}>
              <Grid />
            </Window>
            """;

        return new DesignSurface
        {
            Session = new DesignerSession(XamlDocument.Parse(xaml), SourceLanguage.VisualBasic),
        };
    }

    [AvaloniaFact]
    public void ReportsTheSizeTheDocumentAsksFor()
    {
        Assert.Equal((800d, 500d), For("""Width="800" Height="500" """).DesignedSize);
    }

    [AvaloniaFact]
    public void SaysNothingWhereTheDocumentDoesNot()
    {
        // A UserControl takes the size of whatever hosts it, and inventing a
        // number would be a claim the file does not make.
        Assert.Null(For("").DesignedSize);
    }

    [AvaloniaFact]
    public void ReadsTheSizeTheSameInAnyRegion()
    {
        // Written invariant in the file; a comma decimal separator would
        // otherwise parse as something else entirely.
        Assert.Equal((640d, 480.5d), For("""Width="640" Height="480.5" """).DesignedSize);
    }
}

/// <summary>Seeing where a dragged control will land in the tree.</summary>
public class ElementTreeDropTests
{
    private static (ElementTreePanel Tree, DesignerSession Session) Built()
    {
        var session = new DesignerSession(
            XamlDocument.Parse("""
                <Window xmlns="https://github.com/avaloniaui"
                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                        x:Class="App.MainWindow">
                  <Grid>
                    <StackPanel><Button x:Name="A" /></StackPanel>
                  </Grid>
                </Window>
                """), SourceLanguage.VisualBasic);

        var tree = new ElementTreePanel();
        tree.Show(session);

        return (tree, session);
    }

    private static System.Xml.Linq.XElement Named(DesignerSession s, string name) =>
        s.Document.Root.Descendants().First(e => e.Name.LocalName == name);

    [AvaloniaFact]
    public void FillsAContainerTheControlWouldGoInside()
    {
        var (tree, session) = Built();

        tree.ShowDropOnForTests(Named(session, "StackPanel"));

        var (label, inside) = tree.MarkedForTests;

        Assert.Contains("StackPanel", label);
        Assert.True(inside, "a container should be filled, since the control goes into it");
    }

    [AvaloniaFact]
    public void UnderlinesAControlTheOneDraggedWouldJoin()
    {
        // Onto a plain control it goes beside it, which is a different
        // outcome and needs a different mark.
        var (tree, session) = Built();

        tree.ShowDropOnForTests(Named(session, "Button"));

        var (label, inside) = tree.MarkedForTests;

        Assert.Contains("Button", label);
        Assert.False(inside, "a plain control cannot be dropped into");
    }

    [AvaloniaFact]
    public void MarksNothingUntilSomethingIsDraggedOver()
    {
        var (tree, _) = Built();

        Assert.Null(tree.MarkedForTests.Label);
    }
}
