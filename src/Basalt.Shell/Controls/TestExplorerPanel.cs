using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Basalt.Core.Localization;
using Basalt.Core.Services;

namespace Basalt.Shell.Controls;

/// <summary>
/// A node in the test tree: a project, a class, or a test.
///
/// One type for all three so the tree needs no polymorphism to walk, and the
/// state of a container follows from its children rather than being tracked
/// separately.
/// </summary>
public sealed class TestNode
{
    public TestNode(string name, TestCase? test = null)
    {
        Name = name;
        Test = test;
    }

    public string Name { get; }

    /// <summary>The test itself, for a leaf; null for a container.</summary>
    public TestCase? Test { get; }

    public List<TestNode> Children { get; } = [];

    public TestOutcome Outcome { get; set; } = TestOutcome.NotRun;

    public TestResult? Result { get; set; }

    /// <summary>
    /// Whether the search and toggles leave this node visible. Set by
    /// <see cref="TestFilter.Apply"/>; a node is shown when a test under it is.
    /// </summary>
    public bool IsShown { get; set; } = true;

    /// <summary>
    /// Whether the user has opened this container. Kept across redraws, and
    /// untouched by a search, so clearing the search puts back what the user
    /// left.
    /// </summary>
    public bool IsExpanded { get; set; }

    /// <summary>
    /// Whether the tree shows this container open right now: while a search is
    /// active, when it holds a match; otherwise as the user left it.
    /// </summary>
    public bool IsOpen { get; set; }

    /// <summary>The children the tree draws: the ones the filter leaves visible.</summary>
    public IEnumerable<TestNode> ShownChildren => Children.Where(c => c.IsShown);

    /// <summary>Every test at or below this node.</summary>
    public IEnumerable<TestNode> Leaves =>
        Test is not null ? [this] : Children.SelectMany(c => c.Leaves);

    /// <summary>
    /// What to show beside the name.
    ///
    /// A container takes the worst outcome among its children: a class with
    /// one failing test is a failing class, which is what the user needs to
    /// see without opening it.
    /// </summary>
    public TestOutcome EffectiveOutcome
    {
        get
        {
            if (Test is not null) return Outcome;

            var outcomes = Children.Select(c => c.EffectiveOutcome).ToList();

            if (outcomes.Count == 0) return TestOutcome.NotRun;
            if (outcomes.Contains(TestOutcome.Running)) return TestOutcome.Running;
            if (outcomes.Contains(TestOutcome.Failed)) return TestOutcome.Failed;
            if (outcomes.All(o => o == TestOutcome.Skipped)) return TestOutcome.Skipped;
            if (outcomes.Any(o => o == TestOutcome.Passed)) return TestOutcome.Passed;

            return TestOutcome.NotRun;
        }
    }

    public string Display => Test is null || Result is null
        ? Name
        : $"{Name}   {Result.Duration.TotalMilliseconds:F0} ms";
}

/// <summary>
/// The tests of the solution, grouped by project and class.
///
/// Grouping comes from the fully-qualified names the runner reports, so the
/// tree cannot disagree with what would actually run.
/// </summary>
public sealed class TestExplorerPanel : UserControl
{
    private readonly TreeView _tree;
    private readonly TextBlock _summary;
    private readonly TextBox _details;
    private readonly Button _runAll;
    private readonly Button _runFailed;
    private readonly Button _runSelected;
    private readonly EmptyState _empty;
    private readonly TextBox _search;
    private readonly ToggleButton _showPassed;
    private readonly ToggleButton _showFailed;
    private readonly ToggleButton _showNotRun;
    private readonly ToggleButton _showSkipped;
    private readonly DispatcherTimer _searchDelay;

    /// <summary>The rows created since the last redraw, read by <see cref="RememberExpansion"/>.</summary>
    private readonly HashSet<TreeViewItem> _preparedRows = [];

    /// <summary>How long typing must pause before the search applies.</summary>
    private static readonly TimeSpan DefaultSearchDelay = TimeSpan.FromMilliseconds(150);

