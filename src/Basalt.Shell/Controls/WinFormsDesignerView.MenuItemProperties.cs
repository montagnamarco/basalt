using System.ComponentModel;
using System.Drawing;
using Avalonia.Controls;
using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private int _menuItemPropertyGeneration;
    private bool _restoringMenuStripProperties;
    private string? _activeMenuItemName;
    private static readonly IReadOnlySet<string> MenuItemReadOnlyProperties =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Name" };

    private async void ShowMenuItemProperties(string stripName, string? itemName)
    {
        if (_restoringMenuStripProperties)
        {
            return;
        }
        var generation = ++_menuItemPropertyGeneration;
        if (_session is not { } session || itemName is null)
        {
            _restoringMenuStripProperties = true;
            try
            {
                ShowProperties(_surface.SelectedControl);
            }
            finally
            {
                _restoringMenuStripProperties = false;
            }
            return;
        }

        if (FindMenuItem(session, stripName, itemName) is not { } item)
        {
            return;
        }

        _activeMenuItemName = itemName;
        var original = session.Document;
        try
        {
            var assignments = await Task.Run(() =>
                original.Menus.ReadItemProperties(stripName, itemName));
            if (generation != _menuItemPropertyGeneration || !ReferenceEquals(session, _session) ||
                !ReferenceEquals(original, session.Document) || !IsSelectedMenuItem(stripName, itemName) || _disposed)
            {
                return;
            }

            _properties.Show(item, property => assignments.ContainsKey(property)
                    ? ReadMenuItemValue(item, property) : null,
                (property, text) => RunEditAsync(() => SetMenuItemPropertyAsync(
                    session, original, stripName, itemName, item, property, text)),
                componentName: itemName,
                nestedEdit: (property, text) => RunEditAsync(() => SetMenuItemChildPropertyAsync(
                    session, original, stripName, itemName, item, property, text)),
                editorRequested: property => OpenMenuItemPropertyEditor(
                    session, original, stripName, itemName, item, property),
                readOnlyProperties: MenuItemReadOnlyProperties);
            _events.Show(item, eventName => _handlers.GetValueOrDefault(itemName)?.GetValueOrDefault(eventName),
                (eventName, handler) => RunEditAsync(() =>
                {
                    RequireSelectedMenuItem(session, original, stripName, itemName);
                    return SetHandlerAsync(item, itemName, eventName, handler);
                }),
                eventName => itemName + "_" + eventName);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private static string? ReadMenuItemValue(Forms.ToolStripItem item, string property)
    {
        var descriptor = TypeDescriptor.GetProperties(item)[property];
        return descriptor?.Converter.ConvertToInvariantString(descriptor.GetValue(item));
    }

    private Task SetMenuItemPropertyAsync(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string strip, string itemName, Forms.ToolStripItem item, string property, string? text)
    {
        var descriptor = TypeDescriptor.GetProperties(item)[property]
            ?? throw new ArgumentException("The property does not exist.", nameof(property));
        var value = text is null ? null : descriptor.Converter.ConvertFromInvariantString(text);
        try
        {
            return ApplyMenuItemPropertyAsync(session, original, strip, itemName, property,
                text is null ? null : WinFormsDesignerValues.Serialize(value));
        }
        finally
        {
            if (value is Font { IsSystemFont: false } font)
            {
                font.Dispose();
            }
        }
    }

    private Task SetMenuItemChildPropertyAsync(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string strip, string itemName, Forms.ToolStripItem item, DesignableProperty property, string? text)
    {
        if (text is null)
        {
            using var defaults = (Forms.ToolStripItem)Activator.CreateInstance(item.GetType())!;
            text = WinFormsExpandableProperties.Children(defaults, property.ParentName!)
                .Single(child => child.Name == property.Name).DefaultValue
                ?? throw new InvalidOperationException("The child has no default value.");
        }

        var value = WinFormsExpandableProperties.Rebuild(item, property.ParentName!, property.Name, text);
        try
        {
            return ApplyMenuItemPropertyAsync(session, original, strip, itemName, property.ParentName!,
                WinFormsDesignerValues.Serialize(value));
        }
        finally
        {
            if (value is Font { IsSystemFont: false } font)
            {
                font.Dispose();
            }
        }
    }

    private Task ApplyMenuItemPropertyAsync(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string strip, string itemName, string property, string? expression)
    {
        RequireSelectedMenuItem(session, original, strip, itemName);
        return session.ApplyCollectionAsync(document =>
            document.Menus.SetItemProperty(strip, itemName, property, expression));
    }

    private Task RequireAndSetMenuItemImageAsync(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string strip, string itemName, string property, byte[] bytes, bool isIcon)
    {
        RequireSelectedMenuItem(session, original, strip, itemName);
        return session.SetMenuItemImageResourceAsync(strip, itemName, property, bytes, isIcon);
    }

    private Task RequireAndClearMenuItemImageAsync(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string strip, string itemName, string property)
    {
        RequireSelectedMenuItem(session, original, strip, itemName);
        return session.ClearMenuItemImageResourceAsync(strip, itemName, property);
    }

    private Task RequireAndSetMenuItemProjectImageAsync(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string strip, string itemName, string property, string rootNamespace, string resourceName)
    {
        RequireSelectedMenuItem(session, original, strip, itemName);
        return session.SetMenuItemProjectImageResourceAsync(strip, itemName, property, rootNamespace, resourceName);
    }

    private async void OpenMenuItemPropertyEditor(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string strip, string itemName, Forms.ToolStripItem item, DesignableProperty property)
    {
        if (_busy || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }
        var offset = _properties.CapturePropertyEditorOffset();
        var generation = _menuItemPropertyGeneration;
        Font? selectedFont = null;
        try
        {
            var descriptor = TypeDescriptor.GetProperties(item)[property.Name];
            var value = descriptor?.GetValue(item);
            if (descriptor is not null && IsImageProperty(descriptor.PropertyType))
            {
                var isIcon = descriptor.PropertyType.FullName == "System.Drawing.Icon";
                var projectPath = WinFormsProjectResources.FindProject(_document.FilePath);
                var editor = new WinFormsImageResourceDialog(EncodeCurrentImage(value), isIcon, _importImageFile, projectPath);
                if (!await editor.ShowDialog<bool>(owner))
                {
                    return;
                }
                if (!ReferenceEquals(_session, session) || !ReferenceEquals(session.Document, original))
                {
                    throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
                }
                if (editor.UsedProjectResource && editor.SelectedProjectResourceName is { } resourceName)
                {
                    var rootNamespace = WinFormsProjectResources.RootNamespace(projectPath!);
                    RunEditAsync(() => RequireAndSetMenuItemProjectImageAsync(
                        session, original, strip, itemName, property.Name, rootNamespace, resourceName));
                }
                else if (editor.Cleared)
                {
                    RunEditAsync(() => RequireAndClearMenuItemImageAsync(session, original, strip, itemName, property.Name));
                }
                else if (editor.Changed && editor.SelectedBytes is { } bytes)
                {
                    RunEditAsync(() => RequireAndSetMenuItemImageAsync(session, original, strip, itemName, property.Name, bytes, isIcon));
                }
                return;
            }
            if (value is Color color)
            {
                var editor = new WinFormsColorEditor(color);
                if (!await editor.ShowDialog<bool>(owner))
                {
                    return;
                }
                value = editor.SelectedColor;
            }
            else if (value is Font font)
            {
                var editor = new WinFormsFontEditor(font);
                if (!await editor.ShowDialog<bool>(owner))
                {
                    return;
                }
                selectedFont = editor.SelectedFont;
                value = selectedFont;
            }
            else
            {
                return;
            }

            var expression = WinFormsDesignerValues.Serialize(value);
            RunEditAsync(() => ApplyMenuItemPropertyAsync(session, original, strip, itemName, property.Name, expression));
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            selectedFont?.Dispose();
            if (_attached && ReferenceEquals(_session, session) && generation == _menuItemPropertyGeneration)
            {
                _properties.RestoreSelectedPropertyFocus(offset);
            }
        }
    }

    private void InvalidateMenuItemProperties()
    {
        ++_menuItemPropertyGeneration;
        _activeMenuItemName = null;
    }

    private bool IsSelectedMenuItem(string strip, string itemName) =>
        _selectedName.Equals(strip, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(_activeMenuItemName, itemName, StringComparison.OrdinalIgnoreCase);

    private void RequireSelectedMenuItem(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string strip, string itemName)
    {
        if (!ReferenceEquals(_session, session) || !ReferenceEquals(session.Document, original) ||
            !IsSelectedMenuItem(strip, itemName))
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        }
    }

    private Task OpenMenuItemDefaultEventAsync(string stripName, string itemName) =>
        _session is { } session && FindMenuItem(session, stripName, itemName) is { } item
            ? OpenDefaultEventAsync(item, itemName) : Task.CompletedTask;

    private static IEnumerable<string> MenuItemFieldNames(WinFormsDesignerDocument document)
    {
        var names = new List<string>();
        var menu = document.Menus;
        foreach (var strip in document.Controls.Where(control =>
                     control.TypeName.EndsWith("MenuStrip", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                names.AddRange(menu.Read(strip.Name).Select(item => item.Name));
            }
            catch (NotSupportedException)
            {
                // Unsupported trees remain editable in code; do not guess their event fields.
            }
        }

        return names;
    }

    private static Forms.ToolStripItem? FindMenuItem(WinFormsDesignerSession session, string strip, string itemName)
    {
        if (session.FindControl(strip) is not Forms.MenuStrip menu)
        {
            return null;
        }
        var pending = new Stack<Forms.ToolStripItem>(menu.Items.Cast<Forms.ToolStripItem>());
        while (pending.TryPop(out var item))
        {
            if (string.Equals(item.Name, itemName, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
            if (item is Forms.ToolStripMenuItem parent)
            {
                foreach (Forms.ToolStripItem child in parent.DropDownItems)
                {
                    pending.Push(child);
                }
            }
        }

        return null;
    }
}
