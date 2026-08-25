using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>Setting breakpoints from the editor margin.</summary>
public class BreakpointMarginTests
{
    [AvaloniaFact]
    public void StartsWithNoBreakpoints()
    {
        var margin = new BreakpointMargin();

        Assert.Empty(margin.Lines);
        Assert.Equal(0, margin.CurrentLine);
    }

    [AvaloniaFact]
    public void SetsABreakpointOnALineThatHadNone()
    {
        var margin = new BreakpointMargin();

        margin.Toggle(6);

        Assert.Contains(6, margin.Lines);
    }

    [AvaloniaFact]
    public void ClearsABreakpointOnASecondClick()
    {
        var margin = new BreakpointMargin();

        margin.Toggle(6);
        margin.Toggle(6);

        Assert.Empty(margin.Lines);
    }

    [AvaloniaFact]
    public void ReportsEveryChange()
    {
        var margin = new BreakpointMargin();

        var reported = new List<int>();
        margin.Toggled += (_, line) => reported.Add(line);

        margin.Toggle(6);
        margin.Toggle(6);

        Assert.Equal([6, 6], reported);
    }

    [AvaloniaFact]
    public void IgnoresALineNumberThatCannotExist()
    {
        var margin = new BreakpointMargin();

        margin.Toggle(0);

        Assert.Empty(margin.Lines);
    }

    [AvaloniaFact]
    public void ShowsTheBreakpointsItIsGiven()
    {
        var margin = new BreakpointMargin();

        margin.Show([3, 7, 11]);

        Assert.Equal(3, margin.Lines.Count);
    }

    [AvaloniaFact]
    public void ReplacesWhatItShowsRatherThanAddingToIt()
    {
        // Reopening a file should show that file's breakpoints, not those of
        // the file before it as well.
        var margin = new BreakpointMargin();

        margin.Show([3, 7]);
        margin.Show([11]);

        Assert.Equal([11], margin.Lines);
    }

    [AvaloniaFact]
    public void MarksWhereExecutionIsStopped()
    {
        var margin = new BreakpointMargin();

        margin.ShowCurrentLine(6);

        Assert.Equal(6, margin.CurrentLine);
    }

    [AvaloniaFact]
    public void ClearsTheMarkWhenExecutionResumes()
    {
        var margin = new BreakpointMargin();
        margin.ShowCurrentLine(6);

        margin.ShowCurrentLine(0);

        Assert.Equal(0, margin.CurrentLine);
    }
}

/// <summary>Expressions watched while stepping.</summary>
public class WatchPanelTests
{
    [AvaloniaFact]
    public void StartsEmpty()
    {
        Assert.Empty(new WatchPanel().Entries);
    }

    [AvaloniaFact]
    public void AddsAnExpression()
    {
        var panel = new WatchPanel();

        panel.Add("total");

        Assert.Equal("total", panel.Entries.Single().Expression);
    }

    [AvaloniaFact]
    public void IgnoresAnEmptyExpression()
    {
        var panel = new WatchPanel();

        panel.Add("   ");

        Assert.Empty(panel.Entries);
    }

    [AvaloniaFact]
    public void DoesNotAddTheSameExpressionTwice()
    {
        var panel = new WatchPanel();

        panel.Add("total");
        panel.Add("total");

        Assert.Single(panel.Entries);
    }

    [AvaloniaFact]
    public void RemovesAnExpression()
    {
        var panel = new WatchPanel();
        panel.Add("total");

        panel.Remove(panel.Entries[0]);

        Assert.Empty(panel.Entries);
    }

    [AvaloniaFact]
    public async Task ShowsWhatEachExpressionEvaluatesTo()
    {
        var panel = new WatchPanel();
        panel.Add("total");
        panel.Add("i");

        await panel.RefreshAsync(expression => Task.FromResult<string?>(
            expression == "total" ? "6" : "4"));

        Assert.Equal("total = 6", panel.Entries[0].Display);
        Assert.Equal("i = 4", panel.Entries[1].Display);
    }

    [AvaloniaFact]
    public async Task MarksAnExpressionThatCouldNotBeEvaluated()
    {
        // Usually the variable is not in scope in the frame being looked at,
        // which the user needs to see rather than a blank.
        var panel = new WatchPanel();
        panel.Add("missing");

        await panel.RefreshAsync(_ => Task.FromResult<string?>(null));

        Assert.True(panel.Entries[0].Failed);
        Assert.Contains("not available", panel.Entries[0].Display);
    }

    [AvaloniaFact]
    public async Task KeepsEvaluatingAfterOneExpressionFails()
    {
        var panel = new WatchPanel();
        panel.Add("missing");
        panel.Add("total");

        await panel.RefreshAsync(expression => Task.FromResult<string?>(
            expression == "missing" ? null : "6"));

        Assert.True(panel.Entries[0].Failed);
        Assert.Equal("total = 6", panel.Entries[1].Display);
    }

    [AvaloniaFact]
    public async Task ForgetsValuesWhenExecutionResumes()
    {
        // A value from the previous stop would be quietly wrong.
        var panel = new WatchPanel();
        panel.Add("total");

        await panel.RefreshAsync(_ => Task.FromResult<string?>("6"));
        panel.ClearValues();

        Assert.Null(panel.Entries[0].Value);
        Assert.Contains("…", panel.Entries[0].Display);
    }

    [AvaloniaFact]
    public void ReportsWhenTheWatchedSetChanges()
    {
        var panel = new WatchPanel();

        var raised = 0;
        panel.Changed += (_, _) => raised++;

        panel.Add("total");
        panel.Remove(panel.Entries[0]);

        Assert.Equal(2, raised);
    }
}