    private IReadOnlyList<TestCase> _tests = [];
    private List<TestNode> _roots = [];
    private TestFilter _filter = TestFilter.None;
    private readonly Dictionary<string, TestNode> _byName = new(StringComparer.Ordinal);

    public TestExplorerPanel() : this(DefaultSearchDelay)
    {
    }

    /// <summary>
    /// Builds the panel with the given pause before a typed search applies.
    /// </summary>
    /// <remarks>
    /// Re-filtering on every keystroke rebuilds the whole tree on the UI thread
    /// once per letter. The pause lets a word land first, and each keystroke
    /// restarts it. The delay is a parameter so a test can read a pending search
    /// without waiting out the default.
    /// </remarks>
    internal TestExplorerPanel(TimeSpan searchDelay)
    {
        _searchDelay = new DispatcherTimer { Interval = searchDelay };
        _searchDelay.Tick += (_, _) => FilterChanged();

        _summary = new TextBlock
        {
            Text = Localizer.Get(StringKeys.TestsNoneDiscovered),
            FontSize = PanelMetrics.SecondaryFontSize,
            Margin = new Thickness(Spacing.Normal, 0, Spacing.Normal, Spacing.Tight),
            VerticalAlignment = VerticalAlignment.Center
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        _runAll = new Button { Content = Localizer.Get(StringKeys.TestsRunAll) };
        _runAll.Click += (_, _) => RunRequested?.Invoke(this, RunScope.All);

        _runFailed = new Button { Content = Localizer.Get(StringKeys.TestsRunFailed), IsEnabled = false };
        _runFailed.Click += (_, _) => RunRequested?.Invoke(this, RunScope.Failed);

        // Disabled while nothing visible is selected: with nothing to run it does nothing.
        _runSelected = new Button { Content = Localizer.Get(StringKeys.TestsRunSelected), IsEnabled = false };
        _runSelected.Click += (_, _) => RunRequested?.Invoke(this, RunScope.Selected);

        var debugSelected = new Button { Content = Localizer.Get(StringKeys.TestsDebug) };
        debugSelected.Click += (_, _) => DebugRequested?.Invoke(this, EventArgs.Empty);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            Margin = new Thickness(Spacing.Small, Spacing.Tight)
        };

        buttons.Children.Add(_runAll);
        buttons.Children.Add(_runSelected);
        buttons.Children.Add(_runFailed);
        buttons.Children.Add(debugSelected);

        _tree = new TreeView
        {
            ItemTemplate = new FuncTreeDataTemplate<TestNode>(
                (node, _) => BuildRow(node),
                node => node?.ShownChildren ?? [])
        };

        _tree.ContainerPrepared += OpenPreparedRow;
        _tree.SelectionChanged += (_, _) =>
        {
            ShowDetails(_tree.SelectedItem as TestNode);
            UpdateRunSelected();
        };

        _tree.DoubleTapped += (_, _) =>
        {
            if (_tree.SelectedItem is TestNode { Result: { } result }
                && result.FilePath is { Length: > 0 })
            {
                FailureActivated?.Invoke(this, result);
            }
        };

        BuildContextMenu();

        _details = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
            FontSize = PanelMetrics.SecondaryFontSize,
            Height = 120,
            IsVisible = false
        };

        _search = new TextBox
        {
            PlaceholderText = Localizer.Get(StringKeys.TestsFilterWatermark),
            FontSize = PanelMetrics.FontSize
        };

        _showPassed = OutcomeToggle(Localizer.Get(StringKeys.TestsFilterPassed));
        _showFailed = OutcomeToggle(Localizer.Get(StringKeys.TestsFilterFailed));
        _showNotRun = OutcomeToggle(Localizer.Get(StringKeys.TestsFilterNotRun));
        _showSkipped = OutcomeToggle(Localizer.Get(StringKeys.TestsFilterSkipped));

        // TextChanging, not TextChanged: TextChanged is raised after the next
        // render, which would add a frame of lag before the pause even starts.
        _search.TextChanging += (_, _) => RestartSearchDelay();

