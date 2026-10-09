using Avalonia.Controls;
using Basalt.Shell.Controls;
using Dock.Model.Core;

namespace Basalt.Shell;

public partial class MainWindow
{
    private readonly ContentControl _designerToolboxDock = new();
    private readonly ContentControl _designerPropertiesDock = new();

    private void InitializeDesignerToolDocks()
    {
        _contents[_factory.Toolbox] = _designerToolboxDock;
        _contents[_factory.Properties] = _designerPropertiesDock;
        RefreshDesignerToolDocks();
    }

    private IDock? DefaultDockForTool(IDockable tool)
    {
        if (tool == _factory.Properties)
            return _factory.DesignerToolsArea;
        if (tool == _factory.Toolbox)
            return _factory.ToolboxArea;

        // Everything else that is not already anchored elsewhere (Solution
        // Explorer and the Git panels at top right) opens in the bottom
        // strip the first time it is asked for: Search, References, Object
        // Browser, Tests, Database, the Git history and diff, and the debug
        // tools now open on demand rather than crowding it from the start.
        if (tool == _factory.SolutionExplorer || tool == _factory.GitChanges || tool == _factory.GitBranches)
            return null;

        return _factory.BottomArea;
    }

    private void CancelInactivePropertyFocus(Control? nextProperties)
    {
        if (_designerPropertiesDock.Content is WinFormsPropertyPanel previous
            && !ReferenceEquals(previous, nextProperties))
            previous.CancelSelectedPropertyFocusRequest();
    }

    private void RefreshDesignerToolDocks()
    {
        ViewModel.DesignerUndoRequested = null;
        ViewModel.DesignerRedoRequested = null;
        var document = ViewModel.ActiveDocument;
        var host = document is not null
            && _documents.TryGetValue(document, out var dockable)
            && _contents.TryGetValue(dockable, out var content)
                ? content as DesignerHost : null;

        // Stable dock hosts preserve floating windows and docking positions
        // while their document-specific controls follow the active designer.
        if (host is { ShowingDesigner: true, DesignerView: WinFormsDesignerView forms })
        {
            ViewModel.DesignerUndoRequested = forms.UndoDesignerEdit;
            ViewModel.DesignerRedoRequested = forms.RedoDesignerEdit;
            CancelInactivePropertyFocus(forms.PropertiesContent);
            _designerToolboxDock.Content = forms.ToolboxContent;
            _designerPropertiesDock.Content = forms.PropertiesContent;
            _elementTree?.Show(null);
            return;
        }

        CancelInactivePropertyFocus(null);
        var session = host is { ShowingDesigner: true } && document is not null
            ? ViewModel.DesignerSessionFor(document) : null;

        // DesignerHost caches its own drawing and rebuilds it - with a fresh
        // DesignerSessionFor call of its own - only when the markup no longer
        // matches what it last drew: switching to the text half, editing by
        // hand, and switching back is exactly that case. Without this,
        // ActiveDesigner (what Undo/Redo and every other designer-wide
        // command act on) kept pointing at the session from before the
        // switch, which the screen no longer agreed with.
        if (session is not null) ViewModel.ActiveDesigner = session;

        _designerToolboxDock.Content = session is null ? null : _toolbox;
        _designerPropertiesDock.Content = session is null ? null : _properties;
        _properties?.Show(session);
        _events?.Show(session);
        _elementTree?.Show(session);
    }
}
