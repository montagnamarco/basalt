using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Services;
using Basalt.Workspace.SourceControl;

namespace Basalt.Shell.Controls;

/// <summary>A commit the user asked for.</summary>
public sealed record CommitRequest(string Message, bool Amend);

/// <summary>A changed file as the list shows it.</summary>
public sealed record ChangeRow(FileChange Change)
{
    public string Display => $"{Marker} {Change.Path}";

    /// <summary>The single letter git itself uses for the kind of change.</summary>
    public string Marker => Change.Kind switch
    {
        FileChangeKind.Added => "A",
        FileChangeKind.Modified => "M",
        FileChangeKind.Deleted => "D",
        FileChangeKind.Renamed => "R",
        FileChangeKind.Conflicted => "!",
        _ => "?"
    };
}

/// <summary>
/// What has changed since the last commit, split into staged and not.
///
/// The split matters because it is what the next commit will contain: staging
/// is how the user chooses that, so showing both lists side by side is the
/// point rather than a detail.
/// </summary>
public sealed class GitChangesPanel : UserControl
{
    private readonly ListBox _staged;
    private readonly ListBox _unstaged;
    private readonly TextBox _message;
    private readonly Button _commit;
    private readonly CheckBox _amend;
    private readonly TextBlock _branch;

    public GitChangesPanel()
    {
        _branch = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Margin = new Thickness(8, 6, 8, 4)
        };

        _message = new TextBox
        {
            PlaceholderText = "Commit message",
            AcceptsReturn = true,
            MinHeight = 52,
            Margin = new Thickness(8, 0, 8, 4)
        };

        _amend = new CheckBox
        {
            Content = "Amend the last commit",
            FontSize = 11,
            Margin = new Thickness(8, 0, 8, 2)
        };

        // Ticking amend loads the previous message, since the usual reason to
        // amend is to correct that message rather than to replace it.
        _amend.IsCheckedChanged += (_, _) =>
        {
            if (_amend.IsChecked == true) AmendRequested?.Invoke(this, EventArgs.Empty);
        };

        _commit = new Button
        {
            Content = "Commit",
            Margin = new Thickness(8, 0, 8, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _commit.Click += (_, _) => RequestCommit();

        _staged = BuildList();
        _unstaged = BuildList();

        // Double-tapping moves a file across: the quickest way to say
        // "include this" or "leave this out".
        _staged.DoubleTapped += (_, _) =>
        {
            if (_staged.SelectedItem is ChangeRow row) UnstageRequested?.Invoke(this, row.Change);
        };

        _unstaged.DoubleTapped += (_, _) =>
        {
            if (_unstaged.SelectedItem is ChangeRow row) StageRequested?.Invoke(this, row.Change);
        };

        _staged.ContextMenu = BuildMenuFor(_staged, staged: true);
        _unstaged.ContextMenu = BuildMenuFor(_unstaged, staged: false);

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto,*")
        };

        AddRow(layout, _branch, 0);
        AddRow(layout, _message, 1);
        AddRow(layout, _amend, 2);
        AddRow(layout, _commit, 3);
        AddRow(layout, Header("Staged"), 4);
        AddRow(layout, _staged, 5);
        AddRow(layout, Header("Changes"), 6);
        AddRow(layout, _unstaged, 7);

        Content = layout;
    }

    /// <summary>Raised with the message and whether it should amend.</summary>
    public event EventHandler<CommitRequest>? CommitRequested;

    /// <summary>Raised when the user ticks amend, to load the previous message.</summary>
    public event EventHandler? AmendRequested;
    public event EventHandler<FileChange>? StageRequested;
    public event EventHandler<FileChange>? UnstageRequested;

    /// <summary>Raised when the user asks to see a file's diff.</summary>
    public event EventHandler<FileChange>? DiffRequested;

    /// <summary>What the changes menu can ask for.</summary>
    public enum ChangeCommand { Stage, Unstage, Discard, OpenDiff, OpenFile, CopyPath }

    /// <summary>Raised with what was chosen and which file it applies to.</summary>
    public event EventHandler<(ChangeCommand Command, FileChange File)>? CommandChosen;

