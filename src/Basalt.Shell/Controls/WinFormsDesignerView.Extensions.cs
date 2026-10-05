using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private readonly WinFormsComponentTray _tray = new();
    private readonly WinFormsDesignerLayoutToolbar _layoutToolbar = new();

    private void InitializeExtendedDesigner()
    {
        _layoutToolbar.CommandRequested += command => _surface.ApplyLayoutCommand(command);
        _layoutToolbar.TabOrderModeChanged += enabled => _surface.TabOrderMode = enabled;
        _surface.TabOrderModeChanged += enabled => _layoutToolbar.TabOrderMode = enabled;
        _surface.TabOrderEdits += edits => RunEditAsync(() => _session!.SetTabIndicesBatchAsync(edits));
        _tray.SelectionChanged += name =>
        {
            if (_session?.FindComponent(name ?? "") is { } component) ShowComponentProperties(component);
        };
        _tray.DefaultEventRequested += name => RunEditAsync(() => OpenDefaultEventAsync(name));
        _tray.DeleteRequested += name =>
        {
            if (_session?.FindComponent(name) is { } component) RunEditAsync(() => DeleteComponentAsync(component));
        };
        KeyDown += (_, args) =>
        {
            if (_session is null || args.Handled || args.KeyModifiers != KeyModifiers.Control
                || args.Source is TextBox
                || args.Source is Avalonia.Visual source && source.GetVisualAncestors().OfType<TextBox>().Any()) return;
            if (args.Key == Key.Z) RunEditAsync(() => _session!.UndoAsync());
            else if (args.Key == Key.Y) RunEditAsync(() => _session!.RedoAsync());
            else return;
            args.Handled = true;
        };
    }

    private void ShowComponentProperties(IComponent component)
    {
        InvalidateMenuItemProperties();
        if (_session is null) return;
        _surface.Select(null);
        _selectedName = _session.NameOfComponent(component);
        UpdateObjectSelector();
        _tray.Select(_selectedName);
        _layoutToolbar.SelectedCount = 0;
        _properties.Show(component, property => _session.ExplicitComponentValue(component, property),
            (property, value) => RunEditAsync(() => SetGridPropertyAsync(component, property, value)), componentName: _selectedName,
            nestedEdit: (property, value) => RunEditAsync(() => _session.SetNestedPropertyAsync(component, property.ParentName!, property.Name, value)),
            editorRequested: property => OpenPropertyEditor(component, property),
            referenceNames: _session.ReferenceNames);
        var name = _selectedName;
        _events.Show(component, eventName => _handlers.GetValueOrDefault(name)?.GetValueOrDefault(eventName),
            (eventName, handler) => RunEditAsync(() => SetHandlerAsync(component, name, eventName, handler)),
            eventName => name + "_" + eventName);
    }

    private void RefreshSelectionProperties()
    {
        if (_session?.FindComponent(_selectedName) is { } component) ShowComponentProperties(component);
        else ShowProperties(_surface.SelectedControl);
    }

    private async Task DeleteComponentAsync(IComponent component)
    {
        var session = _session!;
        if (_getCode is null || (!File.Exists(CompanionPath) && _findCode?.Invoke(CompanionPath) is null))
        {
            await session.DeleteComponentAsync(component);
            return;
        }
        var name = session.NameOfComponent(component);
        var code = await _getCode(CompanionPath);
        var before = code.Text;
        var className = session.Document.ClassName;
        var after = await Task.Run(() => WinFormsEventHandlers.RemoveControls(before, className,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name }));
        var originalBindings = new Dictionary<string, IReadOnlyDictionary<string, string>>(_handlers, StringComparer.OrdinalIgnoreCase);
        var updatedBindings = new Dictionary<string, IReadOnlyDictionary<string, string>>(originalBindings, StringComparer.OrdinalIgnoreCase);
        updatedBindings.Remove(name);
        void Restore(string expected, string replacement, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> bindings)
        {
            var current = ResolveTransactionDocument(code);
            if (current.Text != expected) throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            current.Text = replacement;
            _handlers.Clear();
            foreach (var (field, handlers) in bindings) _handlers[field] = handlers;
        }
        await session.DeleteComponentAsync(component,
            () => Restore(before, after, updatedBindings),
            () => Restore(after, before, originalBindings),
            () => Restore(before, after, updatedBindings));
    }

    private async void OpenCollectionEditor(Forms.Control control, string collection)
    {
        if (_busy || _session is not { } session || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var name = session.NameOf(control);
        var selectedName = _selectedName;
        var propertyOffset = _properties.CapturePropertyEditorOffset();
        try
        {
            if (control is Forms.MenuStrip menu && collection == "Items")
            {
                await EditMenuAsync(session, menu, owner);
                return;
            }

            WinFormsCollectionEditor editor;
            if (collection == "Columns")
            {
                var state = session.Document.Collections.ReadColumns(name);
                if (!state.IsSupported)
                {
                    throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsCollectionUnsupported));
                }

                editor = new(state.Columns);
            }
            else
            {
                var state = session.Document.Collections.ReadItems(name);
                if (!state.IsSupported)
                {
                    throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsCollectionUnsupported));
                }

                editor = new(state.Items);
            }

            var original = session.Document;
            if (!await editor.ShowDialog<bool>(owner))
            {
                return;
            }

            if (!ReferenceEquals(_session, session) || !ReferenceEquals(session.Document, original))
            {
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            }

            RunEditAsync(() => collection == "Columns"
                ? session.SetColumnsAsync(control, editor.Columns)
                : session.SetItemsAsync(control, editor.Items));
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            if (_attached && ReferenceEquals(_session, session) && _selectedName == selectedName)
            {
                _properties.RestoreSelectedPropertyFocus(propertyOffset);
            }
        }
    }
}
