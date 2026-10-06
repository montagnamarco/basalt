using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private WinFormsToolStripDesignEditor? _inlineToolStripEditor;
    private string? _inlineToolStripName;
    private WinFormsDesignerDocument? _inlineToolStripSnapshot;

    private void InitializeInlineToolStripEditor()
    {
        _surface.SelectionChanged += RefreshInlineToolStripEditor;
    }

    private void RefreshInlineToolStripEditor(Forms.Control? selected)
    {
        if (_session is not { } session || selected is not Forms.ToolStrip strip ||
            strip is Forms.MenuStrip or Forms.ToolStripDropDown || !ReferenceEquals(_surface.SelectedControl, strip))
        {
            // A MenuStrip owns the shared inline-overlay slot through RefreshInlineMenuEditor,
            // which runs right before this handler on the same SelectionChanged event: clearing
            // the slot here too would remove the menu editor that handler just showed.
            // A drop-down (ContextMenuStrip) is a ToolStrip too, but its items are a menu's.
            if (selected is not Forms.MenuStrip)
            {
                _surface.ShowInlineToolStripEditor(null, null);
            }
            _inlineToolStripName = null;
            _inlineToolStripSnapshot = null;
            return;
        }

        var stripName = session.NameOf(strip);
        _inlineToolStripEditor ??= CreateInlineToolStripEditor();
        _inlineToolStripName = stripName;
        _inlineToolStripSnapshot = session.Document;
        var statusStrip = strip is Forms.StatusStrip;

        try
        {
            var items = session.Document.ToolStripItems;
            _inlineToolStripEditor.Show(stripName, items.Read(stripName), statusStrip, strip.Width);
        }
        catch (NotSupportedException)
        {
            _inlineToolStripEditor.Show(stripName, [], statusStrip, strip.Width, unsupported: true);
        }

        _surface.ShowInlineToolStripEditor(_inlineToolStripEditor, strip);
    }

    private WinFormsToolStripDesignEditor CreateInlineToolStripEditor()
    {
        var editor = new WinFormsToolStripDesignEditor();
        editor.EditRequested += change =>
        {
            if (!_busy)
            {
                RunEditAsync(() => ApplyInlineToolStripEditAsync(change));
            }
        };
        editor.SelectedItemChanged += itemName =>
        {
            if (_inlineToolStripName is { } stripName && _session is not null)
            {
                ShowToolStripItemProperties(stripName, itemName);
            }
        };
        return editor;
    }

    private Task ApplyInlineToolStripEditAsync(WinFormsToolStripDesignChange change)
    {
        if (_session is not { } session || _inlineToolStripName is not { } stripName ||
            !ReferenceEquals(_inlineToolStripSnapshot, session.Document) ||
            _surface.SelectedControl is not Forms.ToolStrip selectedStrip ||
            session.NameOf(selectedStrip) != stripName)
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        }

        var original = session.Document;
        var toolStripItems = original.ToolStripItems;
        var items = toolStripItems.Read(stripName).ToList();
        var name = NewToolStripItemName(toolStripItems, change.TypeName);
        items.Add(new WinFormsToolStripItem(name, change.TypeName, change.Text));
        _inlineToolStripEditor?.SelectPendingItem(name);

        return session.ApplyCollectionAsync(current =>
        {
            if (!ReferenceEquals(current, original))
            {
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            }
            return current.ToolStripItems.Set(stripName, items);
        });
    }

    private static string NewToolStripItemName(WinFormsToolStripDocument document, string typeName)
    {
        var declared = document.DeclaredNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suffix = 1;
        while (declared.Contains(typeName + suffix))
        {
            suffix++;
        }
        return typeName + suffix;
    }
}
