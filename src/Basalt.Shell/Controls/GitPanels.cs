using Basalt.Core.Localization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Basalt.Core.Services;
using Basalt.Workspace.SourceControl;

namespace Basalt.Shell.Controls;

/// <summary>A commit the user asked for.</summary>
public sealed record CommitRequest(string Message, bool Amend);

/// <summary>A changed file as the list shows it.</summary>
/// <remarks>
/// The path is split in two: the name is what someone is looking for and the
/// folder is only context, and shown as one string the name sits at the right
/// where it is the first thing an ellipsis eats. Twenty rows reading
/// "src/Basalt.Shell/Controls/Too…" tell you nothing at all.
/// </remarks>
public sealed record ChangeRow(FileChange Change)
{
    public string Display => $"{Marker} {Change.Path}";

    /// <summary>The file's own name.</summary>
    public string Name => System.IO.Path.GetFileName(Change.Path);

    /// <summary>The folder it sits in, or empty at the repository root.</summary>
    public string Folder
    {
        get
        {
            var folder = System.IO.Path.GetDirectoryName(Change.Path) ?? "";

            // Forward slashes whatever the platform: this is a path as git
            // reports it, not one to open a file with, and a backslash in the
            // middle of the panel on Windows reads as a different repository
            // from the same one on a Mac.
            return folder.Replace('\\', '/');
        }
    }

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