    /// <summary>
    /// Builds the menu for one of the two lists.
    ///
    /// Staging is offered on the unstaged list and unstaging on the staged
    /// one: offering both everywhere would leave half the menu inert.
    /// </summary>
    private ContextMenu BuildMenuFor(ListBox list, bool staged)
    {
        bool HasFile() => list.SelectedItem is ChangeRow;

        void Choose(ChangeCommand command)
        {
            if (list.SelectedItem is ChangeRow row)
                CommandChosen?.Invoke(this, (command, row.Change));
        }

        return ContextMenus.Build(
        [
            staged
                ? new MenuAction("Unstage", () => Choose(ChangeCommand.Unstage))
                    { IsAvailable = HasFile }
                : new MenuAction("Stage", () => Choose(ChangeCommand.Stage))
                    { IsAvailable = HasFile },

            MenuAction.Separator,
            new("Open Diff", () => Choose(ChangeCommand.OpenDiff))
                { IsAvailable = HasFile, Icon = IconKind.Branch },
            new("Open File", () => Choose(ChangeCommand.OpenFile))
                { IsAvailable = HasFile, Icon = IconKind.Open },
            MenuAction.Separator,

            // Only on the unstaged list: discarding a staged change would
            // throw away work the user has already chosen to keep.
            new("Discard Changes…", () => Choose(ChangeCommand.Discard))
                { IsAvailable = () => !staged && HasFile(), Icon = IconKind.Warning },

            MenuAction.Separator,
            new("Copy Path", () => Choose(ChangeCommand.CopyPath))
                { IsAvailable = HasFile, Icon = IconKind.Copy }
        ]);
    }

    internal ContextMenu? StagedMenu => _staged.ContextMenu;
    internal ContextMenu? UnstagedMenu => _unstaged.ContextMenu;

    internal IReadOnlyList<ChangeRow> Staged =>
        (_staged.ItemsSource as IEnumerable<ChangeRow>)?.ToList() ?? [];

    internal IReadOnlyList<ChangeRow> Unstaged =>
        (_unstaged.ItemsSource as IEnumerable<ChangeRow>)?.ToList() ?? [];

    internal string CommitMessage
    {
        get => _message.Text ?? "";
        set => _message.Text = value;
    }

    private static void AddRow(Grid grid, Control child, int row)
    {
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Opacity = 0.7,
        Margin = new Thickness(8, 6, 8, 2)
    };

    private ListBox BuildList()
    {
        var list = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<ChangeRow>((row, _) => new TextBlock
            {
                Text = row?.Display,
                FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
                FontSize = 12
            })
        };

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ChangeRow row) DiffRequested?.Invoke(this, row.Change);
        };

        return list;
    }

    public void Show(string? branch, IReadOnlyList<FileChange> changes)
    {
        _branch.Text = branch is null ? "No branch" : $"On {branch}";

        _staged.ItemsSource = changes.Where(c => c.Staged).Select(c => new ChangeRow(c)).ToList();
        _unstaged.ItemsSource = changes.Where(c => !c.Staged).Select(c => new ChangeRow(c)).ToList();
    }

    /// <summary>
    /// Empties the message box, which a finished commit should do.
    ///
    /// Amend is cleared too: leaving it ticked would make the next commit
    /// silently replace the one just made.
    /// </summary>
    public void ClearMessage()
    {
        _message.Text = "";
        _amend.IsChecked = false;
    }

    /// <summary>Whether the next commit should replace the previous one.</summary>
    public bool IsAmending
    {
        get => _amend.IsChecked == true;
        set => _amend.IsChecked = value;
    }

    internal void RequestCommit()
    {
        var message = _message.Text ?? "";
        if (message.Trim().Length == 0) return;

        CommitRequested?.Invoke(this, new CommitRequest(message, IsAmending));
    }

    internal void RequestCommitForTests() => RequestCommit();

    internal void StageForTests(ChangeRow row) => StageRequested?.Invoke(this, row.Change);

    internal void UnstageForTests(ChangeRow row) => UnstageRequested?.Invoke(this, row.Change);
}

