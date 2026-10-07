using System.ComponentModel;
using System.Drawing;
using Avalonia.Controls;
using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>
/// Wires <see cref="WinFormsToolStripDesignEditor"/>'s nested drop-down panel (see
/// <see cref="WinFormsToolStripDropDownPanel"/>) to <see cref="WinFormsMenuDocument"/>: feeding
/// it the DropDownItems tree of whichever ToolStripDropDownButton/SplitButton item is currently
/// selected, applying its edits as one undo step each through <see cref="WinFormsDesignerSession.ApplyCollectionAsync"/>,
/// and showing a selected nested item's properties. Kept in its own file, alongside
/// <see cref="WinFormsDesignerView.ToolStripInline"/>, so that file's top-level Items logic does
/// not grow by a second, nested tree's worth of plumbing.
/// </summary>
internal sealed partial class WinFormsDesignerView
{
    private string? _inlineDropDownOwnerName;
    private WinFormsDesignerDocument? _inlineDropDownSnapshot;
    private int _dropDownItemPropertyGeneration;
    private bool _restoringDropDownItemProperties;
    private string? _activeDropDownItemName;

    /// <summary>Feeds the DropDownItems tree of whichever item the inline ToolStrip editor
    /// currently has selected to its nested panel; called whenever that selection changes,
    /// including every time <see cref="RefreshInlineToolStripEditor"/> re-shows the editor
    /// after an edit, since <see cref="WinFormsToolStripDesignEditor.Show"/> always re-raises
    /// its SelectedItemChanged event.</summary>
    private void RefreshInlineToolStripDropDownItems()
    {
        if (_session is not { } session || _inlineToolStripEditor is null ||
            _inlineToolStripEditor.SelectedDropDownOwner is not { } owner)
        {
            _inlineDropDownOwnerName = null;
            _inlineDropDownSnapshot = null;
            return;
        }

        _inlineDropDownOwnerName = owner.Name;
        _inlineDropDownSnapshot = session.Document;
        try
        {
            _inlineToolStripEditor.ShowDropDownItems(session.Document.Menus.Read(owner.Name));
        }
        catch (NotSupportedException)
        {
            _inlineToolStripEditor.ShowDropDownItems([], unsupported: true);
        }
    }

