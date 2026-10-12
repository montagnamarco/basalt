using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell;

public partial class MainWindow
{
    private void InitializeDocumentViewLifetime()
    {
        ViewModel.OpenDocuments.CollectionChanged += (_, _) =>
        {
            foreach (var document in _documents.Keys.Where(document => !ViewModel.OpenDocuments.Contains(document)).ToArray())
            {
                ReleaseDocumentView(document);
            }
        };
        _factory.DockableClosed += (_, args) =>
        {
            // A closed terminal tab takes its shell with it. Its view used to
            // stay in _contents, alive and attached to a shell nobody could
            // see or reach again, until the window closed.
            if (args.Dockable is TerminalTool terminal)
            {
                if (_contents.Remove(terminal, out var view)) (view as IDisposable)?.Dispose();
                return;
            }

            if (args.Dockable is not IdeDocument dockable || !ViewModel.OpenDocuments.Contains(dockable.Document))
            {
                return;
            }

            if (ReferenceEquals(ViewModel.ActiveDocument, dockable.Document))
            {
                ViewModel.CloseActiveDocument();
            }
            else
            {
                ViewModel.CloseDocument(dockable.Document);
            }
        };
    }

    private void ReleaseDocumentView(EditorDocumentViewModel document)
    {
        if (!_documents.Remove(document, out var dockable))
        {
            return;
        }

        if (_contents.Remove(dockable, out var content))
        {
            (content as IDisposable)?.Dispose();
        }

        _headed.Remove(dockable);
        RefreshDesignerToolDocks();
        // Removing the model may originate from Close All or opening another solution.
        // Close the visual tab too; the closed event observes an already removed model.
        if (dockable.Owner is Dock.Model.Core.IDock owner && owner.VisibleDockables?.Contains(dockable) == true)
        {
            _factory.CloseDockable(dockable);
        }
    }
}
