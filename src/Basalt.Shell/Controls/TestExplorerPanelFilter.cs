using Basalt.Core.Services;

namespace Basalt.Shell.Controls;

/// <summary>
/// What the Test Explorer shows: the text in its search box and the outcomes
/// its toggles allow.
/// </summary>
/// <remarks>
/// Kept apart from the controls so the rules read on their own and can be
/// tested without a window.
///
/// The text is matched against the fully-qualified name, which holds the
/// namespace, the class and the method, so one substring search covers all
/// three. A test is shown only when it matches the text and its outcome is
/// allowed.
///
/// A running test is never hidden by an outcome toggle. Run Failed marks the
/// failed tests running at once; with Failed toggled on, hiding them then
/// would empty the tree for the length of the run.
/// </remarks>
public sealed record TestFilter(string Text, bool ShowPassed, bool ShowFailed, bool ShowNotRun, bool ShowSkipped)
{
    /// <summary>A filter that shows every test.</summary>
    public static TestFilter None { get; } = new("", false, false, false, false);

    /// <summary>Whether anything is narrowed, which is when the summary says how many are shown.</summary>
    public bool IsActive => Text.Length > 0 || HasOutcomeToggle;

    private bool HasOutcomeToggle => ShowPassed || ShowFailed || ShowNotRun || ShowSkipped;

    public bool Matches(TestCase test, TestOutcome outcome) =>
        MatchesText(test) && MatchesOutcome(outcome);

    /// <summary>
    /// Marks every node shown or hidden, and opens each container that holds a
    /// match while a filter is active.
    /// </summary>
    public void Apply(IEnumerable<TestNode> roots)
    {
        foreach (var root in roots) Mark(root);
    }

    /// <summary>Returns how many tests at or below the node are shown.</summary>
    private int Mark(TestNode node)
    {
        var shown = node.Test is { } test
            ? (Matches(test, node.Outcome) ? 1 : 0)
            : node.Children.Sum(child => Mark(child));

        node.IsShown = shown > 0;

        // While a search is active, a container with a match is opened so the
        // match is visible at once. Otherwise the tree shows what the user chose,
        // which Mark never overwrites: the user's choice is not the filter's to change.
        node.IsOpen = IsActive ? node.Test is null && shown > 0 : node.IsExpanded;

        return shown;
    }

    private bool MatchesText(TestCase test) =>
        Text.Length == 0 || test.FullyQualifiedName.Contains(Text, StringComparison.OrdinalIgnoreCase);

    private bool MatchesOutcome(TestOutcome outcome)
    {
        // With no toggle on, every outcome is shown, as in Visual Studio.
        if (!HasOutcomeToggle || outcome == TestOutcome.Running) return true;

        return outcome switch
        {
            TestOutcome.Passed => ShowPassed,
            TestOutcome.Failed => ShowFailed,

            // A test with no result has not run: that is what "Not run" names.
            TestOutcome.NotRun => ShowNotRun,
            TestOutcome.Skipped => ShowSkipped,
            _ => false
        };
    }
}
