using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Core.Services;

namespace Basalt.Shell.Controls;

/// <summary>
/// Where execution currently is, innermost call first.
///
/// Picking a frame is how the user looks at the state of a caller, so the
/// chosen index is what the variables panel reads from.
/// </summary>
public sealed class CallStackPanel : UserControl
{
    private readonly ListBox _list;
    private readonly EmptyState _empty;
    private IReadOnlyList<StackFrame> _frames = [];

    public CallStackPanel()
    {
        _empty = new EmptyState(Localizer.Get(StringKeys.CallStackEmpty), IconKind.Debug);

        _list = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<StackFrame>((frame, _) => new TextBlock
            {
                Text = Describe(frame),
                FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
                FontSize = PanelMetrics.FontSize
            })
        };

        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedIndex >= 0 && _list.SelectedIndex < _frames.Count)
                FrameSelected?.Invoke(this, _list.SelectedIndex);
        };

        Content = _empty;
    }

    /// <summary>Raised when the user looks at a different frame.</summary>
    public event EventHandler<int>? FrameSelected;

    internal IReadOnlyList<StackFrame> Frames => _frames;

    /// <summary>Which frame the user is looking at; -1 when none.</summary>
    public int SelectedFrame => _list.SelectedIndex;

    private static string Describe(StackFrame? frame)
    {
        if (frame is null) return "";

        return frame.FilePath is null
            ? frame.Method
            : $"{frame.Method}  —  {Path.GetFileName(frame.FilePath)}:{frame.Line}";
    }

    public void Show(IReadOnlyList<StackFrame> frames)
    {
        _frames = frames;

        if (frames.Count == 0)
        {
            Content = _empty;
            return;
        }

        _list.ItemsSource = frames;
        Content = _list;

        // Stopping somewhere puts the user in the innermost frame, which is
        // where they were looking when execution paused.
        _list.SelectedIndex = 0;
    }

    public void Clear() => Show([]);

    internal void SelectForTests(int index) => _list.SelectedIndex = index;
}

/// <summary>A variable as the tree shows it, with children fetched on demand.</summary>
public sealed class VariableNode
{
    private readonly Func<VariableNode, Task<IReadOnlyList<VariableNode>>>? _expand;
    private List<VariableNode>? _children;

    public VariableNode(
        VariableValue value,
        int reference = 0,
        Func<VariableNode, Task<IReadOnlyList<VariableNode>>>? expand = null)
    {
        Value = value;
        Reference = reference;
        _expand = expand;
    }

    public VariableValue Value { get; }

    /// <summary>The debugger's handle for this variable's children.</summary>
    public int Reference { get; }

    public string Display => Value.Type.Length == 0
        ? $"{Value.Name} = {Value.Value}"
        : $"{Value.Name} = {Value.Value}   ({Value.Type})";

    /// <summary>
    /// The children, empty until they are loaded.
    ///
    /// An object's fields are not fetched until it is opened: a deep object
    /// graph would otherwise mean fetching the whole heap to show one frame.
    /// </summary>
    public IReadOnlyList<VariableNode> Children => _children ?? [];

    public bool HasChildren => Value.HasChildren;

    public async Task<IReadOnlyList<VariableNode>> LoadChildrenAsync()
    {
        if (_children is not null) return _children;
        if (_expand is null) return [];

        _children = [.. await _expand(this).ConfigureAwait(true)];
        return _children;
    }
}

/// <summary>The variables of the frame the user is looking at.</summary>
public sealed class VariablesPanel : UserControl
{
    private readonly TreeView _tree;
    private readonly EmptyState _empty;
    private IReadOnlyList<VariableNode> _nodes = [];

    public VariablesPanel()
    {
        _empty = new EmptyState(Localizer.Get(StringKeys.VariablesEmpty), IconKind.Properties);

        _tree = new TreeView
        {
            ItemTemplate = new FuncTreeDataTemplate<VariableNode>(
                (node, _) => new TextBlock
                {
                    Text = node?.Display,
                    FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
                    FontSize = PanelMetrics.FontSize
                },
                node => node?.Children ?? [])
        };

        Content = _empty;
    }

    internal IReadOnlyList<VariableNode> Nodes => _nodes;

    public void Show(IReadOnlyList<VariableNode> nodes)
    {
        _nodes = nodes;

        if (nodes.Count == 0)
        {
            Content = _empty;
            return;
        }

        _tree.ItemsSource = nodes;
        Content = _tree;
    }

    public void Clear() => Show([]);
}

/// <summary>Every breakpoint in the solution, with a switch for each.</summary>
public sealed class BreakpointsPanel : UserControl
{
    private readonly ItemsControl _list;
    private readonly EmptyState _empty;
    private readonly List<Breakpoint> _breakpoints = [];

