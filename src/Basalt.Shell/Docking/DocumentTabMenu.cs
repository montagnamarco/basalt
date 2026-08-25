using Avalonia.Controls;
using Basalt.Shell.Controls;
using Dock.Model.Core;

namespace Basalt.Shell.Docking;

/// <summary>What was asked for on a document tab.</summary>
public enum DocumentTabCommand
{
    Close,
    CloseOthers,
    CloseAll,
    CloseToTheRight,
    CopyPath,
    OpenContainingFolder
}

/// <summary>
/// The menu shown on a right click on a document tab.
///
/// The closing entries go through Dock's own factory, which already knows how
/// to close others, to the left and to the right — reimplementing them here
/// would be a second answer to a question the dock has already answered, and
/// the two would disagree the moment a tab is dragged elsewhere.
/// </summary>
public static class DocumentTabMenu
{
    /// <summary>Builds the menu for one open document.</summary>
    public static ContextMenu Build(
        IdeDocument document, EventHandler<(IdeDocument, DocumentTabCommand)> chosen)
    {
        var menu = new ContextMenu();

        MenuItem Item(string header, DocumentTabCommand command)
        {
            var item = new MenuItem { Header = header };

            if (IconFor(command) is var icon && icon != IconKind.None)
                item.Icon = new IconView { Kind = icon, IconSize = 14 };

            item.Click += (_, _) => chosen(menu, (document, command));

            return item;
        }

        menu.ItemsSource = new Control[]
        {
            Item("Close", DocumentTabCommand.Close),
            Item("Close Others", DocumentTabCommand.CloseOthers),
            Item("Close All", DocumentTabCommand.CloseAll),
            Item("Close to the Right", DocumentTabCommand.CloseToTheRight),
            new Separator(),
            Item("Copy Path", DocumentTabCommand.CopyPath),
            Item("Open Containing Folder", DocumentTabCommand.OpenContainingFolder)
        };

        return menu;
    }

    /// <summary>The icon for a tab command.</summary>
    private static IconKind IconFor(DocumentTabCommand command) => command switch
    {
        DocumentTabCommand.Close or DocumentTabCommand.CloseOthers
            or DocumentTabCommand.CloseAll
            or DocumentTabCommand.CloseToTheRight => IconKind.Exit,

        DocumentTabCommand.CopyPath => IconKind.Copy,
        DocumentTabCommand.OpenContainingFolder => IconKind.FolderOpen,

        _ => IconKind.None
    };

    /// <summary>
    /// Carries out one of the closing commands.
    ///
    /// Returns false for the ones that are not about closing, which the
    /// caller handles itself: copying a path is not the dock's business.
    /// </summary>
    public static bool Close(
        IFactory factory, IDockable dockable, DocumentTabCommand command)
    {
        switch (command)
        {
            case DocumentTabCommand.Close:
                factory.CloseDockable(dockable);
                return true;

            case DocumentTabCommand.CloseOthers:
                factory.CloseOtherDockables(dockable);
                return true;

            case DocumentTabCommand.CloseAll:
                factory.CloseAllDockables(dockable);
                return true;

            case DocumentTabCommand.CloseToTheRight:
                factory.CloseRightDockables(dockable);
                return true;

            default:
                return false;
        }
    }
}