        // A toggle is a click, not typing: it applies at once, to the search as it stands.
        _showPassed.IsCheckedChanged += (_, _) => FilterChanged();
        _showFailed.IsCheckedChanged += (_, _) => FilterChanged();
        _showNotRun.IsCheckedChanged += (_, _) => FilterChanged();
        _showSkipped.IsCheckedChanged += (_, _) => FilterChanged();

        var filterRow = BuildFilterRow();

        var layout = new DockPanel();

        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(filterRow, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(_summary, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(_details, Avalonia.Controls.Dock.Bottom);

        layout.Children.Add(buttons);
        layout.Children.Add(filterRow);
        layout.Children.Add(_summary);
        layout.Children.Add(_details);
        layout.Children.Add(_tree);

        // Nothing discovered: say so where the tree would be, and what to do.
        // The sentence is in the summary line above; here the icon alone says there is nothing yet.
        _empty = new EmptyState("", IconKind.Tests);
        // Named for a screen reader, which does not see the icon.
        Avalonia.Automation.AutomationProperties.SetName(_empty, Localizer.Get(StringKeys.TestsNoneDiscovered));
        layout.Children.Add(_empty);
        UpdateEmpty();

        Content = layout;
    }

    /// <summary>
    /// The search box, with the outcome toggles at its right end.
    /// </summary>
    /// <remarks>
    /// Every keystroke and toggle rebuilds the tree from the controls' current
    /// state, so the filter never holds a copy that could disagree with them.
    /// </remarks>
    private DockPanel BuildFilterRow()
    {
        var toggles = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight
        };

        toggles.Children.Add(_showPassed);
        toggles.Children.Add(_showFailed);
        toggles.Children.Add(_showNotRun);
        toggles.Children.Add(_showSkipped);

        DockPanel.SetDock(toggles, Avalonia.Controls.Dock.Right);

        var row = new DockPanel
        {
            Margin = new Thickness(Spacing.Small, Spacing.Tight)
        };

        row.Children.Add(toggles);
        row.Children.Add(_search);

        return row;
    }

    private static ToggleButton OutcomeToggle(string text) => new()
    {
        Content = text,
        FontSize = PanelMetrics.SecondaryFontSize
    };

    /// <summary>Which tests the user asked to run.</summary>
    public enum RunScope { All, Selected, Failed }

    public event EventHandler<RunScope>? RunRequested;

    /// <summary>Raised when the user asks to debug the selected test.</summary>
    public event EventHandler? DebugRequested;

    /// <summary>Raised when the user asks to see where a test failed.</summary>
    public event EventHandler<TestResult>? FailureActivated;

    /// <summary>What the test window's context menu can ask for.</summary>
    public enum TestCommand { Run, Debug, OpenSource, CopyName, CopyFailure }

    /// <summary>Raised when the user picks something from the menu.</summary>
    public event EventHandler<TestCommand>? CommandChosen;

    internal ContextMenu? Menu => _tree.ContextMenu;

    /// <summary>
    /// Builds the menu.
    ///
    /// Debugging and opening the source need a single test, not a class;
    /// copying a failure needs one that failed.
    /// </summary>
    private void BuildContextMenu()
    {
        bool OneTest() => SelectedNode is { Test: not null };
        bool AnySelection() => SelectedNode is not null;
        bool Failed() => SelectedNode is { Result.Outcome: TestOutcome.Failed };

        void Choose(TestCommand command) => CommandChosen?.Invoke(this, command);

        _tree.ContextMenu = ContextMenus.Build(
        [
            new(Localizer.Get(StringKeys.TestsRun), () => Choose(TestCommand.Run))
                { IsAvailable = AnySelection, Icon = IconKind.Run },
            new(Localizer.Get(StringKeys.TestsDebug), () => Choose(TestCommand.Debug))
                { IsAvailable = OneTest, Icon = IconKind.Debug },
            MenuAction.Separator,
            new(Localizer.Get(StringKeys.TestsOpenSource), () => Choose(TestCommand.OpenSource))
                { IsAvailable = OneTest, Icon = IconKind.Open },
            MenuAction.Separator,
            new(Localizer.Get(StringKeys.TestsCopyName), () => Choose(TestCommand.CopyName))
                { IsAvailable = OneTest, Icon = IconKind.Copy },
            new(Localizer.Get(StringKeys.TestsCopyFailure), () => Choose(TestCommand.CopyFailure))
                { IsAvailable = Failed, Icon = IconKind.Error }
        ]);
    }

