using Basalt.Core.Localization;
using Basalt.Shell.Database;

namespace Basalt.Shell;

/// <summary>
/// The table designer's wiring: where its documents open, what "Generate script only" does with its text, and
/// refreshing the Explorer after a table was created or changed.
/// </summary>
public partial class MainWindow
{
    private void WireDatabaseDesign()
    {
        _databaseExplorer.DesignRequested += (_, request) => Guarded.Run(
            () => OpenTableDesignerAsync(request), ViewModel.WriteOutput, "database");

        _databaseExplorer.DiagramRequested += (_, request) => Guarded.Run(
            () => OpenDatabaseDiagramAsync(request), ViewModel.WriteOutput, "database");
    }

    /// <summary>Where the diagrams' layouts are kept: beside the settings file, so BASALT_SETTINGS moves them with it.</summary>
    private DiagramLayoutStore? _diagramLayouts;

    /// <summary>Opens the diagram of a connection. One diagram per connection: an open one is shown again.</summary>
    internal async Task OpenDatabaseDiagramAsync(DatabaseDiagramRequest request)
    {
        var open = _databaseDocuments.FirstOrDefault(d => d.View is DatabaseDiagramView view && view.ConnectionId == request.ConnectionId);

        if (open is not null)
        {
            ShowDatabaseDocument(open);
            return;
        }

        _diagramLayouts ??= new DiagramLayoutStore(DiagramLayoutStore.PathBeside(_settingsStore.FilePath));

        var diagram = new DatabaseDiagramView(_databases, request.ConnectionId, _diagramLayouts);
        var document = new DatabaseDocument(diagram.Title, diagram);

        // A double-click on a box is the same question as Design Table on it.
        diagram.TableDesignRequested += (_, name) => Guarded.Run(
            () => OpenTableDesignerAsync(new DatabaseDesignRequest(request.ConnectionId, name)), ViewModel.WriteOutput, "database");

        AddDatabaseDocument(document);

        await diagram.LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Opens the designer on a table, or on a new one. A table already open is shown again.</summary>
    internal async Task OpenTableDesignerAsync(DatabaseDesignRequest request)
    {
        if (request.Name is not null)
        {
            var open = _databaseDocuments.FirstOrDefault(d =>
                d.View is TableDesignerView view && view.ConnectionId == request.ConnectionId && view.Table == request.Name);

            if (open is not null)
            {
                ShowDatabaseDocument(open);
                return;
            }
        }

        var designer = new TableDesignerView(_databases, request.ConnectionId, request.Name);
        var document = new DatabaseDocument(designer.Title, designer);

        designer.TitleChanged += (_, _) => document.Title = designer.Title;
        designer.ScriptOnlyRequested += (_, sql) => OpenQueryDocument(request.ConnectionId, sql, null);
        designer.Saved += (_, _) => _ = _databaseExplorer.RefreshConnectionAsync(request.ConnectionId);

        AddDatabaseDocument(document);

        await designer.LoadAsync().ConfigureAwait(true);
    }
}
