using Avalonia.Controls.Templates;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace Basalt.Shell.Docking;

/// <summary>
/// Keeps a document tab from showing its document's view.
/// </summary>
/// <remarks>
/// The tab's close buttons are presenters whose content is the document and whose template is the tab
/// strip's close template, found through the tab strip. A tab taken out of its strip (Reset Layout, closing
/// the window) loses that template, and the presenter looks further up for one that matches a dockable:
/// the window's, which hands out the document's editor or database view. That view is already shown in the
/// document area, a control has one parent only, and Avalonia throws. Measured with a code tab and a query
/// tab, each through Reset Layout and then closing the window.
///
/// A template on the tab itself is found first, and builds nothing: no part of a tab ever shows the view.
/// </remarks>
internal static class DocumentTabContentGuard
{
    private static readonly FuncDataTemplate<IDockable> Nothing = new((_, _) => null);

    public static void Apply(DocumentTabStripItem tab)
    {
        if (!tab.DataTemplates.Contains(Nothing)) tab.DataTemplates.Add(Nothing);
    }
}
