using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Core.Services;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>The panels that show where a stopped program is.</summary>
public class CallStackPanelTests
{
    private static StackFrame Frame(string method, int line) =>
        new(method, "/src/Program.vb", line);

    [AvaloniaFact]
    public void SaysNothingIsRunningBeforeAnythingStops()
    {
        var panel = new CallStackPanel();

        Assert.Empty(panel.Frames);
    }

    [AvaloniaFact]
    public void ListsTheFramesItWasGiven()
    {
        var panel = new CallStackPanel();

        panel.Show([Frame("Main", 6), Frame("Caller", 20)]);

        Assert.Equal(2, panel.Frames.Count);
    }

    [AvaloniaFact]
    public void StartsInTheInnermostFrame()
    {
        // Where execution stopped is what the user was looking at.
        var panel = new CallStackPanel();

        panel.Show([Frame("Main", 6), Frame("Caller", 20)]);

        Assert.Equal(0, panel.SelectedFrame);
    }

    [AvaloniaFact]
    public void ReportsWhenTheUserLooksAtAnotherFrame()
    {
        var panel = new CallStackPanel();
        panel.Show([Frame("Main", 6), Frame("Caller", 20)]);

        var chosen = -1;
        panel.FrameSelected += (_, index) => chosen = index;

        panel.SelectForTests(1);

        Assert.Equal(1, chosen);
    }

    [AvaloniaFact]
    public void EmptiesWhenExecutionResumes()
    {
        var panel = new CallStackPanel();
        panel.Show([Frame("Main", 6)]);

        panel.Clear();

        Assert.Empty(panel.Frames);
    }
}

/// <summary>The variables of the frame being looked at.</summary>
public class VariablesPanelTests
{
    private static VariableNode Node(string name, string value, bool hasChildren = false) =>
        new(new VariableValue(name, value, "Integer", hasChildren));

    [AvaloniaFact]
    public void ShowsNameValueAndType()
    {
        Assert.Equal("total = 6   (Integer)", Node("total", "6").Display);
    }

    [AvaloniaFact]
    public void LeavesOutTheTypeWhenThereIsNone()
    {
        var node = new VariableNode(new VariableValue("x", "1", "", false));

        Assert.Equal("x = 1", node.Display);
    }

    [AvaloniaFact]
    public void ListsTheVariablesItWasGiven()
    {
        var panel = new VariablesPanel();

        panel.Show([Node("total", "6"), Node("i", "4")]);

        Assert.Equal(2, panel.Nodes.Count);
    }

    [AvaloniaFact]
    public async Task DoesNotFetchChildrenUntilAskedTo()
    {
        // An object graph would otherwise be walked in full to show one frame.
        var fetched = 0;

        var node = new VariableNode(
            new VariableValue("customer", "{Customer}", "Customer", true),
            reference: 7,
            expand: _ =>
            {
                fetched++;
                return Task.FromResult<IReadOnlyList<VariableNode>>([Node("Name", "\"Ada\"")]);
            });

        Assert.Empty(node.Children);
        Assert.Equal(0, fetched);

        await node.LoadChildrenAsync();

        Assert.Single(node.Children);
        Assert.Equal(1, fetched);
    }

    [AvaloniaFact]
    public async Task FetchesChildrenOnlyOnce()
    {
        var fetched = 0;

        var node = new VariableNode(
            new VariableValue("customer", "{Customer}", "Customer", true),
            reference: 7,
            expand: _ =>
            {
                fetched++;
                return Task.FromResult<IReadOnlyList<VariableNode>>([Node("Name", "\"Ada\"")]);
            });

        await node.LoadChildrenAsync();
        await node.LoadChildrenAsync();

        Assert.Equal(1, fetched);
    }
}

/// <summary>Listing and switching breakpoints.</summary>
public class BreakpointsPanelTests
{
    private static Breakpoint At(int line, string? condition = null) =>
        new("/src/Program.vb", line, Condition: condition);

    [AvaloniaFact]
    public void ListsTheBreakpointsItWasGiven()
    {
        var panel = new BreakpointsPanel();

        panel.Show([At(6), At(12)]);

        Assert.Equal(2, panel.Breakpoints.Count);
    }

    [AvaloniaFact]
    public void ReportsABreakpointBeingSwitchedOff()
    {
        var panel = new BreakpointsPanel();
        panel.Show([At(6)]);

        Breakpoint? changed = null;
        panel.Toggled += (_, breakpoint) => changed = breakpoint;

        panel.ToggleForTests(At(6), enabled: false);

        Assert.NotNull(changed);
        Assert.False(changed!.Enabled);
    }

    [AvaloniaFact]
    public void KeepsTheSwitchedStateItReported()
    {
        var panel = new BreakpointsPanel();
        panel.Show([At(6)]);

        panel.ToggleForTests(At(6), enabled: false);

        Assert.False(panel.Breakpoints[0].Enabled);
    }

    [AvaloniaFact]
    public void SaysNothingWhenTheStateHasNotChanged()
    {
        var panel = new BreakpointsPanel();
        panel.Show([At(6)]);

        var raised = 0;
        panel.Toggled += (_, _) => raised++;

        panel.ToggleForTests(At(6), enabled: true);

        Assert.Equal(0, raised);
    }

    [AvaloniaFact]
    public void EmptiesWhenAskedTo()
    {
        var panel = new BreakpointsPanel();
        panel.Show([At(6)]);

        panel.Clear();

        Assert.Empty(panel.Breakpoints);
    }

    [AvaloniaFact]
    public void AsksForTheConditionToBeEdited()
    {
        // The panel could always show a condition and never let one be
        // changed, which is what this closes: a value shown but not editable
        // reads as a bug.
        var panel = new BreakpointsPanel();

        panel.Show([At(12)]);

        Breakpoint? asked = null;
        panel.ConditionRequested += (_, breakpoint) => asked = breakpoint;

        panel.RequestConditionForTests(panel.Breakpoints[0]);

        Assert.Equal(12, asked?.Line);
    }

    [AvaloniaFact]
    public void AsksForABreakpointToBeRemoved()
    {
        var panel = new BreakpointsPanel();

        panel.Show([At(12)]);

        Breakpoint? asked = null;
        panel.RemoveRequested += (_, breakpoint) => asked = breakpoint;

        panel.RequestRemoveForTests(panel.Breakpoints[0]);

        Assert.Equal("/src/Program.vb", asked?.FilePath);
    }

    [AvaloniaFact]
    public void SaysWhetherAConditionIsBeingAddedOrChanged()
    {
        // The wording tells the user whether one is already there.
        Assert.Contains("Add Condition…", MenuOf(At(12)));
        Assert.Contains("Edit Condition…", MenuOf(At(12, "i = 3")));
    }

    /// <summary>The menu entries of a breakpoint's row.</summary>
    private static IReadOnlyList<string> MenuOf(Breakpoint breakpoint)
    {
        var panel = new BreakpointsPanel();

        panel.Show([breakpoint]);

        var row = panel.BuildRowForTests(breakpoint);

        return
        [
            .. (row.ContextMenu?.ItemsSource ?? Array.Empty<object>())
                .OfType<MenuItem>()
                .Select(item => item.Header?.ToString() ?? "")
        ];
    }
}
