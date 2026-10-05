using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;

namespace Basalt.Tests;

/// <summary>
/// Telling one panel from another at a glance.
///
/// Dock carries a title on a tool and nothing else — no icon in its model,
/// none in its controls — so every panel looked alike until its name was
/// read, and the three buttons on the title bar were blank grey squares.
/// </summary>
public class PanelIconTests
{
    /// <summary>Every tool the IDE defines, built the way the IDE builds it.</summary>
    private static IReadOnlyList<IdeTool> Tools() =>
    [
        new SolutionExplorerTool(), new ToolboxTool(), new PropertiesTool(),
        new ProblemsTool(), new OutputTool(), new SearchTool(),
        new OutlineTool(), new ReferencesTool(), new CallStackTool(),
        new VariablesTool(), new BreakpointsTool(), new TestsTool(),
        new AssistantTool(), new GitChangesTool(), new GitBranchesTool(),
        new GitDiffTool(), new GitHistoryTool(), new TerminalTool("/tmp", 1)
    ];

    [Fact]
    public void EveryPanelHasAnIcon()
    {
        // Unlike a menu, where an icon has to earn its place, a panel is
        // found by its shape in a row of tabs: here the icon is the point.
        var bare = Tools()
            .Where(t => t.Icon == IconKind.None)
            .Select(t => t.Title ?? t.Id ?? "?")
            .ToList();

        Assert.True(bare.Count == 0,
            "These panels have no icon:\n" + string.Join("\n", bare));
    }

    [Fact]
    public void NoIconStandsForTwoPanels()
    {
        // Two panels behind one icon is two tabs the eye cannot separate,
        // which is the whole reason for having icons here.
        var shared = Tools()
            .GroupBy(t => t.Icon)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => t.Title))}")
            .ToList();

        Assert.True(shared.Count == 0,
            "These icons stand for more than one panel:\n" + string.Join("\n", shared));
    }

    [AvaloniaFact]
    public void ThePanelAddsNoRowOfItsOwnWhenItHasNoActions()
    {
        // Dock's title bar carries the name; a second bar carrying only an
        // icon stacked a third bar over the content of every panel.
        var inner = new TextBlock { Text = "content" };

        var header = new PanelHeader("Problems", inner);

        var window = new Window { Content = header, Width = 300, Height = 200 };

        window.Show();
        window.UpdateLayout();

        // The content sits directly in the header: no icon, no name, no row.
        Assert.Same(inner, header.Content);
        Assert.Empty(header.GetVisualDescendants().OfType<IconView>());

        var labels = header.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        Assert.DoesNotContain("Problems", labels);
        Assert.Contains("content", labels);

        window.Close();
    }

    [AvaloniaFact]
    public void ThePanelAddsOneCompactRowWhenItHasActions()
    {
        var inner = new TextBlock { Text = "content" };
        var actions = new Button { Content = "go" };

        var header = new PanelHeader("Explorer", inner, actions);

        var window = new Window { Content = header, Width = 300, Height = 200 };

        window.Show();
        window.UpdateLayout();

        Assert.Same(actions, header.Tools);

        // One row, of toolbar height: the actions' own height plus padding.
        var row = actions.GetVisualAncestors().OfType<Border>().First();
        Assert.Equal(actions.Bounds.Height + Spacing.Hairline * 2 + 1, row.Bounds.Height);
        Assert.Equal(0, row.Bounds.Top);

        // The content starts where the row ends: nothing else is stacked between.
        Assert.Equal(row.Bounds.Bottom, inner.Bounds.Top);

        window.Close();
    }

    [AvaloniaFact]
    public void TheTitleBarButtonsHaveGlyphs()
    {
        // The three grey squares: Dock names the buttons, its theme draws
        // nothing in them.
        foreach (var kind in new[] { IconKind.Close, IconKind.Pin, IconKind.Menu })
            Assert.NotNull(IdeIcons.PathFor(kind));
    }
}
