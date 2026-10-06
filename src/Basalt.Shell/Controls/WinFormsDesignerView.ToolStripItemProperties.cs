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
    private int _toolStripItemPropertyGeneration;
    private bool _restoringToolStripItemProperties;
    private string? _activeToolStripItemName;
    private static readonly IReadOnlySet<string> ToolStripItemReadOnlyProperties =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Name" };

    private async void ShowToolStripItemProperties(string stripName, string? itemName)
    {
        if (_restoringToolStripItemProperties)
        {
            return;
        }
        var generation = ++_toolStripItemPropertyGeneration;
        if (_session is not { } session || itemName is null)
        {
            _restoringToolStripItemProperties = true;
            try
            {
                ShowProperties(_surface.SelectedControl);
            }
            finally
            {
                _restoringToolStripItemProperties = false;
            }
            return;
        }

        if (FindToolStripItem(session, stripName, itemName) is not { } item)
        {
            return;
        }

        _activeToolStripItemName = itemName;
        var original = session.Document;
        try
        {
            var assignments = await Task.Run(() =>
                original.ToolStripItems.ReadItemProperties(stripName, itemName));
            if (generation != _toolStripItemPropertyGeneration || !ReferenceEquals(session, _session) ||
                !ReferenceEquals(original, session.Document) || !IsSelectedToolStripItem(stripName, itemName) || _disposed)
            {
                return;
            }

            _properties.Show(item, property => assignments.ContainsKey(property)
                    ? ReadToolStripItemValue(item, property) : null,
                (property, text) => RunEditAsync(() => SetToolStripItemPropertyAsync(
                    session, original, stripName, itemName, item, property, text)),
                componentName: itemName,
                nestedEdit: (property, text) => RunEditAsync(() => SetToolStripItemChildPropertyAsync(
                    session, original, stripName, itemName, item, property, text)),
                editorRequested: property => OpenToolStripItemPropertyEditor(
                    session, original, stripName, itemName, item, property),
                readOnlyProperties: ToolStripItemReadOnlyProperties);
            _events.Show(item, eventName => _handlers.GetValueOrDefault(itemName)?.GetValueOrDefault(eventName),
                (eventName, handler) => RunEditAsync(() =>
                {
                    RequireSelectedToolStripItem(session, original, stripName, itemName);
                    return SetHandlerAsync(item, itemName, eventName, handler);
                }),
                eventName => itemName + "_" + eventName);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private static string? ReadToolStripItemValue(Forms.ToolStripItem item, string property)
    {
        var descriptor = TypeDescriptor.GetProperties(item)[property];
        return descriptor?.Converter.ConvertToInvariantString(descriptor.GetValue(item));
    }

    private Task SetToolStripItemPropertyAsync(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string strip, string itemName, Forms.ToolStripItem item, string property, string? text)
    {
        var descriptor = TypeDescriptor.GetProperties(item)[property]
            ?? throw new ArgumentException("The property does not exist.", nameof(property));
        var value = text is null ? null : descriptor.Converter.ConvertFromInvariantString(text);
        try
        {
            return ApplyToolStripItemPropertyAsync(session, original, strip, itemName, property,
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

    private Task SetToolStripItemChildPropertyAsync(WinFormsDesignerSession session, WinFormsDesignerDocument original,
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
            return ApplyToolStripItemPropertyAsync(session, original, strip, itemName, property.ParentName!,
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

    private Task ApplyToolStripItemPropertyAsync(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string strip, string itemName, string property, string? expression)
    {
        RequireSelectedToolStripItem(session, original, strip, itemName);
        return session.ApplyCollectionAsync(document =>
            document.ToolStripItems.SetItemProperty(strip, itemName, property, expression));
    }

    private Task RequireAndSetToolStripItemImageAsync(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string strip, string itemName, string property, byte[] bytes, bool isIcon)
    {
        RequireSelectedToolStripItem(session, original, strip, itemName);
        return session.SetToolStripItemImageResourceAsync(strip, itemName, property, bytes, isIcon);
    }

    private Task RequireAndClearToolStripItemImageAsync(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string strip, string itemName, string property)
    {
        RequireSelectedToolStripItem(session, original, strip, itemName);
        return session.ClearToolStripItemImageResourceAsync(strip, itemName, property);
    }

    private async void OpenToolStripItemPropertyEditor(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string strip, string itemName, Forms.ToolStripItem item, DesignableProperty property)
    {
        if (_busy || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }
        var offset = _properties.CapturePropertyEditorOffset();
        var generation = _toolStripItemPropertyGeneration;
        Font? selectedFont = null;
        try
        {
            var descriptor = TypeDescriptor.GetProperties(item)[property.Name];
            var value = descriptor?.GetValue(item);
            if (descriptor is not null && IsImageProperty(descriptor.PropertyType))
            {
                var isIcon = descriptor.PropertyType.FullName == "System.Drawing.Icon";
                var editor = new WinFormsImageResourceDialog(EncodeCurrentImage(value), isIcon, _importImageFile);
                if (!await editor.ShowDialog<bool>(owner))
                {
                    return;
                }
                if (!ReferenceEquals(_session, session) || !ReferenceEquals(session.Document, original))
                {
                    throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
                }
                if (editor.Cleared)
                {
                    RunEditAsync(() => RequireAndClearToolStripItemImageAsync(session, original, strip, itemName, property.Name));
                }
                else if (editor.Changed && editor.SelectedBytes is { } bytes)
                {
                    RunEditAsync(() => RequireAndSetToolStripItemImageAsync(session, original, strip, itemName, property.Name, bytes, isIcon));
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
            RunEditAsync(() => ApplyToolStripItemPropertyAsync(session, original, strip, itemName, property.Name, expression));
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            selectedFont?.Dispose();
            if (_attached && ReferenceEquals(_session, session) && generation == _toolStripItemPropertyGeneration)
            {
                _properties.RestoreSelectedPropertyFocus(offset);
            }
        }
    }

    private void InvalidateToolStripItemProperties()
    {
        ++_toolStripItemPropertyGeneration;
        _activeToolStripItemName = null;
    }

    private bool IsSelectedToolStripItem(string strip, string itemName) =>
        _selectedName.Equals(strip, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(_activeToolStripItemName, itemName, StringComparison.OrdinalIgnoreCase);

    private void RequireSelectedToolStripItem(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string strip, string itemName)
    {
        if (!ReferenceEquals(_session, session) || !ReferenceEquals(session.Document, original) ||
            !IsSelectedToolStripItem(strip, itemName))
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        }
    }

    // A ToolStrip/StatusStrip's own Items collection is flat: unlike a MenuStrip's
    // ToolStripMenuItem tree, items added through the "Type Here" box never nest inside
    // one another, so there is no DropDownItems walk to do here.
    private static Forms.ToolStripItem? FindToolStripItem(WinFormsDesignerSession session, string strip, string itemName)
    {
        if (session.FindControl(strip) is not Forms.ToolStrip toolStrip)
        {
            return null;
        }
        foreach (Forms.ToolStripItem item in toolStrip.Items)
        {
            if (string.Equals(item.Name, itemName, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }
        return null;
    }
}