    /// <summary>
    /// What the letter is drawn in.
    /// </summary>
    /// <remarks>
    /// The same colours every git client uses, which is the point: someone
    /// scanning the list is looking for the red one, and reads the letter only
    /// once they have found the row.
    /// </remarks>
    public Color Colour => Change.Kind switch
    {
        FileChangeKind.Added => Color.FromRgb(0x2E, 0xA0, 0x43),
        FileChangeKind.Modified => Color.FromRgb(0xBF, 0x87, 0x00),
        FileChangeKind.Deleted => Color.FromRgb(0xCF, 0x22, 0x2E),
        FileChangeKind.Renamed => Color.FromRgb(0x37, 0x94, 0xFF),
        FileChangeKind.Conflicted => Color.FromRgb(0xCF, 0x22, 0x2E),
        _ => Color.FromRgb(0x8A, 0x8A, 0x8A)
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
    private readonly TextBlock _upstream;
    private readonly Button _pull;
    private readonly Button _push;
    private readonly TextBlock _stagedCount;
    private readonly TextBlock _unstagedCount;
    private readonly Button _stageAll;
    private readonly Button _unstageAll;
    private readonly TextBlock _summaryLength;

    /// <summary>
    /// Where a commit subject stops being a subject.
    /// </summary>
    /// <remarks>
    /// Fifty characters is the convention git's own tooling assumes, and a
    /// count that turns amber as it is approached is how someone learns it
    /// without being refused a longer one — the rule is a convention, and
    /// there are good commits that break it.
    /// </remarks>
    private const int SubjectLimit = 50;

    public GitChangesPanel()
    {
        _branch = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // The counts against the remote, which is the question the header is
        // actually there to answer: whether there is anything to send and
        // anything waiting to come down.
        _upstream = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center
        };

        _pull = SmallButton(IconKind.Pull, "Pull");
        _push = SmallButton(IconKind.Push, "Push");

        _pull.Click += (_, _) => RemoteRequested?.Invoke(this, RemoteCommand.Pull);
        _push.Click += (_, _) => RemoteRequested?.Invoke(this, RemoteCommand.Push);

        _message = new TextBox
        {
            PlaceholderText = "Commit message",
            AcceptsReturn = true,
            MinHeight = 60,
            MaxHeight = 140,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(Spacing.Normal, 0, Spacing.Normal, 0)
        };

        _message.TextChanged += (_, _) => UpdateCommitState();

        // Ctrl+Enter commits. Reaching for the button after typing a message
        // is the one gesture in this panel done dozens of times a day.
        _message.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter
                && e.KeyModifiers.HasFlag(
                    OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control))
            {
                RequestCommit();
                e.Handled = true;
            }
        };

        _summaryLength = new TextBlock
        {
            FontSize = 10,
            Opacity = 0.55,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        _amend = new CheckBox
        {
            Content = "Amend the last commit",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Ticking amend loads the previous message, since the usual reason to
        // amend is to correct that message rather than to replace it.
        _amend.IsCheckedChanged += (_, _) =>
        {
            if (_amend.IsChecked == true) AmendRequested?.Invoke(this, EventArgs.Empty);

            UpdateCommitState();
        };

        _commit = new Button
        {
            Content = "Commit",
            Margin = new Thickness(Spacing.Normal, 0, Spacing.Normal, Spacing.Normal),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
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

        _stagedCount = CountLabel();
        _unstagedCount = CountLabel();

        _stageAll = LinkButton("Stage all");
        _unstageAll = LinkButton("Unstage all");

        _stageAll.Click += (_, _) => StageAllRequested?.Invoke(this, EventArgs.Empty);
        _unstageAll.Click += (_, _) => UnstageAllRequested?.Invoke(this, EventArgs.Empty);

        // Auto rather than a fixed share each. Two staged files and twenty
        // unstaged given half the panel apiece leaves the top list mostly
        // empty and scrolls the one being read; sized to their contents the
        // lists take what they need, and the whole panel scrolls once there
        // is more than fits.
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto")
        };

        AddRow(layout, BranchBar(), 0);
        AddRow(layout, _message, 1);
        AddRow(layout, MessageFooter(), 2);
        AddRow(layout, _commit, 3);
        AddRow(layout, new Separator { Opacity = 0.25, Margin = new Thickness(0, 0, 0, 2) }, 4);
        _stagedHeader = SectionHeader("Staged", _stagedCount, _unstageAll);
        _unstagedHeader = SectionHeader("Changes", _unstagedCount, _stageAll);

        AddRow(layout, _stagedHeader, 5);
        AddRow(layout, _staged, 6);
        AddRow(layout, _unstagedHeader, 7);
        AddRow(layout, _unstaged, 8);
        AddRow(layout, _nothing, 9);

        // Only the lists scroll, and only once they outgrow the panel: the
        // message box and the Commit button stay put, since a commit button
        // that scrolls out of view is one people stop finding.
        Content = new ScrollViewer
        {
            Content = layout,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        UpdateCommitState();
    }

    /// <summary>The two section headings, hidden together with their lists.</summary>
    private readonly Control _stagedHeader;
    private readonly Control _unstagedHeader;

    /// <summary>Says there is nothing to commit, when there is nothing.</summary>
    private readonly TextBlock _nothing = new()
    {
        FontSize = 12,
        Opacity = 0.6,
        Margin = new Thickness(Spacing.Normal, Spacing.Normal),
        IsVisible = false,
    };

    /// <summary>Whether the panel is saying there is nothing to commit. For tests.</summary>
    internal bool IsShowingEmptyState => _nothing.IsVisible;

    /// <summary>Raised with the message and whether it should amend.</summary>
    public event EventHandler<CommitRequest>? CommitRequested;

    /// <summary>Raised when the user ticks amend, to load the previous message.</summary>
    public event EventHandler? AmendRequested;
    public event EventHandler<FileChange>? StageRequested;
    public event EventHandler<FileChange>? UnstageRequested;

    /// <summary>Raised to stage or unstage everything at once.</summary>
    /// <remarks>
    /// A commit that takes in the whole working tree is the common case, and
    /// doing it by double-tapping twenty rows is twenty chances to miss one.
    /// </remarks>
    public event EventHandler? StageAllRequested;
    public event EventHandler? UnstageAllRequested;

    /// <summary>What the header's two buttons ask for.</summary>
    public enum RemoteCommand { Pull, Push }

    /// <summary>Raised when the user asks to exchange commits with the remote.</summary>
    public event EventHandler<RemoteCommand>? RemoteRequested;

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
                { IsAvailable = HasFile, Icon = IconKind.Diff },
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
        set
        {
            _message.Text = value;
            UpdateCommitState();
        }
    }

    /// <summary>Whether Commit can be pressed at all.</summary>
    internal bool CanCommit => _commit.IsEnabled;

    /// <summary>What the header says about the remote.</summary>
    internal string UpstreamText => _upstream.Text ?? "";

    internal bool CanPush => _push.IsEnabled;
    internal bool CanPull => _pull.IsEnabled;

    private static void AddRow(Grid grid, Control child, int row)
    {
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    /// <summary>The branch, how it stands against the remote, and the two buttons.</summary>
    private Control BranchBar()
    {
        var name = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new IconView { Kind = IconKind.Branch, IconSize = 13 },
                _branch,
                _upstream
            }
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _pull, _push }
        };

        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(Spacing.Normal, Spacing.Normal, Spacing.Normal, Spacing.Normal),
            Children = { name, buttons }
        };

        Grid.SetColumn(buttons, 1);

        return bar;
    }

    /// <summary>Amend on the left, the subject's length on the right.</summary>
    private Control MessageFooter()
    {
        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(Spacing.Normal, 2, Spacing.Normal, Spacing.Tight),
            Children = { _amend, _summaryLength }
        };

        Grid.SetColumn(_summaryLength, 1);

        return footer;
    }

    /// <summary>A heading with its count and the action that applies to it.</summary>
    private static Control SectionHeader(string text, TextBlock count, Button action)
    {
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = text,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Opacity = 0.75,
                    VerticalAlignment = VerticalAlignment.Center
                },
                count
            }
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(Spacing.Normal, Spacing.Normal, Spacing.Tight, 2),
            Children = { left, action }
        };

        Grid.SetColumn(action, 1);

        return header;
    }

    /// <summary>How many files, in a pill beside the heading.</summary>
    private static TextBlock CountLabel() => new()
    {
        FontSize = 10,
        Opacity = 0.6,
        VerticalAlignment = VerticalAlignment.Center,
        FontFamily = Monospace
    };

    private static readonly FontFamily Monospace =
        new("Menlo,Consolas,DejaVu Sans Mono,monospace");

    /// <summary>A button that reads as a link rather than as a control.</summary>
    /// <remarks>
    /// Stage all sits inside a heading, and a raised button there competes
    /// with Commit for the eye — which is the one button in the panel that
    /// should be obvious.
    /// </remarks>
    private static Button LinkButton(string text) => new()
    {
        Content = new TextBlock { Text = text, FontSize = 10 },
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(Spacing.Tight, 0),
        Opacity = 0.75,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>An icon button for the header.</summary>
    private static Button SmallButton(IconKind kind, string tip)
    {
        var button = new Button
        {
            Content = new IconView { Kind = kind, IconSize = 14 },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(Spacing.Tight, 2),
            VerticalAlignment = VerticalAlignment.Center
        };

        ToolTip.SetTip(button, tip);

        return button;
    }

    private ListBox BuildList()
    {
        var list = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<ChangeRow>(
                (row, _) => Row(row), supportsRecycling: false)
        };

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ChangeRow row) DiffRequested?.Invoke(this, row.Change);
        };

        return list;
    }

    /// <summary>One changed file: its letter, its name, then its folder.</summary>
    private static Control Row(ChangeRow? row)
    {
        if (row is null) return new TextBlock();

        var marker = new TextBlock
        {
            Text = row.Marker,
            FontFamily = Monospace,
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(row.Colour),
            Width = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        var name = new TextBlock
        {
            Text = row.Name,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        // The folder is dimmed and given the room that is left. When the panel
        // is narrow it is the folder that disappears, and the name — the thing
        // being looked for — stays whole.
        var folder = new TextBlock
        {
            Text = row.Folder,
            FontSize = 10,
            Opacity = 0.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            Children = { marker, name, folder }
        };

        Grid.SetColumn(name, 1);
        Grid.SetColumn(folder, 2);

        folder.Margin = new Thickness(Spacing.Normal, 0, 0, 0);

        ToolTip.SetTip(layout, row.Change.Path);

        return layout;
    }

    public void Show(string? branch, IReadOnlyList<FileChange> changes) =>
        Show(branch, changes, upstream: null);

    /// <summary>
    /// What the working tree looks like, and how the branch stands.
    /// </summary>
    /// <param name="upstream">
    /// Null when it has not been read: the header then says only the branch
    /// name rather than claiming the branch is in step with a remote nobody
    /// has asked about.
    /// </param>
    public void Show(
        string? branch, IReadOnlyList<FileChange> changes, UpstreamState? upstream)
    {
        _branch.Text = branch ?? "No branch";

        var staged = changes.Where(c => c.Staged).Select(c => new ChangeRow(c)).ToList();
        var unstaged = changes.Where(c => !c.Staged).Select(c => new ChangeRow(c)).ToList();

        _staged.ItemsSource = staged;
        _unstaged.ItemsSource = unstaged;

        _stagedCount.Text = staged.Count == 0 ? "" : staged.Count.ToString();
        _unstagedCount.Text = unstaged.Count == 0 ? "" : unstaged.Count.ToString();

        _unstageAll.IsVisible = staged.Count > 0;
        _stageAll.IsVisible = unstaged.Count > 0;

        // A clean tree showed two bare headings and nothing else, which reads
        // as a panel that failed to load rather than as "there is nothing to
        // commit". Said in words, and only when there is genuinely nothing:
        // a section that is empty because everything is staged says so
        // through the other section being full.
        var nothingAtAll = staged.Count == 0 && unstaged.Count == 0;

        _nothing.IsVisible = nothingAtAll;
        _nothing.Text = Localizer.Get(StringKeys.GitNothingToCommit);

        // The headings go with them. Leaving "Staged" and "Changes" above the
        // message keeps exactly the emptiness the message is there to
        // replace, and says it twice.
        _stagedHeader.IsVisible = !nothingAtAll;
        _unstagedHeader.IsVisible = !nothingAtAll;

        ShowUpstream(upstream);
        UpdateCommitState();
    }

    /// <summary>Says what there is to send and to receive.</summary>
    private void ShowUpstream(UpstreamState? upstream)
    {
        if (upstream is null)
        {
            _upstream.Text = "";
            _pull.IsEnabled = true;
            _push.IsEnabled = true;
            return;
        }

        if (upstream.IsUntracked)
        {
            // Publishing a branch is a push, so the button stays live; there
            // is nothing to pull from a remote that has never heard of it.
            _upstream.Text = "not published";
            _pull.IsEnabled = false;
            _push.IsEnabled = true;
            return;
        }

        _upstream.Text = upstream.IsInStep
            ? "up to date"
            : string.Join(
                "  ",
                new[]
                {
                    upstream.Ahead > 0 ? $"↑{upstream.Ahead}" : null,
                    upstream.Behind > 0 ? $"↓{upstream.Behind}" : null
                }.Where(part => part is not null));

        // Greyed rather than hidden. A button that comes and goes moves the
        // one beside it, and the second press lands on the wrong one.
        _pull.IsEnabled = upstream.Behind > 0;
        _push.IsEnabled = upstream.Ahead > 0;
    }

    /// <summary>
    /// Whether Commit can be pressed, and what the length counter says.
    /// </summary>
    /// <remarks>
    /// A commit with nothing staged fails at git and prints an error, and one
    /// with an empty message opens an editor that is not there. Refusing both
    /// in the panel says so before the attempt.
    ///
    /// Amending is the exception: correcting the previous message is a commit
    /// with nothing staged, and it is the reason amend exists.
    /// </remarks>
    private void UpdateCommitState()
    {
        var message = (_message.Text ?? "").Trim();
        var amending = _amend.IsChecked == true;

        _commit.IsEnabled = message.Length > 0 && (amending || Staged.Count > 0);

        _commit.Content = amending ? "Amend" : "Commit";

        var subject = (_message.Text ?? "").Replace("\r\n", "\n").Split('\n')[0];

        _summaryLength.Text = subject.Length == 0 ? "" : subject.Length.ToString();

        _summaryLength.Foreground = subject.Length > SubjectLimit
            ? new SolidColorBrush(Color.FromRgb(0xBF, 0x87, 0x00))
            : null;

        ToolTip.SetTip(
            _commit,
            _commit.IsEnabled
                ? null
                : message.Length == 0 ? "Write a message first" : "Stage something to commit");
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

        UpdateCommitState();
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

        // Nothing staged and not amending is a commit git refuses; the panel
        // has already greyed the button, and this is the keyboard path.
        if (!IsAmending && Staged.Count == 0) return;

        CommitRequested?.Invoke(this, new CommitRequest(message, IsAmending));
    }

    internal void RequestCommitForTests() => RequestCommit();

    internal void StageForTests(ChangeRow row) => StageRequested?.Invoke(this, row.Change);

    internal void UnstageForTests(ChangeRow row) => UnstageRequested?.Invoke(this, row.Change);

    internal void StageAllForTests() => StageAllRequested?.Invoke(this, EventArgs.Empty);

    internal void PressPullForTests() => _pull.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    internal void PressPushForTests() => _push.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
