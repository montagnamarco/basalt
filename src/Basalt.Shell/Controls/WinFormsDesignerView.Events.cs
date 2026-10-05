using System.ComponentModel;
using System.IO.Ports;
using Basalt.Core.Localization;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private Task OpenDefaultEventAsync()
    {
        if (_session is null || _surface.SelectedControl is not { } control)
            return Task.CompletedTask;

        return OpenDefaultEventAsync(control, _session.NameOf(control));
    }

    private Task OpenDefaultEventAsync(string componentName)
    {
        if (_session?.FindComponent(componentName) is not { } component)
            return Task.CompletedTask;

        return OpenDefaultEventAsync(component, _session.NameOfComponent(component));
    }

    private Task OpenDefaultEventAsync(object component, string field)
    {
        var descriptor = TypeDescriptor.GetDefaultEvent(component)
            ?? (component is SerialPort
                ? TypeDescriptor.GetEvents(component)[nameof(SerialPort.DataReceived)]
                : null);
        if (descriptor is null)
            return Task.CompletedTask;

        var handler = _handlers.GetValueOrDefault(field)?.GetValueOrDefault(descriptor.Name)
            ?? (field == "Me" ? _session!.Document.ClassName : field) + "_" + descriptor.Name;
        return SetHandlerAsync(component, field, descriptor.Name, handler, preferExisting: true);
    }

    private async Task SetHandlerAsync(object control, string fieldName, string eventName, string? handlerName, bool preferExisting = false)
    {
        if (_openCode is null || _session is not { } session)
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeUnavailable));
        var className = session.Document.ClassName;
        var eventType = TypeDescriptor.GetEvents(control)[eventName]?.EventType
            ?? throw new ArgumentException("The event does not exist.", nameof(eventName));
        var code = await _openCode(CompanionPath);
        var original = code.Text;
        var originalCaret = code.CaretOffset;
        var edited = await Task.Run(() =>
        {
            var before = WinFormsEventHandlers.Read(original, className, fieldName);
            var existing = before.GetValueOrDefault(eventName);
            var effectiveHandler = preferExisting ? existing ?? handlerName : handlerName;
            var text = StringComparer.OrdinalIgnoreCase.Equals(existing, effectiveHandler)
                ? original
                : WinFormsEventHandlers.Set(original, className, fieldName, eventName, effectiveHandler, eventType);
            var position = effectiveHandler is null ? 0
                : WinFormsEventHandlers.HandlerOffset(text, className, effectiveHandler);
            return (Text: text, Position: position,
                Before: before,
                After: WinFormsEventHandlers.Read(text, className, fieldName));
        });
        if (!ReferenceEquals(session, _session) || _disposed)
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));

        void Restore(string expected, string replacement, int caret, IReadOnlyDictionary<string, string> bindings)
        {
            var current = ResolveTransactionDocument(code);
            if (current.Text != expected)
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            current.Text = replacement;
            current.RequestCaret(caret);
            _handlers[fieldName] = bindings;
        }

        if (edited.Text == original)
        {
            var current = ResolveTransactionDocument(code);
            if (current.Text != original)
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            current.RequestCaret(edited.Position);
            _handlers[fieldName] = edited.After;
            if (string.Equals(_selectedName, fieldName, StringComparison.OrdinalIgnoreCase))
                RefreshSelectionProperties();
            return;
        }
        await session.ApplyCompanionEditAsync(
            () => Restore(original, edited.Text, edited.Position, edited.After),
            () => Restore(edited.Text, original, originalCaret, edited.Before),
            () => Restore(original, edited.Text, edited.Position, edited.After));
    }
}
