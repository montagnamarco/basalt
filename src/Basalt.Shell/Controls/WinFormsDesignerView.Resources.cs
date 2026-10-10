using System.ComponentModel;
using System.Xml;
using Avalonia.Threading;
using Basalt.Core.Localization;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private EditorDocumentViewModel? _resourceBuffer;
    private string? _resourceProblem;
    private int _resourceGeneration;
    private int _resourceEditDepth;

    private void BindResourceEditing(WinFormsDesignerSession session)
    {
        // The culture files are bound with the neutral one: a delete or rename reaches them before any language is shown.
        BindCultureResourceEditing(session);
        session.PrepareResourceEdit = async expected =>
        {
            var path = Path.ChangeExtension(CompanionPath, ".resx");
            var buffer = _findCode is null ? _resourceBuffer : _findCode(path);
            if (buffer is null)
            {
                var getDocument = _getCode ?? _openCode;
                if (getDocument is null)
                    throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeUnavailable));
                buffer = await getDocument(path);
            }
            if (_disposed || !ReferenceEquals(session, _session) || buffer.Text != expected)
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            TrackResourceBuffer(buffer);
        };
        session.CommitResourceSource = (expected, replacement) =>
        {
            var path = Path.ChangeExtension(CompanionPath, ".resx");
            var buffer = _findCode is null ? _resourceBuffer : _findCode(path);
            if (_disposed || !ReferenceEquals(session, _session) || buffer is null || buffer.Text != expected)
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            ++_resourceEditDepth;
            try
            {
                ++_resourceGeneration;
                try
                {
                    buffer.Text = replacement;
                }
                catch
                {
                    // A listener can throw after the setter updates its backing
                    // field. Restore only our own write, preserving later edits.
                    if (buffer.Text == replacement)
                    {
                        try { buffer.Text = expected; }
                        catch { /* The setter may have restored its field before notifying. */ }
                    }
                    throw;
                }
                TrackResourceBuffer(buffer);
                _resourceProblem = null;
            }
            finally { --_resourceEditDepth; }
        };
    }

    private static async Task<(WinFormsResourceDocument? Document, string? Problem)> ParseResourcesAsync(string? source)
    {
        if (source is null) return (null, null);
        try
        {
            return (await Task.Run(() => new WinFormsResourceDocument(source)), null);
        }
        catch (Exception exception) when (exception is XmlException or FormatException)
        {
            return (null, exception.Message);
        }
    }

    private void TrackResourceBuffer(EditorDocumentViewModel? buffer)
    {
        if (ReferenceEquals(_resourceBuffer, buffer)) return;
        if (_resourceBuffer is not null) _resourceBuffer.PropertyChanged -= ResourceBufferChanged;
        ++_resourceGeneration;
        _resourceBuffer = buffer;
        if (_resourceBuffer is not null) _resourceBuffer.PropertyChanged += ResourceBufferChanged;
    }

    private async void ResourceBufferChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(EditorDocumentViewModel.Text)
            || sender is not EditorDocumentViewModel buffer)
            return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ResourceBufferChanged(sender, args));
            return;
        }
        if (_resourceEditDepth > 0) return;
        try
        {
            await RefreshResourceBufferAsync(buffer);
        }
        catch (Exception exception)
        {
            if (!_disposed && _attached && ReferenceEquals(buffer, _resourceBuffer)) ShowError(exception);
        }
    }

    private async Task RefreshResourceBufferAsync(EditorDocumentViewModel buffer)
    {
        if (_disposed || !_attached || !ReferenceEquals(buffer, _resourceBuffer)
            || _session is not { } session) return;
        var generation = ++_resourceGeneration;
        var source = buffer.Text;
        var parsed = await ParseResourcesAsync(source);
        if (_disposed || !_attached || generation != _resourceGeneration
            || !ReferenceEquals(session, _session) || !ReferenceEquals(buffer, _resourceBuffer)
            || buffer.Text != source)
            return;
        _resourceProblem = parsed.Problem;
        if (_document.Text != session.Document.Source)
        {
            ShowProblems();
            return;
        }
        var previousSelection = _editSelectionNames;
        _editSelectionNames = _surface.SelectedControls.Select(session.NameOf).ToArray();
        try
        {
            session.UpdateResources(parsed.Document);
            ShowProblems();
        }
        finally
        {
            _editSelectionNames = previousSelection;
        }
    }
}