    /// <summary>
    /// The node the user has selected, unless the search now hides it.
    /// </summary>
    /// <remarks>
    /// Every command reads the selection through here, so a hidden selection
    /// is no selection for the menu, the Run buttons and Copy or Open alike.
    /// Otherwise Copy Name could act on a test the user cannot see.
    /// </remarks>
    private TestNode? SelectedNode => _tree.SelectedItem is TestNode { IsShown: true } node ? node : null;

    /// <summary>The test the menu applies to, or null when a class is selected or hidden.</summary>
    public TestCase? SelectedTest => SelectedNode?.Test;

    /// <summary>What the selected test reported, when it has run and is shown.</summary>
    public TestResult? SelectedResult => SelectedNode?.Result;

    /// <summary>Every project in the tree, whether the filter shows it or not.</summary>
    internal IReadOnlyList<TestNode> Roots => _roots;

    internal string Summary => _summary.Text ?? "";

    internal string Details => _details.Text ?? "";

    /// <summary>
    /// The visible tests under the selected node: none when nothing is selected.
    /// </summary>
    /// <remarks>
    /// "Run Selected" runs what the user picked and nothing else. Running
    /// everything when nothing is picked would be a second meaning of the same
    /// button, and "Run All" is the command for everything.
    /// </remarks>
    public IReadOnlyList<TestCase> SelectedTests
    {
        get
        {
            if (SelectedNode is not { } node) return [];

            return [.. node.Leaves.Where(l => l.IsShown).Select(l => l.Test!)];
        }
    }

    /// <summary>
    /// Every test discovered, regardless of selection or search.
    ///
    /// "Run All" means all: a selection in the tree must not narrow it to one
    /// class or project the way <see cref="SelectedTests"/> does, and a search
    /// that hides tests must not hide them from the run either.
    /// </summary>
    public IReadOnlyList<TestCase> AllTests => _tests;

    /// <summary>The tests that failed the last time they ran.</summary>
    public IReadOnlyList<TestCase> FailedTests =>
        [.. _byName.Values
            .Where(n => n.Test is not null && n.Outcome == TestOutcome.Failed)
            .Select(n => n.Test!)];

    /// <summary>
    /// Builds the tree from the discovered tests.
    ///
    /// Grouped by project, then by the class part of each name. A search or
    /// toggle the user has set stays in force across a rediscovery.
    /// </summary>
    public void Show(IReadOnlyList<TestCase> tests)
    {
        _tests = tests;
        _byName.Clear();

        var roots = new List<TestNode>();

        foreach (var byProject in tests.GroupBy(t => t.ProjectPath, StringComparer.Ordinal))
        {
            var project = new TestNode(Path.GetFileNameWithoutExtension(byProject.Key));

            foreach (var byClass in byProject.GroupBy(t => t.ClassName, StringComparer.Ordinal))
            {
                var className = new TestNode(
                    byClass.Key.Length == 0 ? Localizer.Get(StringKeys.TestsNoClass) : ShortClassName(byClass.Key));

                foreach (var test in byClass.OrderBy(t => t.DisplayName, StringComparer.Ordinal))
                {
                    var leaf = new TestNode(test.DisplayName, test);

                    _byName[test.FullyQualifiedName] = leaf;
                    className.Children.Add(leaf);
                }

                project.Children.Add(className);
            }

            roots.Add(project);
        }

        _roots = roots;

        RebuildTree();
        UpdateSummary();
    }

    /// <summary>The class without its namespace, which the tree already shows.</summary>
    private static string ShortClassName(string className)
    {
        var dot = className.LastIndexOf('.');

        return dot < 0 ? className : className[(dot + 1)..];
    }

