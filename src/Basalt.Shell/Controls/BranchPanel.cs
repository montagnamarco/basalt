using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Core.Services;

namespace Basalt.Shell.Controls;

/// <summary>A branch as the list shows it.</summary>
public sealed record BranchRow(BranchInfo Branch)
{
    public string Display
    {
        get
        {
            var name = Branch.IsCurrent ? $"● {Branch.Name}" : $"   {Branch.Name}";

            if (Branch.Ahead == 0 && Branch.Behind == 0) return name;

            // How far this branch has drifted from the one it tracks, which is
            // what decides whether a pull or a push is needed.
            var drift = string.Join(" ", new[]
            {
                Branch.Ahead > 0 ? $"↑{Branch.Ahead}" : null,
                Branch.Behind > 0 ? $"↓{Branch.Behind}" : null
            }.Where(part => part is not null));

            return $"{name}   {drift}";
        }
    }
}

/// <summary>
/// The branches of the repository, and the commands that act on them.
///
/// Local and remote branches are listed apart: only a local branch can be
/// checked out directly, and mixing them invites the user to try.
/// </summary>
public sealed class BranchPanel : UserControl
{
    private readonly ListBox _local;
    private readonly ListBox _remote;
    private readonly TextBox _newBranch;

    public BranchPanel()
    {
        _newBranch = new TextBox
        {
            PlaceholderText = Localizer.Get(StringKeys.BranchNewName),
            FontSize = PanelMetrics.FontSize,
            Margin = new Thickness(Spacing.Small, Spacing.Small, Spacing.Small, Spacing.Tight)
        };

        var create = new Button
        {
            Content = Localizer.Get(StringKeys.BranchCreate),
            Margin = new Thickness(Spacing.Small, 0, Spacing.Small, Spacing.Small),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        create.Click += (_, _) => RequestCreate();

        _local = BuildList(checkoutOnDoubleTap: true);
        _remote = BuildList(checkoutOnDoubleTap: false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            Margin = new Thickness(Spacing.Small, Spacing.Tight)
        };

        AddButton(buttons, Localizer.Get(StringKeys.BranchMerge), () =>
        {
            if (_local.SelectedItem is BranchRow row) MergeRequested?.Invoke(this, row.Branch);
        });

        AddButton(buttons, Localizer.Get(StringKeys.BranchDelete), () =>
        {
            if (_local.SelectedItem is BranchRow row) DeleteRequested?.Invoke(this, row.Branch);
        });

        AddButton(buttons, Localizer.Get(StringKeys.MenuGitFetch), () => FetchRequested?.Invoke(this, EventArgs.Empty));
        AddButton(buttons, Localizer.Get(StringKeys.MenuGitPull), () => PullRequested?.Invoke(this, EventArgs.Empty));
        AddButton(buttons, Localizer.Get(StringKeys.MenuGitPush), () => PushRequested?.Invoke(this, EventArgs.Empty));

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto,*") };

        AddRow(layout, _newBranch, 0);
        AddRow(layout, create, 1);
        AddRow(layout, buttons, 2);
        AddRow(layout, Header(Localizer.Get(StringKeys.BranchLocal)), 3);
        AddRow(layout, _local, 4);
        AddRow(layout, Header(Localizer.Get(StringKeys.BranchRemote)), 5);
        AddRow(layout, _remote, 6);

        Content = layout;
    }

    public event EventHandler<string>? CreateRequested;
    public event EventHandler<BranchInfo>? CheckoutRequested;
    public event EventHandler<BranchInfo>? MergeRequested;
    public event EventHandler<BranchInfo>? DeleteRequested;
    public event EventHandler? FetchRequested;
    public event EventHandler? PullRequested;
    public event EventHandler? PushRequested;

    internal IReadOnlyList<BranchRow> Local =>
        (_local.ItemsSource as IEnumerable<BranchRow>)?.ToList() ?? [];

    internal IReadOnlyList<BranchRow> Remote =>
        (_remote.ItemsSource as IEnumerable<BranchRow>)?.ToList() ?? [];

    internal string NewBranchName
    {
        get => _newBranch.Text ?? "";
        set => _newBranch.Text = value;
    }

    private static void AddRow(Grid grid, Control child, int row)
    {
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    private static void AddButton(Panel parent, string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => action();
        parent.Children.Add(button);
    }

    private static TextBlock Header(string text) => new TextBlock
    {
        Text = text,
        FontSize = PanelMetrics.SecondaryFontSize,
        Margin = new Thickness(Spacing.Normal, Spacing.Small, Spacing.Normal, 2)
    }.Themed(TextBlock.ForegroundProperty, "ChromeSecondaryForegroundBrush");

    private ListBox BuildList(bool checkoutOnDoubleTap)
    {
        var list = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<BranchRow>((row, _) => new TextBlock
            {
                Text = row?.Display,
                FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
                FontSize = PanelMetrics.FontSize,
                FontWeight = row?.Branch.IsCurrent == true ? FontWeight.SemiBold : FontWeight.Normal
            })
        };

        if (checkoutOnDoubleTap)
        {
            list.DoubleTapped += (_, _) =>
            {
                if (list.SelectedItem is BranchRow row) CheckoutRequested?.Invoke(this, row.Branch);
            };
        }

        return list;
    }

    internal void RequestCreate()
    {
        var name = (_newBranch.Text ?? "").Trim();
        if (name.Length == 0) return;

        CreateRequested?.Invoke(this, name);
        _newBranch.Text = "";
    }

    public void Show(IReadOnlyList<BranchInfo> branches)
    {
        _local.ItemsSource = branches.Where(b => !b.IsRemote).Select(b => new BranchRow(b)).ToList();
        _remote.ItemsSource = branches.Where(b => b.IsRemote).Select(b => new BranchRow(b)).ToList();
    }

    internal void CheckoutForTests(BranchRow row) => CheckoutRequested?.Invoke(this, row.Branch);
    internal void MergeForTests(BranchRow row) => MergeRequested?.Invoke(this, row.Branch);
    internal void DeleteForTests(BranchRow row) => DeleteRequested?.Invoke(this, row.Branch);
}
