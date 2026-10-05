using Avalonia.Controls;
using Basalt.Shell.Controls;
using Dock.Model.Core;

namespace Basalt.Shell;

public partial class MainWindow
{
    private readonly ContentControl _designerToolboxDock = new();
    private readonly ContentControl _designerPropertiesDock = new();
    private readonly ContentControl _designerEventsDock = new();

    private void InitializeDesignerToolDocks()
    {
        _contents[_factory.Toolbox] = _designerToolboxDock;
        _contents[_factory.Properties] = _designerPropertiesDock;
        _contents[_factory.Events] = _designerEventsDock;
        RefreshDesignerToolDocks();
    }

    private IDock? DefaultDockForTool(IDockable tool)
    {
        if (tool == _factory.Properties || tool == _factory.Events)
            return _factory.DesignerToolsArea;
        if (tool == _factory.Toolbox)
            return _factory.ToolboxArea;
        if (tool == _factory.Problems || tool == _factory.Output)
            return _factory.BottomArea;
        return null;
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
            _designerEventsDock.Content = forms.EventsContent;
            _elementTree?.Show(null);
            return;
        }

        CancelInactivePropertyFocus(null);
        var session = host is { ShowingDesigner: true } && document is not null
            ? ViewModel.DesignerSessionFor(document) : null;
        _designerToolboxDock.Content = session is null ? null : _toolbox;
        _designerPropertiesDock.Content = session is null ? null : _properties;
        _designerEventsDock.Content = session is null ? null : _events;
        _properties?.Show(session);
        _events?.Show(session);
        _elementTree?.Show(session);
    }
}
