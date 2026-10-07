using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private WinFormsMenuDesignEditor? _inlineMenuEditor;
    private string? _inlineMenuStripName;
    private WinFormsDesignerDocument? _inlineMenuSnapshot;

    private void InitializeInlineMenuEditor()
    {
        _surface.SelectionChanged += RefreshInlineMenuEditor;
    }

    private void RefreshInlineMenuEditor(Forms.Control? selected)
    {
        if (_session is not { } session || selected is not Forms.MenuStrip strip ||
            !ReferenceEquals(_surface.SelectedControl, strip))
        {
            // A plain ToolStrip/StatusStrip (also a ToolStrip subclass, but not a MenuStrip)
            // owns the shared inline-overlay slot through RefreshInlineToolStripEditor, which
            // runs right after this handler on the same SelectionChanged event: clearing the
            // slot here too would remove what that handler is about to show.
            if (selected is not Forms.ToolStrip || selected is Forms.MenuStrip)
            {
                _surface.ShowInlineMenuEditor(null, null);
            }
            _inlineMenuStripName = null;
            _inlineMenuSnapshot = null;
            return;
        }

        var stripName = session.NameOf(strip);
        _inlineMenuEditor ??= CreateInlineMenuEditor();
        _inlineMenuStripName = stripName;
        _inlineMenuSnapshot = session.Document;

        try
        {
            var menu = session.Document.Menus;
            _inlineMenuEditor.Show(stripName, menu.Read(stripName), strip.Width);
        }
        catch (NotSupportedException)
        {
            _inlineMenuEditor.Show(stripName, [], strip.Width, unsupported: true);
        }

        _surface.ShowInlineMenuEditor(_inlineMenuEditor, strip);
    }

    private WinFormsMenuDesignEditor CreateInlineMenuEditor()
    {
        var editor = new WinFormsMenuDesignEditor();
        editor.EditRequested += change =>
        {
            if (!_busy)
            {
                RunEditAsync(() => ApplyInlineMenuEditAsync(change));
            }
        };
        editor.SelectedItemChanged += itemName =>
        {
            if (_inlineMenuStripName is { } stripName && _session is not null)
            {
                ShowMenuItemProperties(stripName, itemName);
            }
        };
        editor.DefaultEventRequested += itemName =>
        {
            if (_inlineMenuStripName is { } stripName &&
                _inlineMenuEditor?.SelectedItemName == itemName &&
                _surface.SelectedControl is Forms.MenuStrip strip &&
                _session?.NameOf(strip) == stripName)
            {
                RunEditAsync(() => OpenMenuItemDefaultEventAsync(stripName, itemName));
            }
        };
        return editor;
    }

    private Task ApplyInlineMenuEditAsync(WinFormsMenuDesignChange change)
    {
        if (_session is not { } session || _inlineMenuStripName is not { } stripName ||
            !ReferenceEquals(_inlineMenuSnapshot, session.Document) ||
            _surface.SelectedControl is not Forms.MenuStrip selectedStrip ||
            session.NameOf(selectedStrip) != stripName)
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        }

        var original = session.Document;
        var menu = original.Menus;
        var items = menu.Read(stripName).ToList();

        if (change.Action == WinFormsMenuDesignAction.DeleteItem)
        {
            return DeleteInlineMenuItemAsync(session, stripName, change.Name!, items);
        }

        switch (change.Action)
        {
            case WinFormsMenuDesignAction.AddItem:
            case WinFormsMenuDesignAction.AddSeparator:
                var separator = change.Action == WinFormsMenuDesignAction.AddSeparator;
                var name = NewMenuItemName(menu, separator);
                items.Add(new WinFormsMenuItem(name, change.Parent,
                    separator ? "" : change.Text ?? "", separator));
                _inlineMenuEditor?.SelectPendingItem(name);
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
            return current.Menus.Set(stripName, items);
        });
    }

    /// <summary>Deletes a menu item (and its whole subtree) through <see
    /// cref="WinFormsDesignerSession.DeleteMenuItemAsync"/>: one undo step covering the
    /// Designer.vb declarations, any resx entries they owned, and (when a companion code file is
    /// open) the companion's own Handles clauses for every removed name, the same pattern <see
    /// cref="DeleteSelectedControlsAsync"/> already uses for a deleted control. Selects the
    /// neighbouring item - Visual Studio's own rule - before the deletion commits, so the
    /// Properties panel already targets it once the editor's next Show() call arrives.</summary>
    private Task DeleteInlineMenuItemAsync(WinFormsDesignerSession session, string stripName, string itemName,
        List<WinFormsMenuItem> itemsBeforeRemoval)
    {
        _inlineMenuEditor?.SelectPendingItem(
            WinFormsMenuTreeHelpers.SelectionAfterRemoval(itemsBeforeRemoval, itemName, stripName));

        if (_getCode is null || (!File.Exists(CompanionPath) && _findCode?.Invoke(CompanionPath) is null))
        {
            return session.DeleteMenuItemAsync(stripName, itemName);
        }

        return DeleteInlineMenuItemWithCompanionAsync(session, stripName, itemName);
    }

    private async Task DeleteInlineMenuItemWithCompanionAsync(WinFormsDesignerSession session, string stripName, string itemName)
    {
        var removedNames = session.Document.Menus.DescendantNames(stripName, itemName);
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

        await session.DeleteMenuItemAsync(stripName, itemName,
            () => Restore(before, after, updatedBindings),
            () => Restore(after, before, originalBindings),
            () => Restore(before, after, updatedBindings));
    }

    private static string NewMenuItemName(WinFormsMenuDocument document, bool separator)
    {
        var prefix = separator ? "ToolStripSeparator" : "ToolStripMenuItem";
        var declared = document.DeclaredNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suffix = 1;
        while (declared.Contains(prefix + suffix))
        {
            suffix++;
        }
        return prefix + suffix;
    }
}