/// <summary>
/// The commit history with its branching drawn beside it.
///
/// The lanes come from <see cref="CommitGraph"/>; this control only paints
/// them, so the layout can be tested without a rendered control.
/// </summary>
public sealed class GitHistoryPanel : UserControl
{
    private const double LaneWidth = 14;
    private const double RowHeight = 22;

    private static readonly Color[] LaneColours =
    [
        Color.FromRgb(0x37, 0x94, 0xFF), Color.FromRgb(0x2E, 0xA0, 0x43),
        Color.FromRgb(0xBF, 0x87, 0x00), Color.FromRgb(0xCF, 0x22, 0x2E),
        Color.FromRgb(0x82, 0x50, 0xDF), Color.FromRgb(0x1B, 0x7C, 0x83)
    ];

    private readonly ListBox _list;
    private IReadOnlyList<GraphRow> _rows = [];

    public GitHistoryPanel()
    {
        _list = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<GraphRow>((row, _) => BuildRow(row))
        };

        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is GraphRow row) CommitSelected?.Invoke(this, row.Commit);
        };

        Content = _list;
    }

    public event EventHandler<CommitInfo>? CommitSelected;

    internal IReadOnlyList<GraphRow> Rows => _rows;

    /// <summary>The colour a lane is drawn in, repeating once they run out.</summary>
    internal static Color ColourForLane(int lane) => LaneColours[lane % LaneColours.Length];

    private static Control BuildRow(GraphRow? row)
    {
        if (row is null) return new TextBlock();

        var layout = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Height = RowHeight
        };

        layout.Children.Add(new GraphCell(row) { Width = (row.LaneCount + 1) * LaneWidth });

        var text = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        foreach (var name in row.Commit.Refs)
        {
            text.Children.Add(new Border
            {
                Background = new SolidColorBrush(ColourForLane(row.Lane), 0.18),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = name, FontSize = 10 }
            });
        }

        text.Children.Add(new TextBlock
        {
            Text = row.Commit.Subject,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });

        text.Children.Add(new TextBlock
        {
            Text = $"{row.Commit.Author}   {row.Commit.When:yyyy-MM-dd}",
            FontSize = 11,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        });

        layout.Children.Add(text);

        return layout;
    }

    public void Show(IReadOnlyList<CommitInfo> commits)
    {
        _rows = CommitGraph.Build(commits);
        _list.ItemsSource = _rows;
    }

    public void Clear() => Show([]);

    /// <summary>The dot and the lines of one row of the graph.</summary>
    private sealed class GraphCell : Control
    {
        private readonly GraphRow _row;

        public GraphCell(GraphRow row) => _row = row;

        public override void Render(DrawingContext context)
        {
            var centre = RowHeight / 2;

            foreach (var edge in _row.Edges)
            {
                var pen = new Pen(new SolidColorBrush(ColourForLane(edge.ToLane)), 1.5);

                var from = new Point(X(edge.FromLane), centre);
                var to = new Point(X(edge.ToLane), RowHeight);

                if (edge.FromLane == edge.ToLane)
                {
                    context.DrawLine(pen, from, to);
                    continue;
                }

                // A branch or merge bends rather than cutting the corner, which
                // is what makes the two lanes readable as separate lines.
                var figure = new PathFigure { StartPoint = from, IsClosed = false };

                figure.Segments!.Add(new QuadraticBezierSegment
                {
                    Point1 = new Point(X(edge.FromLane), RowHeight),
                    Point2 = to
                });

                var geometry = new PathGeometry();
                geometry.Figures!.Add(figure);

                context.DrawGeometry(null, pen, geometry);
            }

            var colour = ColourForLane(_row.Lane);

            context.DrawEllipse(
                new SolidColorBrush(colour),
                new Pen(new SolidColorBrush(colour), 1),
                new Point(X(_row.Lane), centre),
                _row.Commit.IsMerge ? 3.5 : 4.5,
                _row.Commit.IsMerge ? 3.5 : 4.5);
        }

        private static double X(int lane) => (lane + 0.5) * LaneWidth;
    }
}
