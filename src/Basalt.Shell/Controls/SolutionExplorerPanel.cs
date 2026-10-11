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
    private readonly TreeView _tree;

    public SolutionExplorerPanel(MainWindowViewModel viewModel)
    {
        _tree = new TreeView
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

        _tree.DoubleTapped += (_, _) =>
        {
            if (_tree.SelectedItem is SolutionTreeNode { IsOpenable: true } node)
            {
                FileOpened?.Invoke(this, node);
            }
        };

        _tree.SelectionChanged += (_, _) =>
        {
            // Selecting a form is the same action as opening its designer.
            // Other files still require a double-click. Not when the tree
            // made the selection itself — Refresh, Show All Files, Reveal —
            // since that is restoring state, not the user asking to open it.
            if (!viewModel.IsBusy && !viewModel.Explorer.IsProgrammaticSelection
                && _tree.SelectedItem is SolutionTreeNode { IsForm: true } form)
                FileOpened?.Invoke(this, form);
        };

        // Enter opens the selected file, the same as a double-click;
        // Alt+Enter opens the selected project's Properties, as everywhere
        // else in the IDE.
        _tree.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter) return;

            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))
            {
                if (SelectedNode is { Kind: NodeKind.Project })
                {
                    CommandChosen?.Invoke(this, (ExplorerCommand.Properties, SelectedNode));
                    e.Handled = true;
                }

                return;
            }

            if (e.KeyModifiers != Avalonia.Input.KeyModifiers.None) return;

            if (SelectedNode is { IsOpenable: true } node)
            {
                FileOpened?.Invoke(this, node);
                e.Handled = true;
            }
        };

        DataContext = viewModel;
        BuildContextMenu();

        Tools = BuildTools(viewModel);

        Content = _tree;
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

    /// <summary>
    /// The folders that are open, so they can be opened again next time.
    /// </summary>
    /// <remarks>
    /// By path rather than by position: a project reopened after a file was
    /// added or removed has a differently shaped tree, and an index into it
    /// would unfold whatever now sits where the folder used to.
    /// </remarks>
    public IReadOnlyList<string> ExpandedFolders()
    {
        if (DataContext is not MainWindowViewModel viewModel) return [];

        var open = new List<string>();

        void Walk(SolutionTreeNode node)
        {
            if (node.IsExpanded && node.Path is { Length: > 0 }) open.Add(node.Path);

            foreach (var child in node.Children) Walk(child);
        }

        foreach (var root in viewModel.Explorer.Roots) Walk(root);

        return open;
    }

    /// <summary>
    /// Opens the folders that were open last time.
    /// </summary>
    /// <remarks>
    /// A folder that has gone since is simply not found, which is the right
    /// answer: it was deleted or renamed on purpose, and there is nothing to
    /// tell the user about it.
    /// </remarks>
    public void ExpandFolders(IReadOnlyList<string> folders)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        if (folders.Count == 0) return;

        var wanted = folders.ToHashSet(StringComparer.Ordinal);

        void Walk(SolutionTreeNode node)
        {
            if (node.Path is { Length: > 0 } path && wanted.Contains(path))
                node.IsExpanded = true;

            foreach (var child in node.Children) Walk(child);
        }

        foreach (var root in viewModel.Explorer.Roots) Walk(root);
    }

    /// <summary>Asked when the tree should follow the document being edited.</summary>
    public event EventHandler? SyncRequested;

    /// <summary>Opens the tree down to a file and selects it.</summary>
    public void Reveal(string filePath)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        viewModel.Explorer.SelectPathQuietly(filePath);
    }

    public event EventHandler<SolutionTreeNode>? FileOpened;

    /// <summary>What the explorer's context menu can ask for.</summary>
    public enum ExplorerCommand
    {
        Open, OpenInDesigner, ViewCode, Rename, Delete,
        NewFile, NewFolder, RevealInFinder, CopyPath, CopyRelativePath, Refresh,
        Properties, References, AddNewItem
    }

    /// <summary>Raised when the user picks something from the menu.</summary>
    public event EventHandler<(ExplorerCommand Command, SolutionTreeNode? Node)>? CommandChosen;

    /// <summary>The node the menu applies to: whatever is selected.</summary>
    internal SolutionTreeNode? SelectedNode => _tree.SelectedItem as SolutionTreeNode;

    internal ContextMenu? Menu => _tree.ContextMenu;

    /// <summary>The tree, so a test can double-click it for real.</summary>
    internal TreeView TreeForTests => _tree;

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
        bool IsForm() => SelectedNode is { IsForm: true };
        bool IsProject() => SelectedNode is { Kind: NodeKind.Project };

        // Off for the solution and for a project, whose own path is the
        // .sln/.vbproj the session is open from: see
        // SolutionTreeNode.CanRenameOrDelete. Off too while a debug session
        // is running — the same refusal MainWindow.ExplorerCommands makes
        // when asked directly, greyed out here first so the menu reflects
        // it rather than letting the click do nothing.
        bool CanRenameOrDelete() => SelectedNode is { CanRenameOrDelete: true }
            && !((DataContext as MainWindowViewModel)?.IsDebuggingProbe?.Invoke() ?? false);

        void Choose(ExplorerCommand command) =>
            CommandChosen?.Invoke(this, (command, SelectedNode));

        _tree.ContextMenu = ContextMenus.Build(
        [
            new(Localizer.Get(StringKeys.SolutionExplorerOpen), () => Choose(ExplorerCommand.Open))
                { IsAvailable = HasFile, Icon = IconKind.Open },
            new(Localizer.Get(StringKeys.SolutionExplorerOpenInDesigner), () => Choose(ExplorerCommand.OpenInDesigner))
                { IsAvailable = IsDesignable, Icon = IconKind.DesignerFile },
            new(Localizer.Get(StringKeys.WinFormsDesignerViewCode), () => Choose(ExplorerCommand.ViewCode))
                { IsAvailable = IsForm, Icon = IconKind.VisualBasicFile },
            MenuAction.Separator,
            // Not the New icon: that one is New File's and New Solution's, and
            // the menu tests keep an icon from standing for two unrelated commands.
            new(Localizer.Get(StringKeys.ExplorerAddNewItem), () => Choose(ExplorerCommand.AddNewItem))
                { Icon = IconKind.TextFile },
            new(Localizer.Get(StringKeys.SolutionExplorerNewFile), () => Choose(ExplorerCommand.NewFile)) { Icon = IconKind.New },
            new(Localizer.Get(StringKeys.SolutionExplorerNewFolder), () => Choose(ExplorerCommand.NewFolder))
                { Icon = IconKind.Folder },
            MenuAction.Separator,
            new(Localizer.Get(StringKeys.SolutionExplorerRename), () => Choose(ExplorerCommand.Rename))
                { IsAvailable = CanRenameOrDelete, Icon = IconKind.Replace },
            new(Localizer.Get(StringKeys.SolutionExplorerDelete), () => Choose(ExplorerCommand.Delete))
                { IsAvailable = CanRenameOrDelete, Icon = IconKind.Exit },
            MenuAction.Separator,
            new(RevealLabel, () => Choose(ExplorerCommand.RevealInFinder))
                { IsAvailable = HasNode, Icon = IconKind.FolderOpen },
            new(Localizer.Get(StringKeys.SolutionExplorerCopyPath), () => Choose(ExplorerCommand.CopyPath))
                { IsAvailable = HasNode, Icon = IconKind.Copy },
            new(Localizer.Get(StringKeys.SolutionExplorerCopyRelativePath), () => Choose(ExplorerCommand.CopyRelativePath))
                { IsAvailable = HasNode, Icon = IconKind.Copy },
            MenuAction.Separator,
            new(Localizer.Get(StringKeys.SolutionExplorerRefresh), () => Choose(ExplorerCommand.Refresh)) { Icon = IconKind.Refresh },
            MenuAction.Separator,

            // On the project node, which is where anyone looks for them
            // before thinking of the Project menu.
            new(Localizer.Get(StringKeys.ExplorerAddReference), () => Choose(ExplorerCommand.References))
                { IsAvailable = IsProject, Icon = IconKind.References },
            new(Localizer.Get(StringKeys.SolutionExplorerProperties), () => Choose(ExplorerCommand.Properties))
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
            FontSize = PanelMetrics.FontSize,
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }
}
