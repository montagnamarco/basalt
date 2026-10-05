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
            _surface.ShowInlineMenuEditor(null, null);
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
