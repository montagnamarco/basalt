using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private bool _disposed;
    private int _loadGeneration;
    private string[] _retainedSelectionNames = [];

    private async void AttachDesignerSession()
    {
        if (_disposed)
            return;
        var generation = ++_loadGeneration;
        var retainedSession = _session is not null && _session.Document.Source == _document.Text
            ? _session : null;
        if (retainedSession is not null)
        {
            RestoreSessionVisuals();
        }
        else
        {
            ReleaseDesignerSession();
        }

        try
        {
            var source = _document.Text;
            var parsed = retainedSession?.Document
                ?? await Task.Run(() => new WinFormsDesignerDocument(source));
            var resourcePath = Path.ChangeExtension(CompanionPath, ".resx");
            var resourceBuffer = _findCode?.Invoke(resourcePath);
            var resourceSource = resourceBuffer?.Text
                ?? (File.Exists(resourcePath) ? await File.ReadAllTextAsync(resourcePath) : null);
            var resourceSnapshot = await ParseResourcesAsync(resourceSource);
            var resources = resourceSnapshot.Document;
            var companion = CompanionPath;
            var companionDocument = _findCode?.Invoke(companion);
            var handlerSource = companionDocument?.Text
                ?? (File.Exists(companion) ? await File.ReadAllTextAsync(companion) : null);
            var bindings = handlerSource is null ? null : await Task.Run(() =>
                parsed.Controls.Select(control => control.Name)
                    .Concat(parsed.Components.Select(component => component.Name))
                    .Concat(MenuItemFieldNames(parsed)).Prepend("Me")
                    .ToDictionary(name => name,
                        name => WinFormsEventHandlers.Read(handlerSource, parsed.ClassName, name),
                        StringComparer.OrdinalIgnoreCase));
            if (_disposed || !_attached || generation != _loadGeneration)
                return;
            if (_document.Text != source || (companionDocument is not null && companionDocument.Text != handlerSource)
                || (resourceBuffer is not null && resourceBuffer.Text != resourceSource))
            {
                AttachDesignerSession();
                return;
            }
            _resourceProblem = resourceSnapshot.Problem;
            _handlers.Clear();
            if (bindings is not null)
            {
                foreach (var (name, handlers) in bindings)
                    _handlers[name] = handlers;
            }

            if (retainedSession is null)
            {
                _session = new WinFormsDesignerSession(parsed, resources, WinFormsProjectResources.FindProject(_document.FilePath),
                    _designContext);
                BindResourceEditing(_session);
                BindImageResourceCreation(_session);
                _session.SourceChanging += ValidateDesignerBuffer;
                _session.Changed += SessionChanged;
                LogUserCodeFailures();
            }
            else
            {
                retainedSession.UpdateResources(resources);
            }
            TrackResourceBuffer(resourceBuffer);
            RestoreSessionVisuals();
        }
        catch (Exception exception)
        {
            if (!_disposed && _attached && generation == _loadGeneration)
                ShowError(exception);
        }
    }

    private void RestoreSessionVisuals()
    {
        if (_session is null)
            return;
        _tray.Show(_session.Document.Components);
        ReplaceSurfaceRoot();
        if (_session.FindComponent(_selectedName) is { } component)
        {
            _tray.Select(_selectedName);
            ShowComponentProperties(component);
        }
        else if (_retainedSelectionNames.Contains(_selectedName, StringComparer.OrdinalIgnoreCase))
        {
            var controls = _retainedSelectionNames.Select(_session.FindControl)
                .OfType<Forms.Control>().ToArray();
            if (controls.Length > 0)
            {
                _surface.SelectControls(controls);
            }
            else
            {
                _surface.Select(_session.Form);
            }
        }
        else
        {
            _surface.Select(_session.FindControl(_selectedName) ?? _session.Form);
        }

        ShowProblems();
    }

    private void ReleaseDesignerSession()
    {
        _surface.RootForm = null;
        if (_session is null)
            return;
        _session.Changed -= SessionChanged;
        _session.SourceChanging -= ValidateDesignerBuffer;
        _session.Dispose();
        _session = null;
    }

    private void ReplaceSurfaceRoot()
    {
        _rebuildingSurface = true;

        try
        {
            _surface.RootForm = _session?.Form;
        }
        finally
        {
            _rebuildingSurface = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _attached = false;
        ++_loadGeneration;
        UnsubscribeFromBuilds();
        TrackResourceBuffer(null);
        ReleaseDesignerSession();

        // Released after the session: its controls came from this context.
        var designContext = _designContext;
        _designContext = null;
        designContext?.Unload();
        _handlers.Clear();
        _properties.Show(null);
        _events.Show(null);
    }

    private void ValidateDesignerBuffer()
    {
        if (_disposed || _session is null || _document.Text != _session.Document.Source)
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
    }
}
