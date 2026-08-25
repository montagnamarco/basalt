using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
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

    private IReadOnlyList<TestCase> _tests = [];
    private readonly Dictionary<string, TestNode> _byName = new(StringComparer.Ordinal);

    public TestExplorerPanel()
    {
        _summary = new TextBlock
        {
            Text = "No tests discovered.",
            FontSize = 11,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center
        };

        _runAll = new Button { Content = "Run All", FontSize = 11 };
        _runAll.Click += (_, _) => RunRequested?.Invoke(this, RunScope.All);

        _runFailed = new Button { Content = "Run Failed", FontSize = 11, IsEnabled = false };
        _runFailed.Click += (_, _) => RunRequested?.Invoke(this, RunScope.Failed);

        var runSelected = new Button { Content = "Run Selected", FontSize = 11 };
        runSelected.Click += (_, _) => RunRequested?.Invoke(this, RunScope.Selected);

        var debugSelected = new Button { Content = "Debug", FontSize = 11 };
        debugSelected.Click += (_, _) => DebugRequested?.Invoke(this, EventArgs.Empty);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(6, 4)
        };

        buttons.Children.Add(_runAll);
        buttons.Children.Add(runSelected);
        buttons.Children.Add(_runFailed);
        buttons.Children.Add(debugSelected);

        _tree = new TreeView
        {
            ItemTemplate = new FuncTreeDataTemplate<TestNode>(
                (node, _) => BuildRow(node),
                node => node?.Children ?? [])
        };

        _tree.SelectionChanged += (_, _) => ShowDetails(_tree.SelectedItem as TestNode);

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
            FontSize = 11,
            Height = 120,
            IsVisible = false
        };

        var layout = new DockPanel();

        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(_summary, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(_details, Avalonia.Controls.Dock.Bottom);

        layout.Children.Add(buttons);
        layout.Children.Add(_summary);
        layout.Children.Add(_details);
        layout.Children.Add(_tree);

        Content = layout;
    }

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
        bool OneTest() => _tree.SelectedItem is TestNode { Test: not null };
        bool AnySelection() => _tree.SelectedItem is TestNode;
        bool Failed() => _tree.SelectedItem is TestNode { Result.Outcome: TestOutcome.Failed };

        void Choose(TestCommand command) => CommandChosen?.Invoke(this, command);

        _tree.ContextMenu = ContextMenus.Build(
        [
            new("Run", () => Choose(TestCommand.Run))
                { IsAvailable = AnySelection, Icon = IconKind.Run },
            new("Debug", () => Choose(TestCommand.Debug))
                { IsAvailable = OneTest, Icon = IconKind.Debug },
            MenuAction.Separator,
            new("Open Source", () => Choose(TestCommand.OpenSource))
                { IsAvailable = OneTest, Icon = IconKind.Open },
            MenuAction.Separator,
            new("Copy Name", () => Choose(TestCommand.CopyName))
                { IsAvailable = OneTest, Icon = IconKind.Copy },
            new("Copy Failure", () => Choose(TestCommand.CopyFailure))
                { IsAvailable = Failed, Icon = IconKind.Error }
        ]);
    }

    /// <summary>The test the menu applies to, or null when a class is selected.</summary>
    public TestCase? SelectedTest => (_tree.SelectedItem as TestNode)?.Test;

    /// <summary>What the selected test reported, when it has run.</summary>
    public TestResult? SelectedResult => (_tree.SelectedItem as TestNode)?.Result;

    internal IReadOnlyList<TestNode> Roots =>
        (_tree.ItemsSource as IEnumerable<TestNode>)?.ToList() ?? [];

    internal string Summary => _summary.Text ?? "";

    internal string Details => _details.Text ?? "";

    /// <summary>The tests the user has selected, or all of them when none is.</summary>
    public IReadOnlyList<TestCase> SelectedTests
    {
        get
        {
            if (_tree.SelectedItem is not TestNode node) return _tests;

            return [.. node.Leaves.Select(l => l.Test!).Where(t => t is not null)];
        }
    }

    /// <summary>The tests that failed the last time they ran.</summary>
    public IReadOnlyList<TestCase> FailedTests =>
        [.. _byName.Values
            .Where(n => n.Test is not null && n.Outcome == TestOutcome.Failed)
            .Select(n => n.Test!)];

    /// <summary>
    /// Builds the tree from the discovered tests.
    ///
    /// Grouped by project, then by the class part of each name.
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
                    byClass.Key.Length == 0 ? "(no class)" : ShortClassName(byClass.Key));

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

        _tree.ItemsSource = roots;

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

        Refresh();
        _summary.Text = "Running…";
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

        Refresh();
        UpdateSummary();

        _runFailed.IsEnabled = FailedTests.Count > 0;
    }

    private void UpdateSummary()
    {
        if (_tests.Count == 0)
        {
            _summary.Text = "No tests discovered.";
            return;
        }

        var counted = _byName.Values.ToList();

        var passed = counted.Count(n => n.Outcome == TestOutcome.Passed);
        var failed = counted.Count(n => n.Outcome == TestOutcome.Failed);
        var skipped = counted.Count(n => n.Outcome == TestOutcome.Skipped);

        _summary.Text = passed + failed + skipped == 0
            ? $"{_tests.Count} tests."
            : $"{passed} passed, {failed} failed, {skipped} skipped, of {_tests.Count}.";
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
    /// Redraws the tree.
    ///
    /// The nodes are the same objects; reassigning the source is what makes
    /// the view read their new state.
    /// </summary>
    private void Refresh()
    {
        var roots = Roots;

        _tree.ItemsSource = null;
        _tree.ItemsSource = roots;
    }

    private static Control BuildRow(TestNode? node)
    {
        if (node is null) return new TextBlock();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
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
            FontSize = 12,
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
}