    private Task ApplyInlineToolStripDropDownEditAsync(WinFormsToolStripDropDownChange change)
    {
        if (_session is not { } session || _inlineDropDownOwnerName is not { } ownerName ||
            !ReferenceEquals(_inlineDropDownSnapshot, session.Document) ||
            _inlineToolStripEditor?.SelectedDropDownOwner is not { } owner ||
            !owner.Name.Equals(ownerName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        }

        var original = session.Document;
        var menu = original.Menus;
        var items = menu.Read(ownerName).ToList();

        if (change.Action == WinFormsMenuDesignAction.DeleteItem)
        {
            return DeleteInlineDropDownItemAsync(session, ownerName, change.Name!, items);
        }

        switch (change.Action)
        {
            case WinFormsMenuDesignAction.AddItem:
            case WinFormsMenuDesignAction.AddSeparator:
                var separator = change.Action == WinFormsMenuDesignAction.AddSeparator;
                var typeName = separator ? "ToolStripSeparator" : change.TypeName;
                var name = NewDropDownItemName(menu, typeName);
                items.Add(new WinFormsMenuItem(name, change.Parent, separator ? "" : change.Text ?? "",
                    separator, typeName));
                _inlineToolStripEditor?.SelectPendingDropDownItem(name);
                break;

            case WinFormsMenuDesignAction.ChangeText:
                var index = items.FindIndex(item => item.Name == change.Name && !item.IsSeparator);
                if (index < 0)
                {
                    throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
                }
                items[index] = items[index] with { Text = change.Text ?? "" };
                break;
        }

        return session.ApplyCollectionAsync(current =>
        {
            if (!ReferenceEquals(current, original))
            {
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            }
            return current.Menus.Set(ownerName, items);
        });
    }

    /// <summary>Deletes a nested item inside a DropDownButton/SplitButton's own cascading
    /// DropDownItems tree through <see cref="WinFormsDesignerSession.DeleteDropDownItemAsync"/>,
    /// the same one-undo-step companion Handles cleanup <see
    /// cref="WinFormsDesignerView.DeleteInlineMenuItemAsync"/> uses for a MenuStrip item. Selects
    /// the neighbouring nested item before the deletion commits, Visual Studio's own rule.</summary>
    private Task DeleteInlineDropDownItemAsync(WinFormsDesignerSession session, string ownerName, string itemName,
        List<WinFormsMenuItem> itemsBeforeRemoval)
    {
        _inlineToolStripEditor?.SelectPendingDropDownItem(
            WinFormsMenuTreeHelpers.SelectionAfterRemoval(itemsBeforeRemoval, itemName, ownerName));

        if (_getCode is null || (!File.Exists(CompanionPath) && _findCode?.Invoke(CompanionPath) is null))
        {
            return session.DeleteDropDownItemAsync(ownerName, itemName);
        }

        return DeleteInlineDropDownItemWithCompanionAsync(session, ownerName, itemName);
    }

    private async Task DeleteInlineDropDownItemWithCompanionAsync(WinFormsDesignerSession session, string ownerName, string itemName)
    {
        var removedNames = session.Document.Menus.DescendantNames(ownerName, itemName);
        var className = session.Document.ClassName;
        var code = await _getCode!(CompanionPath);
        var before = code.Text;
        var after = await Task.Run(() => WinFormsEventHandlers.RemoveControls(before, className, removedNames));

        var originalBindings = new Dictionary<string, IReadOnlyDictionary<string, string>>(_handlers, StringComparer.OrdinalIgnoreCase);
        var updatedBindings = new Dictionary<string, IReadOnlyDictionary<string, string>>(originalBindings, StringComparer.OrdinalIgnoreCase);
        foreach (var name in removedNames) updatedBindings.Remove(name);

        void Restore(string expected, string replacement, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> bindings)
        {
            var current = ResolveTransactionDocument(code);
            if (current.Text != expected) throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            current.Text = replacement;
            _handlers.Clear();
            foreach (var (field, handlers) in bindings) _handlers[field] = handlers;
        }

        await session.DeleteDropDownItemAsync(ownerName, itemName,
            () => Restore(before, after, updatedBindings),
            () => Restore(after, before, originalBindings),
            () => Restore(before, after, updatedBindings));
    }

    // Same prefix+suffix convention WinFormsDesignerView.MenuInline's NewMenuItemName already
    // uses for a MenuStrip's own Items ("ToolStripMenuItem1", not a caption-derived name),
    // generalised to the other kinds this nested arrow also offers (ToolStripComboBox,
    // ToolStripTextBox): one naming rule for every cascading drop-down tree, whichever kind of
    // item owns its root.
    private static string NewDropDownItemName(WinFormsMenuDocument document, string typeName)
    {
        var declared = document.DeclaredNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suffix = 1;
        while (declared.Contains(typeName + suffix))
        {
            suffix++;
        }
        return typeName + suffix;
    }

    /// <summary>Shows the Properties/Events panels for a nested item selected inside the
    /// drop-down panel, or restores the top-level selected item's own properties once that
    /// nested selection is cleared (mirrors <see cref="ShowMenuItemProperties"/> and
    /// <see cref="ShowToolStripItemProperties"/>, the two editors this one's selection sits
    /// alongside).</summary>
    private async void ShowToolStripDropDownItemProperties(string? itemName)
    {
        if (_restoringDropDownItemProperties)
        {
            return;
        }
        var generation = ++_dropDownItemPropertyGeneration;
        if (_session is not { } session || itemName is null || _inlineDropDownOwnerName is not { } ownerName)
        {
            _activeDropDownItemName = null;
            if (_session is not null && _inlineToolStripName is { } stripName)
            {
                _restoringDropDownItemProperties = true;
                try
                {
                    ShowToolStripItemProperties(stripName, _inlineToolStripEditor?.SelectedItemName);
                }
                finally
                {
                    _restoringDropDownItemProperties = false;
                }
            }
            return;
        }

        if (FindDropDownItem(session, ownerName, itemName) is not { } item)
        {
            return;
        }

        _activeDropDownItemName = itemName;
        var original = session.Document;
        try
        {
            var assignments = await Task.Run(() => original.Menus.ReadItemProperties(ownerName, itemName));
            if (generation != _dropDownItemPropertyGeneration || !ReferenceEquals(session, _session) ||
                !ReferenceEquals(original, session.Document) || !IsSelectedDropDownItem(ownerName, itemName) || _disposed)
            {
                return;
            }

            _properties.Show(item, property => assignments.ContainsKey(property)
                    ? ReadToolStripItemValue(item, property) : null,
                (property, text) => RunEditAsync(() => SetToolStripDropDownItemPropertyAsync(
                    session, original, ownerName, itemName, item, property, text)),
                componentName: itemName,
                nestedEdit: (property, text) => RunEditAsync(() => SetToolStripDropDownItemChildPropertyAsync(
                    session, original, ownerName, itemName, item, property, text)),
                editorRequested: property => OpenToolStripDropDownItemPropertyEditor(
                    session, original, ownerName, itemName, item, property),
                readOnlyProperties: ToolStripItemReadOnlyProperties);
            _events.Show(item, eventName => _handlers.GetValueOrDefault(itemName)?.GetValueOrDefault(eventName),
                (eventName, handler) => RunEditAsync(() =>
                {
                    RequireSelectedDropDownItem(session, original, ownerName, itemName);
                    return SetHandlerAsync(item, itemName, eventName, handler);
                }),
                eventName => itemName + "_" + eventName);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private Task SetToolStripDropDownItemPropertyAsync(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string ownerName, string itemName, Forms.ToolStripItem item, string property, string? text)
    {
        var descriptor = TypeDescriptor.GetProperties(item)[property]
            ?? throw new ArgumentException("The property does not exist.", nameof(property));
        var value = text is null ? null : descriptor.Converter.ConvertFromInvariantString(text);
        try
        {
            return ApplyToolStripDropDownItemPropertyAsync(session, original, ownerName, itemName, property,
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

    private Task SetToolStripDropDownItemChildPropertyAsync(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string ownerName, string itemName, Forms.ToolStripItem item, DesignableProperty property, string? text)
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
            return ApplyToolStripDropDownItemPropertyAsync(session, original, ownerName, itemName, property.ParentName!,
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

    private Task ApplyToolStripDropDownItemPropertyAsync(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string ownerName, string itemName, string property, string? expression)
    {
        RequireSelectedDropDownItem(session, original, ownerName, itemName);
        return session.ApplyCollectionAsync(document =>
            document.Menus.SetItemProperty(ownerName, itemName, property, expression));
    }

    private async void OpenToolStripDropDownItemPropertyEditor(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string ownerName, string itemName, Forms.ToolStripItem item, DesignableProperty property)
    {
        if (_busy || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }
        var offset = _properties.CapturePropertyEditorOffset();
        var generation = _dropDownItemPropertyGeneration;
        Font? selectedFont = null;
        try
        {
            var value = TypeDescriptor.GetProperties(item)[property.Name]?.GetValue(item);
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
            RunEditAsync(() => ApplyToolStripDropDownItemPropertyAsync(session, original, ownerName, itemName, property.Name, expression));
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            selectedFont?.Dispose();
            if (_attached && ReferenceEquals(_session, session) && generation == _dropDownItemPropertyGeneration)
            {
                _properties.RestoreSelectedPropertyFocus(offset);
            }
        }
    }

    private void InvalidateDropDownItemProperties()
    {
        ++_dropDownItemPropertyGeneration;
        _activeDropDownItemName = null;
    }

    private bool IsSelectedDropDownItem(string ownerName, string itemName) =>
        string.Equals(_inlineDropDownOwnerName, ownerName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(_activeDropDownItemName, itemName, StringComparison.OrdinalIgnoreCase);

    private void RequireSelectedDropDownItem(WinFormsDesignerSession session, WinFormsDesignerDocument original,
        string ownerName, string itemName)
    {
        if (!ReferenceEquals(_session, session) || !ReferenceEquals(session.Document, original) ||
            !IsSelectedDropDownItem(ownerName, itemName))
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        }
    }

    // A ToolStripDropDownButton/SplitButton item is not itself a Control WinFormsDesignerSession
    // tracks by name (only its owning ToolStrip is): find it by walking that ToolStrip's own
    // flat Items, the same way FindToolStripItem (see WinFormsDesignerView.ToolStripItemProperties)
    // does for a plain item, then its DropDownItems the same way FindMenuItem
    // (see WinFormsDesignerView.MenuItemProperties) walks a MenuStrip's.
    private static Forms.ToolStripDropDownItem? FindDropDownOwner(WinFormsDesignerSession session, string ownerName)
    {
        foreach (var control in session.Document.Controls)
        {
            if (session.FindControl(control.Name) is not Forms.ToolStrip strip || strip is Forms.MenuStrip)
            {
                continue;
            }
            foreach (Forms.ToolStripItem item in strip.Items)
            {
                if (item is Forms.ToolStripDropDownItem dropDown &&
                    string.Equals(item.Name, ownerName, StringComparison.OrdinalIgnoreCase))
                {
                    return dropDown;
                }
            }
        }
        return null;
    }

    private static Forms.ToolStripItem? FindDropDownItem(WinFormsDesignerSession session, string ownerName, string itemName)
    {
        if (FindDropDownOwner(session, ownerName) is not { } owner)
        {
            return null;
        }
        var pending = new Stack<Forms.ToolStripItem>(owner.DropDownItems.Cast<Forms.ToolStripItem>());
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