    /// <summary>Marks tests as running, so the user sees something happen.</summary>
    public void MarkRunning(IEnumerable<TestCase> tests)
    {
        foreach (var test in tests)
        {
            if (!_byName.TryGetValue(test.FullyQualifiedName, out var node)) continue;

            node.Outcome = TestOutcome.Running;
            node.Result = null;
        }

        RebuildTree();
        _summary.Text = Localizer.Get(StringKeys.TestsRunning);
    }

    /// <summary>Records what a run produced.</summary>
    public void ShowResults(IReadOnlyList<TestResult> results)
    {
        foreach (var result in results)
        {
            if (!_byName.TryGetValue(result.FullyQualifiedName, out var node)) continue;

            node.Outcome = result.Outcome;
            node.Result = result;
        }

        // A test that was running and got no result did not run after all,
        // usually because the build failed.
        foreach (var node in _byName.Values.Where(n => n.Outcome == TestOutcome.Running))
            node.Outcome = TestOutcome.NotRun;

        // The outcomes have changed, so a Failed toggle must now pick different tests.
        RebuildTree();
        UpdateSummary();

        _runFailed.IsEnabled = FailedTests.Count > 0;
    }

    /// <summary>
    /// Totals for every test, then how many the search leaves showing.
    /// </summary>
    /// <remarks>
    /// The totals never narrow: "Run All" and the counts are about the whole
    /// suite, and the shown count is an extra fact beside them.
    /// </remarks>
    private void UpdateSummary()
    {
        if (_tests.Count == 0)
        {
            _summary.Text = Localizer.Get(StringKeys.TestsNoneDiscovered);
            UpdateEmpty();
            return;
        }

        var counted = _byName.Values.ToList();

        var passed = counted.Count(n => n.Outcome == TestOutcome.Passed);
        var failed = counted.Count(n => n.Outcome == TestOutcome.Failed);
        var skipped = counted.Count(n => n.Outcome == TestOutcome.Skipped);

        var text = passed + failed + skipped == 0
            ? string.Format(Localizer.Get(StringKeys.TestsCount), _tests.Count)
            : string.Format(Localizer.Get(StringKeys.TestsSummary), passed, failed, skipped, _tests.Count);

        if (_filter.IsActive)
        {
            var shown = counted.Count(n => n.IsShown);

            text += " " + string.Format(Localizer.Get(StringKeys.TestsShown), shown);
        }

        _summary.Text = text;

        UpdateEmpty();
    }

    /// <summary>
    /// Starts, or restarts, the pause that ends a burst of typing.
    /// </summary>
    private void RestartSearchDelay()
    {
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    /// <summary>
    /// Applies the search and toggles now: the pause ended, or a toggle was clicked.
    /// </summary>
    /// <remarks>
    /// Reads the controls rather than a saved copy, so a toggle also applies a
    /// search still waiting on its pause, and the pause is cancelled.
    /// </remarks>
    private void FilterChanged()
    {
        _searchDelay.Stop();
        RebuildTree();
        UpdateSummary();
    }

    /// <summary>
    /// Reads the search box and toggles, applies them to every node, and redraws.
    /// </summary>
    private void RebuildTree()
    {
        // Before the filter reopens or closes anything: what the user did to the
        // rows since the last redraw must be kept.
        RememberExpansion();

        _filter = CurrentFilter();
        _filter.Apply(_roots);

        Refresh();
        UpdateRunSelected();
    }

    /// <summary>"Run Selected" is available only while a visible test or class is selected.</summary>
    private void UpdateRunSelected() => _runSelected.IsEnabled = SelectedNode is not null;

    private TestFilter CurrentFilter() => new(
        _search.Text?.Trim() ?? "",
        _showPassed.IsChecked == true,
        _showFailed.IsChecked == true,
        _showNotRun.IsChecked == true,
        _showSkipped.IsChecked == true);

    private void UpdateEmpty()
    {
        var none = _tests.Count == 0;
        _empty.IsVisible = none;
        _tree.IsVisible = !none;
    }

    private void ShowDetails(TestNode? node)
    {
        if (node?.Result is not { Outcome: TestOutcome.Failed } result)
        {
            _details.IsVisible = false;
            _details.Text = "";
            return;
        }

        _details.Text = result.StackTrace is { Length: > 0 } stack
            ? $"{result.Message}\n\n{stack}"
            : result.Message ?? "";

        _details.IsVisible = true;
    }

    /// <summary>
    /// Redraws the tree with the projects the filter shows.
    ///
    /// The nodes are the same objects; reassigning the source is what makes
    /// the view read their new state. The selection survives the reassignment
    /// (a headless test keeps a selected test across a search), so the
    /// filter needs no step of its own to keep it.
    /// </summary>
    private void Refresh()
    {
        _tree.ItemsSource = null;
        _tree.ItemsSource = _roots.Where(r => r.IsShown).ToList();
    }

    /// <summary>
    /// Opens each row as the tree shows its node, as the row is created.
    /// </summary>
    /// <remarks>
    /// Rows are created lazily, so a flag set on a node only takes effect when
    /// its row appears. A row also raises ContainerPrepared for its own
    /// children, which is why the handler subscribes again for each row.
    /// </remarks>
    private void OpenPreparedRow(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not TreeViewItem row) return;

        row.IsExpanded = row.DataContext is TestNode { IsOpen: true };
        _preparedRows.Add(row);

        row.ContainerPrepared -= OpenPreparedRow;
        row.ContainerPrepared += OpenPreparedRow;
    }

