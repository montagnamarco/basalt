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
    public void ThePanelWearsItsIconWithoutRepeatingItsName()
    {
        var inner = new TextBlock { Text = "content" };

        var header = new PanelHeader("Problems", IconKind.Problems, inner);

        // Shown in a window: an unattached control has no visual tree.
        var window = new Window { Content = header, Width = 300, Height = 200 };

        window.Show();
        window.UpdateLayout();

        var icons = header.GetVisualDescendants().OfType<IconView>().ToList();

        Assert.Contains(icons, i => i.Kind == IconKind.Problems);

        // No name: Dock's own tab carries it, and repeating it read as
        // "Problems" twice, one line above the other.
        var labels = header.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "")
            .ToList();

        Assert.DoesNotContain("Problems", labels);

        // And what it is a header for is still there.
        Assert.Contains(inner, header.GetVisualDescendants().OfType<TextBlock>());

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
