using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Basalt.Core.Localization;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    internal event Action? EventsPanelRequested;
    internal event Action? PropertiesPanelRequested;

    internal Control ToolboxContent => _toolbox;
    internal Control PropertiesContent => _properties;
    internal Control EventsContent => _events;

    internal void UndoDesignerEdit() => RunEditAsync(() => _session!.UndoAsync());
    internal void RedoDesignerEdit() => RunEditAsync(() => _session!.RedoAsync());

    private DockPanel CreateDesignerBody(bool dockTools)
    {
        _surface.DefaultEventRequested += () => RunEditAsync(OpenDefaultEventAsync);
        _surface.CanViewCode = _openCode is not null;
        _surface.CodeRequested += () => RunEditAsync(async () =>
        {
            if (_openCode is not null)
                await _openCode(CompanionPath);
        });
        _surface.PropertiesRequested += () =>
        {
            PropertiesPanelRequested?.Invoke();
            _properties.RequestSelectedPropertyFocus();
        };
        var canvas = new ScrollViewer
        {
            Content = _surface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var center = new DockPanel { Children = { _tray, canvas } };
        DockPanel.SetDock(_tray, Avalonia.Controls.Dock.Bottom);
        var body = new DockPanel();
        _events.VisualStudioStyle = true;

        if (dockTools)
        {
            // The document retains its session and panel controls; the shell
            // hosts those controls in its stable, movable tool windows.
            _properties.ExternalEventsRequested = () => EventsPanelRequested?.Invoke();
        }
        else
        {
            _properties.EventsContent = _events;
            var left = StandaloneTool("WinFormsDesigner_Toolbox", _toolbox, 165);
            var right = StandaloneTool("WinFormsDesigner_Properties", _properties, 285);
            DockPanel.SetDock(left, Avalonia.Controls.Dock.Left);
            DockPanel.SetDock(right, Avalonia.Controls.Dock.Right);
            body.Children.Add(left);
            body.Children.Add(right);
        }

        body.Children.Add(center);
        return body;
    }

    private static DockPanel StandaloneTool(string titleKey, Control content, double width)
    {
        var title = new TextBlock { Text = Localizer.Get(titleKey) };
        DockPanel.SetDock(title, Avalonia.Controls.Dock.Top);
        return new DockPanel { Width = width, Children = { title, content } };
    }
}