    /// <summary>
    /// Records each container the user opened or closed, before the rows are redrawn.
    /// </summary>
    /// <remarks>
    /// Read from the rows, not from their Expanded and Collapsed events: tearing
    /// rows down raises Collapsed too, so an event-driven record closed the whole
    /// tree on every redraw (ElementTreePanel found the same). A row that differs
    /// from what the tree showed was changed by the user.
    /// </remarks>
    private void RememberExpansion()
    {
        foreach (var row in _preparedRows)
        {
            if (row.DataContext is not TestNode { Test: null } node) continue;

            if (row.IsExpanded != node.IsOpen) node.IsExpanded = row.IsExpanded;
        }

        _preparedRows.Clear();
    }

    private static Control BuildRow(TestNode? node)
    {
        if (node is null) return new TextBlock();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Small
        };

        row.Children.Add(new IconView
        {
            Kind = IconFor(node.EffectiveOutcome),
            IconSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });

        row.Children.Add(new TextBlock
        {
            Text = node.Display,
            FontSize = PanelMetrics.FontSize,
            VerticalAlignment = VerticalAlignment.Center
        });

        return row;
    }

    /// <summary>The icon an outcome is shown with.</summary>
    internal static IconKind IconFor(TestOutcome outcome) => outcome switch
    {
        TestOutcome.Passed => IconKind.Run,
        TestOutcome.Failed => IconKind.Error,
        TestOutcome.Skipped => IconKind.Warning,
        TestOutcome.Running => IconKind.Refresh,
        _ => IconKind.Breakpoint
    };

    internal void SelectForTests(TestNode node) => _tree.SelectedItem = node;

    /// <summary>Types in the search box and lets the pause run out. For tests.</summary>
    internal void SearchForTests(string text)
    {
        TypeForTests(text);
        FilterChanged();
    }

    /// <summary>Types in the search box without waiting out the pause. For tests.</summary>
    internal void TypeForTests(string text) => _search.Text = text;

    /// <summary>Whether a typed search is still waiting on its pause. For tests.</summary>
    internal bool SearchPendingForTests => _searchDelay.IsEnabled;

    /// <summary>Turns an outcome toggle on or off, as clicking it does. For tests.</summary>
    internal void SetOutcomeToggleForTests(TestOutcome outcome, bool isOn) =>
        OutcomeToggleFor(outcome).IsChecked = isOn;

    private ToggleButton OutcomeToggleFor(TestOutcome outcome) => outcome switch
    {
        TestOutcome.Passed => _showPassed,
        TestOutcome.Failed => _showFailed,
        TestOutcome.NotRun => _showNotRun,
        TestOutcome.Skipped => _showSkipped,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The explorer has no toggle for this outcome.")
    };
}
