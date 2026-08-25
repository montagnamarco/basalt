using Avalonia;
using Avalonia.Controls.Primitives;
using Basalt.Core.Localization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell.Controls;

/// <summary>File tree of the currently open solution.</summary>
public sealed class SolutionExplorerPanel : UserControl
{
    private readonly TreeView _albero;

    public SolutionExplorerPanel(MainWindowViewModel viewModel)
    {
        _albero = new TreeView
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding("Explorer.Roots"),
            [!TreeView.SelectedItemProperty] = new Binding("Explorer.SelectedNode")
            {
                Mode = BindingMode.TwoWay
            },
            ItemTemplate = new FuncTreeDataTemplate<SolutionTreeNode>(
                (node, _) => BuildTreeRow(node),
                node => node?.Children ?? [])
        };

        _albero.DoubleTapped += (_, _) =>
        {
            if (_albero.SelectedItem is SolutionTreeNode node &&
                node.IsOpenable)
            {
                FileOpened?.Invoke(this, node);
            }
        };

        // Alt+Enter on the selection, as everywhere else.
        _albero.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter
                || !e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))
                return;

            if (SelectedNode is { Kind: NodeKind.Project })
            {
                CommandChosen?.Invoke(this, (ExplorerCommand.Properties, SelectedNode));
                e.Handled = true;
            }
        };

        DataContext = viewModel;
        BuildContextMenu();

        Tools = BuildTools(viewModel);

        Content = _albero;
    }

    /// <summary>
    /// The buttons the panel offers, for the header to place.
    ///
    /// Handed over rather than drawn here: the header owns the row that the
    /// icon and the name sit in, and the buttons belong on the same line.
    /// </summary>
    public Control Tools { get; }

    private Control BuildTools(MainWindowViewModel viewModel)
    {
        var showAll = new ToggleButton
        {
            Content = new IconView { Kind = IconKind.Explorer, IconSize = 13 },
            Padding = new Thickness(4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            [ToolTip.TipProperty] = Localizer.Get(StringKeys.ExplorerShowAllFiles),
            [!ToggleButton.IsCheckedProperty] =
                new Binding("Explorer.ShowAllFiles") { Mode = BindingMode.TwoWay }
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children =
            {
                showAll,
                Tool(IconKind.Layout, StringKeys.ExplorerCollapseAll, CollapseAll),
                Tool(IconKind.GoToDefinition, StringKeys.ExplorerSyncWithEditor,
                    () => SyncRequested?.Invoke(this, EventArgs.Empty)),
                Tool(IconKind.Refresh, StringKeys.ExplorerRefresh,
                    () => viewModel.Explorer.Refresh())
            }
        };
    }

    private static Button Tool(IconKind icon, string tip, Action click)
    {
        var button = new Button
        {
            Content = new IconView { Kind = icon, IconSize = 13 },
            Padding = new Thickness(4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            [ToolTip.TipProperty] = Localizer.Get(tip)
        };

        button.Click += (_, _) => click();

        return button;
    }

    /// <summary>Closes every folder, leaving the roots open.</summary>
    internal void CollapseAll()
    {
        if (DataContext is not MainWindowViewModel viewModel) return;

        foreach (var root in viewModel.Explorer.Roots)
            foreach (var node in root.Children)
                Close(node);
    }

    private static void Close(SolutionTreeNode node)
    {
        node.IsExpanded = false;

        foreach (var child in node.Children) Close(child);
    }

    /// <summary>Asked when the tree should follow the document being edited.</summary>
    public event EventHandler? SyncRequested;

    /// <summary>Opens the tree down to a file and selects it.</summary>
    public void Reveal(string filePath)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;

        foreach (var root in viewModel.Explorer.Roots)
            if (Reveal(root, filePath)) return;
    }

    private static bool Reveal(SolutionTreeNode node, string filePath)
    {
        if (string.Equals(node.Path, filePath, StringComparison.Ordinal))
        {
            node.IsSelected = true;
            return true;
        }

        foreach (var child in node.Children)
        {
            if (!Reveal(child, filePath)) continue;

            // Opened on the way back out, so only the branch holding the file
            // is left open rather than the whole tree.
            node.IsExpanded = true;
            return true;
        }

        return false;
    }

    public event EventHandler<SolutionTreeNode>? FileOpened;

    /// <summary>What the explorer's context menu can ask for.</summary>
    public enum ExplorerCommand
    {
        Open, OpenInDesigner, Rename, Delete,
        NewFile, NewFolder, RevealInFinder, CopyPath, CopyRelativePath, Refresh,
        Properties
    }

    /// <summary>Raised when the user picks something from the menu.</summary>
    public event EventHandler<(ExplorerCommand Command, SolutionTreeNode? Node)>? CommandChosen;

    /// <summary>The node the menu applies to: whatever is selected.</summary>
    internal SolutionTreeNode? SelectedNode => _albero.SelectedItem as SolutionTreeNode;

    internal ContextMenu? Menu => _albero.ContextMenu;

    /// <summary>The tree, so a test can double-click it for real.</summary>
    internal TreeView TreeForTests => _albero;

    /// <summary>
    /// Builds the menu.
    ///
    /// The entries that need a file are unavailable on a folder, and the ones
    /// that need any selection are unavailable with none — greyed rather than
    /// hidden, so the menu keeps its shape.
    /// </summary>
    private void BuildContextMenu()
    {
        bool HasNode() => SelectedNode is not null;
        bool HasFile() => SelectedNode is { IsOpenable: true };
        bool IsDesignable() => SelectedNode is { IsDesignable: true };
        bool IsProject() => SelectedNode is { Kind: NodeKind.Project };

        void Choose(ExplorerCommand command) =>
            CommandChosen?.Invoke(this, (command, SelectedNode));

        _albero.ContextMenu = ContextMenus.Build(
        [
            new("Open", () => Choose(ExplorerCommand.Open))
                { IsAvailable = HasFile, Icon = IconKind.Open },
            new("Open in Designer", () => Choose(ExplorerCommand.OpenInDesigner))
                { IsAvailable = IsDesignable, Icon = IconKind.DesignerFile },
            MenuAction.Separator,
            new("New File…", () => Choose(ExplorerCommand.NewFile)) { Icon = IconKind.New },
            new("New Folder…", () => Choose(ExplorerCommand.NewFolder))
                { Icon = IconKind.Folder },
            MenuAction.Separator,
            new("Rename…", () => Choose(ExplorerCommand.Rename))
                { IsAvailable = HasNode, Icon = IconKind.Replace },
            new("Delete", () => Choose(ExplorerCommand.Delete))
                { IsAvailable = HasNode, Icon = IconKind.Exit },
            MenuAction.Separator,
            new(RevealLabel, () => Choose(ExplorerCommand.RevealInFinder))
                { IsAvailable = HasNode, Icon = IconKind.FolderOpen },
            new("Copy Path", () => Choose(ExplorerCommand.CopyPath))
                { IsAvailable = HasNode, Icon = IconKind.Copy },
            new("Copy Relative Path", () => Choose(ExplorerCommand.CopyRelativePath))
                { IsAvailable = HasNode, Icon = IconKind.Copy },
            MenuAction.Separator,
            new("Refresh", () => Choose(ExplorerCommand.Refresh)) { Icon = IconKind.Refresh },
            MenuAction.Separator,

            // On the project node, which is where anyone looks for them
            // before thinking of the Project menu.
            new("Properties…", () => Choose(ExplorerCommand.Properties))
                { IsAvailable = IsProject, Icon = IconKind.Properties }
        ]);
    }

    /// <summary>What the file manager is called on this platform.</summary>
    private static string RevealLabel =>
        OperatingSystem.IsMacOS() ? "Reveal in Finder"
        : OperatingSystem.IsWindows() ? "Show in Explorer"
        : "Show in File Manager";

    /// <summary>
    /// The icon a node calls for.
    ///
    /// Containers are named by their kind; a file is named by its extension,
    /// which is what tells a stylesheet from a script.
    /// </summary>
    private static IconKind IconFor(SolutionTreeNode? node) => node?.Kind switch
    {
        null => IconKind.None,
        NodeKind.Solution => IconKind.Solution,
        NodeKind.Project => IconKind.Project,
        NodeKind.Folder => IconKind.Folder,
        _ => IdeIcons.ForFile(node.Path)
    };

    private static Control BuildTreeRow(SolutionTreeNode? node)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        // Drawn icons rather than emoji: an emoji renders differently on every
        // platform, and this IDE runs on three.
        row.Children.Add(new IconView
        {
            Kind = IconFor(node),
            IconSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = node?.Name,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }
}