    public BreakpointsPanel()
    {
        _empty = new EmptyState(Localizer.Get(StringKeys.BreakpointsEmpty), IconKind.Breakpoint);

        _list = new ItemsControl
        {
            ItemTemplate = new FuncDataTemplate<Breakpoint>((breakpoint, _) => BuildRow(breakpoint))
        };

        Content = _empty;
    }

    /// <summary>Raised when a breakpoint is switched on or off.</summary>
    public event EventHandler<Breakpoint>? Toggled;

    /// <summary>Raised when the user asks to open where a breakpoint is.</summary>
    public event EventHandler<Breakpoint>? Activated;

    /// <summary>Raised when the user asks to change a breakpoint's condition.</summary>
    public event EventHandler<Breakpoint>? ConditionRequested;

    /// <summary>Raised when the user asks to remove a breakpoint.</summary>
    public event EventHandler<Breakpoint>? RemoveRequested;

    internal IReadOnlyList<Breakpoint> Breakpoints => _breakpoints;

    private Control BuildRow(Breakpoint? breakpoint)
    {
        if (breakpoint is null) return new TextBlock();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Small,
            Margin = new Thickness(Spacing.Small, 2)
        };

        var enabled = new CheckBox { IsChecked = breakpoint.Enabled, VerticalAlignment = VerticalAlignment.Center };
        enabled.IsCheckedChanged += (_, _) => Toggle(breakpoint, enabled.IsChecked == true);

        var label = new TextBlock
        {
            Text = Describe(breakpoint),
            FontSize = PanelMetrics.FontSize,
            VerticalAlignment = VerticalAlignment.Center
        };

        label.DoubleTapped += (_, _) => Activated?.Invoke(this, breakpoint);

        // A condition can be read in the row but not changed there, so the
        // menu is where it is changed: showing one with no way to edit it is
        // the gap this closes.
        var menu = new ContextMenu();

        var edit = new MenuItem
        {
            Header = breakpoint.Condition is { Length: > 0 }
                ? Localizer.Get(StringKeys.BreakpointEditCondition)
                : Localizer.Get(StringKeys.BreakpointAddCondition)
        };

        edit.Click += (_, _) => ConditionRequested?.Invoke(this, breakpoint);

        var goTo = new MenuItem { Header = Localizer.Get(StringKeys.BreakpointGoTo) };
        goTo.Click += (_, _) => Activated?.Invoke(this, breakpoint);

        var remove = new MenuItem { Header = Localizer.Get(StringKeys.BreakpointRemove) };
        remove.Click += (_, _) => RemoveRequested?.Invoke(this, breakpoint);

        menu.ItemsSource = new Control[] { goTo, edit, new Separator(), remove };

        row.ContextMenu = menu;

        row.Children.Add(enabled);
        row.Children.Add(label);

        return row;
    }

    /// <summary>Builds one row, for tests.</summary>
    internal Control BuildRowForTests(Breakpoint breakpoint) => BuildRow(breakpoint);

    /// <summary>Asks for a breakpoint's condition to be edited, for tests.</summary>
    internal void RequestConditionForTests(Breakpoint breakpoint) =>
        ConditionRequested?.Invoke(this, breakpoint);

    /// <summary>Asks for a breakpoint to be removed, for tests.</summary>
    internal void RequestRemoveForTests(Breakpoint breakpoint) =>
        RemoveRequested?.Invoke(this, breakpoint);

    private static string Describe(Breakpoint breakpoint)
    {
        var where = $"{Path.GetFileName(breakpoint.FilePath)}:{breakpoint.Line}";

        return breakpoint.Condition is { Length: > 0 } condition
            ? $"{where}   {string.Format(Localizer.Get(StringKeys.BreakpointWhen), condition)}"
            : where;
    }

    private void Toggle(Breakpoint breakpoint, bool enabled)
    {
        var index = _breakpoints.FindIndex(
            b => b.FilePath == breakpoint.FilePath && b.Line == breakpoint.Line);

        if (index < 0 || _breakpoints[index].Enabled == enabled) return;

        var updated = _breakpoints[index] with { Enabled = enabled };
        _breakpoints[index] = updated;

        Toggled?.Invoke(this, updated);
    }

    public void Show(IReadOnlyList<Breakpoint> breakpoints)
    {
        _breakpoints.Clear();
        _breakpoints.AddRange(breakpoints);

        if (_breakpoints.Count == 0)
        {
            Content = _empty;
            return;
        }

        _list.ItemsSource = _breakpoints.ToList();
        Content = _list;
    }

    public void Clear() => Show([]);

    internal void ToggleForTests(Breakpoint breakpoint, bool enabled) => Toggle(breakpoint, enabled);
}
