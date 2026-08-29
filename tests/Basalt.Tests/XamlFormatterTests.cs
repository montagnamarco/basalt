using System.Xml.Linq;
using Basalt.Designer.Model;

namespace Basalt.Tests;

/// <summary>
/// Laying a whole XAML document out to be read.
/// </summary>
/// <remarks>
/// The per-edit formatter keeps a tidy file tidy but cannot rescue one that is
/// not: markup written on one line stayed on one line however many controls
/// were dropped into it, because nothing revisited what was already there.
/// </remarks>
public class XamlFormatterTests
{
    private static string Format(string xaml) =>
        XamlFormatter.Format(
            XDocument.Parse(xaml, LoadOptions.PreserveWhitespace));

    [Fact]
    public void PutsEveryTagOnItsOwnLine()
    {
        // The case that was reported: a whole form on one line.
        var formatted = Format("""
            <Window xmlns="https://github.com/avaloniaui" x:Class="App.W"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid><StackPanel><Button Content="Uno" /><Button Content="Due" /></StackPanel></Grid>
            </Window>
            """);

        Assert.DoesNotContain("/><", formatted);
        Assert.DoesNotContain("><StackPanel", formatted);
    }

    [Fact]
    public void NestsEachLevelOneStepFurtherIn()
    {
        var formatted = Format("""
            <Window xmlns="https://github.com/avaloniaui">
              <Grid><StackPanel><Button Content="Uno" /></StackPanel></Grid>
            </Window>
            """);

        Assert.Contains("\n  <Grid>", formatted);
        Assert.Contains("\n    <StackPanel>", formatted);
        Assert.Contains("\n      <Button", formatted);
    }

    [Fact]
    public void FormattingTwiceChangesNothingTheSecondTime()
    {
        // The property that makes a formatter trustworthy: running it on its
        // own output is a no-op, so saving twice cannot drift.
        var once = Format("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="App.MainWindow" Width="800" Height="500" Title="An Application">
              <Grid RowDefinitions="Auto,*">
                <StackPanel Orientation="Horizontal">
                  <Button Content="Uno" />
                </StackPanel>
                <ListBox Grid.Row="1" />
              </Grid>
            </Window>
            """);

        Assert.Equal(once, Format(once));
    }

    [Fact]
    public void BreaksALongTagOverSeveralLines()
    {
        // A root carries the namespaces, a class, a size and a title: on one
        // line that runs off any window.
        var formatted = Format("""
            <Window xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="App.MainWindow" Width="800" Height="500" Title="An Application">
              <Grid />
            </Window>
            """);

        var first = formatted.Split('\n')[0];

        Assert.True(first.Length <= 100, $"the first line is {first.Length} characters");
        Assert.Contains("\n        xmlns:x=", formatted);
    }

    [Fact]
    public void KeepsAShortTagWhole()
    {
        // A Button with one attribute reads worse split in two.
        var formatted = Format("""
            <Window xmlns="https://github.com/avaloniaui">
              <Button Content="Uno" />
            </Window>
            """);

        Assert.Contains("<Button Content=\"Uno\" />", formatted);
    }

    [Fact]
    public void LeavesTheTextOfAControlAlone()
    {
        // Breaking this would put whitespace inside what the control says.
        var formatted = Format("""
            <Window xmlns="https://github.com/avaloniaui">
              <TextBlock>Ciao</TextBlock>
            </Window>
            """);

        Assert.Contains("<TextBlock>Ciao</TextBlock>", formatted);
    }

    [Fact]
    public void KeepsTheCommentsAPersonWrote()
    {
        var formatted = Format("""
            <Window xmlns="https://github.com/avaloniaui">
              <!-- The buttons along the bottom -->
              <Grid />
            </Window>
            """);

        Assert.Contains("<!-- The buttons along the bottom -->", formatted);
    }

    [Fact]
    public void ClosesAnEmptyElementOnItself()
    {
        var formatted = Format("""
            <Window xmlns="https://github.com/avaloniaui">
              <Grid></Grid>
            </Window>
            """);

        Assert.Contains("<Grid />", formatted);
    }

    [Fact]
    public void WritesEachNamespaceOnce()
    {
        // LINQ-to-XML reports inherited namespaces on every element that uses
        // them; repeating them would put xmlns on every control in the file.
        var formatted = Format("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid><Button x:Name="A" /></Grid>
            </Window>
            """);

        Assert.Equal(1, formatted.Split("xmlns=").Length - 1);
        Assert.Equal(1, formatted.Split("xmlns:x=").Length - 1);
    }

    [Fact]
    public void KeepsThePrefixOnAnAttributeThatHasOne()
    {
        var formatted = Format("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Button x:Name="OkButton" />
            </Window>
            """);

        Assert.Contains("x:Name=\"OkButton\"", formatted);
    }

    [Fact]
    public void FollowsTheIndentAsked()
    {
        var formatted = XamlFormatter.Format(
            XDocument.Parse("""
                <Window xmlns="https://github.com/avaloniaui">
                  <Grid><Button /></Grid>
                </Window>
                """, LoadOptions.PreserveWhitespace),
            indent: 4);

        Assert.Contains("\n    <Grid>", formatted);
        Assert.Contains("\n        <Button />", formatted);
    }
}
